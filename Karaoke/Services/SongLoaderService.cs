using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Karaoke.Services;

using TagParseEntry = (
    SongTag TagType,
    Func<string, object>? TagParseFunc
);

public partial class SongLoaderService(
    IDalamudPluginInterface pluginInterface,
    IPluginLog pluginLog,
    SongNameService songNameService,
    HttpClient httpClient,
    Configuration configuration
    ) : IHostedService
{
    private const string LyricFileExtension = ".lrc";

    private const string LyricRepoUrl = @"https://github.com/RajahOmen/karaoke-lyrics/archive/main.zip";
    private const string LyricRepoRootDir = @"karaoke-lyrics-main/";
    private readonly string lyricsDirectory = $"{pluginInterface.ConfigDirectory}\\lyrics";
    private readonly IPluginLog pluginLog = pluginLog;
    private readonly SongNameService songNameService = songNameService;
    private readonly HttpClient httpClient = httpClient;
    private readonly Configuration configuration = configuration;
    private CancellationTokenSource cancelLoadLyrics = new();

    public event Action? OnLyricLoad;

    private static readonly FrozenDictionary<string, TagParseEntry> SongTagLabels = new Dictionary<string, TagParseEntry>()
    {
        // lrc file tags
        { "ti", (SongTag.Name, null) },         // overrides spreadsheet-defined value
        { "ar", (SongTag.Artist, null) },
        { "lr", (SongTag.Lyricist, null) },
        { "au", (SongTag.SongAuthor, null) },
        { "al", (SongTag.Album, null) },
        { "by", (SongTag.LrcAuthor, null) },
        { "length", (SongTag.Duration, s => Util.ParseTime(s)) }, // overrides game-defined, hh:mm.x[xxx] format
        { "offset", (SongTag.Offset, parseGlobalOffset) },
        { "#", (SongTag.Comment, null) },

        // custom tags
        { "loop", (SongTag.LoopLineIndex, s => int.Parse(s)) },
        { "ids", (SongTag.BgmIds, parseBgmIds) },
        { "idoffsets", (SongTag.BgmIdOffsets, parseBgmIdOffsets) }
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, int> SubcategoryPriority = new Dictionary<string, int>()
    {
        { "official", 1 },
        { "unofficial", 0 },
    }.ToFrozenDictionary();
    private const int DefaultPriority = 2;
    private Dictionary<uint, List<string>> songLyricFiles = [];


    public bool ReloadingLyrics { get; private set; }
    public void ReloadLyrics(Song? currentSong)
    {
        if (ReloadingLyrics)
            return;

        ReloadingLyrics = true;

        _ = Task.Run(async () =>
        {
            try
            {
                songLyricFiles = await readAllLyricFiles(CancellationToken.None);
                if (currentSong is not null)
                    await loadSongLyricsAsync(currentSong, CancellationToken.None);
            }
            finally
            {
                ReloadingLyrics = false;
            }
        });
    }

    private async Task loadSongLyricsAsync(
        Song song,
        CancellationToken cancellationToken
        )
    {
        pluginLog.Verbose($"Loading lyrics for [{song.Id}] \"{song.Name}\"");

        if (!songLyricFiles.TryGetValue(song.Id, out var lyricsFiles) || lyricsFiles.Count == 0)
        {
            pluginLog.Debug($"No lyric file mapping found for song {song.Id} ('{song.Name}')");
            try
            {
                song.LoadLyricsAndTags([], [], lyricFileName: null, []);
            }
            catch (Exception ex) when (ex is not TaskCanceledException)
            {
                pluginLog.Error(ex, "Error loading EMPTY lyrics/tags");
            }
            return;
        }

        var lyricsFile = lyricsFiles[0];
        if (configuration.BgmIdLyricFileLoads.TryGetValue(song.Id, out var fileNameOverride))
        {
            if (lyricsFiles.Contains(fileNameOverride))
            {
                lyricsFile = fileNameOverride;
            }
            else
            {
                // filename/path doesn't exist anymore, remove this setting
                configuration.BgmIdLyricFileLoads.Remove(song.Id);
                configuration.Save();
            }
        }

        List<string> lyricLines = [];
        try
        {
            try
            {
                pluginLog.Debug($"Opening lyric file {lyricsFile} for song {song.Id}");
                lyricLines = (await File.ReadAllLinesAsync(lyricsFile, cancellationToken)).ToList();
            }
            catch (Exception ex) when (ex is not TaskCanceledException)
            {
                pluginLog.Warning($"Error opening/reading lyric file {lyricsFile}:\n{ex.Message}\n{ex.StackTrace}");
            }

            try
            {
                var (songLyrics, songTags) = parseLyricFileLines(lyricLines);

                if (new FileInfo(lyricsFile).Directory?.Name is string subdirName)
                    songTags[SongTag.LyricCategory] = subdirName;

                pluginLog.Debug($"=== PARSE '{song.Name}' [{lyricsFile}] ===");
                pluginLog.Debug($"==== TAGS ====");
                foreach (var (tagType, tagVal) in songTags)
                    pluginLog.Debug($"{Enum.GetName(tagType)}:'{tagVal}'");
                pluginLog.Debug($"=== LYRICS ===");
                foreach (var lyric in songLyrics)
                    pluginLog.Debug(lyric.ToString());
                pluginLog.Debug("=== PARSE END ===");

                if (cancellationToken.IsCancellationRequested)
                    throw new TaskCanceledException();
                try
                {
                    pluginLog.Verbose($"LoopStart, LoopLyricIdx: {song.LoopStart}, {song.LoopLyricIdx}, {songLyrics.Count}");
                    song.LoadLyricsAndTags([.. songLyrics], songTags, lyricsFile, lyricsFiles.ToArray());
                    pluginLog.Debug("=== SONG INFO ===");
                    if (song.LoopLyricIdx >= 0)
                    {
                        var songLoopLyric = (song.Lyrics?.Length ?? -1) > song.LoopLyricIdx
                            ? (song.Lyrics?[song.LoopLyricIdx].ToString() ?? "???")
                            : "No Lyrics Loaded";
                        pluginLog.Debug($"[{Util.FormatTime(song.LoopStart ?? -1, 4)} / {song.LoopLyricIdx}] => {songLoopLyric}");
                    }
                    else
                    {
                        pluginLog.Debug($"Song does not have a looped lyric");
                    }

                    for (var i = 0; i < (song.Lyrics?.Length ?? 0); i++)
                        pluginLog.Debug($"[{i}] {song.Lyrics![i].ToString()}");

                    pluginLog.Debug($"Song metadata: Duration: {Util.FormatTime(song.Duration, 2)}, LoopStart: {Util.FormatTime(song.LoopStart ?? -1, 2)}, LoopLyricIdx: {song.LoopLyricIdx}");

                    OnLyricLoad?.Invoke();
                }
                catch (Exception ex) when (ex is not TaskCanceledException)
                {
                    pluginLog.Error(ex, "Error loading lyrics/tags");
                }
            }
            catch (Exception ex)
            {
                pluginLog.Warning($"Error parsing lyric file {lyricsFile}:\n{ex.Message}\n{ex.StackTrace}");
            }
        }
        catch (TaskCanceledException)
        {
            pluginLog.Debug($"Lyric load task for [{song.Id}] \"{song.Name}\" cancelled");
        }
    }

    private async Task loadLyricsFromRepo(CancellationToken cancellationToken)
    {
        pluginLog.Info($"Fetching lyrics from repo: {LyricRepoUrl}");
        var contents = await httpClient.GetStreamAsync(LyricRepoUrl, cancellationToken);
        var archive = new ZipArchive(contents, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
                continue;
            pluginLog.Verbose($"Loading repo file: {entry.FullName}");
            var path = Path.Combine(lyricsDirectory, entry.FullName.Replace(LyricRepoRootDir, ""));
            new FileInfo(path).Directory?.Create();
            await entry.ExtractToFileAsync(path, overwrite: true, cancellationToken: cancellationToken);
        }
    }

    private async Task<Dictionary<uint, List<string>>> readAllLyricFiles(CancellationToken cancellationToken)
    {
        // download updated files from github lyrics repo
        await loadLyricsFromRepo(cancellationToken);

        var files = Directory.GetFiles(
            lyricsDirectory,
            $"*{LyricFileExtension}",
            new EnumerationOptions() { RecurseSubdirectories = true }
        );

        var bgmIdFileMapping = new Dictionary<uint, List<string>>();

        var tasks = files.Select(f => parseBgmIdsFromFile(f, cancellationToken));
        var bgmIds = await Task.WhenAll(tasks);

        pluginLog.Verbose($"Song Id => Lyric Files:");
        for (var i = 0; i < files.Length; i++)
        {
            var fileName = files[i];
            foreach (var bgmId in bgmIds[i])
            {
                pluginLog.Verbose($"{bgmId} => {fileName}");

                if (!bgmIdFileMapping.TryGetValue(bgmId, out var lyricFiles))
                {
                    lyricFiles = [];
                    bgmIdFileMapping[bgmId] = lyricFiles;
                }
                lyricFiles.Add(fileName);
            }
        }

        foreach (var (bgmId, bgmFileList) in bgmIdFileMapping)
        {
            bgmIdFileMapping[bgmId] = bgmFileList
                .OrderByDescending(f =>
                    SubcategoryPriority.GetValueOrDefault(new FileInfo(f).Directory?.Name ?? "", DefaultPriority)
                ).ToList();
        }

        return bgmIdFileMapping;
    }

    private async Task<uint[]> parseBgmIdsFromFile(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var lines = File.ReadLinesAsync(filePath, cancellationToken);
            await foreach (var line in lines)
            {
                if (TagRegex().Match(line) is not { Success: true } match)
                    continue;

                var tag = match.Groups[1].Value;
                if (!SongTagLabels.TryGetValue(tag, out var tagParseEntry))
                    continue;

                if (tagParseEntry.TagType != SongTag.BgmIds)
                    continue;

                if (tagParseEntry.TagParseFunc is null)
                    throw new Exception("Song id tag parse must be a func, is null");

                var idsStr = match.Groups[2].Value;
                if (tagParseEntry.TagParseFunc(match.Groups[2].Value) is not uint[] ids)
                    throw new Exception($"Song id parse func returned invalid result. Value: '{idsStr}'");

                return ids;
            }

            throw new Exception($"Expecting a song ids tag (ex: [ids:1;2;3])");
        }
        catch (Exception ex) when (ex is not TaskCanceledException)
        {
            pluginLog.Warning(ex, $"Error reading lrc file '{filePath}'");
            return [];
        }
    }

    private (List<LyricLine> Lyrics, Dictionary<SongTag, object> Tags) parseLyricFileLines(List<string> lyricLines)
    {
        List<LyricLine> lyrics = [];
        Dictionary<SongTag, object> tags = [];

        for (var lineIdx = 0; lineIdx < lyricLines.Count; lineIdx++)
        {
            var line = lyricLines[lineIdx];
            if (line.IsNullOrWhitespace())
                continue;

            if (TagRegex().Matches(line) is { Count: > 0 } matches)
            {
                foreach (Match match in matches)
                {
                    var tagTypeStr = match.Groups[1].Value;
                    if (SongTagLabels.TryGetValue(tagTypeStr, out var tagParseEntry))
                    {
                        var (type, parseFunc) = tagParseEntry;
                        object val = match.Groups[2].Value;
                        try
                        {
                            if (parseFunc is not null)
                                val = parseFunc(match.Groups[2].Value);
                            tags.Add(type, val);
                        }
                        catch (Exception ex)
                        {
                            pluginLog.Warning($"Unable to parse song tag value '{val}' (type: '{type}'): {line}\n{ex.Message}");
                        }
                    }
                    else
                    {
                        pluginLog.Warning($"Unable to parse song tag (line {lineIdx}/{lyricLines.Count - 1}), unknown type '{tagTypeStr}': {line}");
                    }
                }
            }
            else
            {
                try
                {
                    lyrics.Add(new LyricLine(line));
                }
                catch (Exception ex)
                {
                    pluginLog.Warning($"Unable to parse lyric (line {lineIdx}/{lyricLines.Count - 1}), '{line}'\n{ex.Message}");
                }
            }
        }

        return (lyrics, tags);
    }

    public Song GetSongById(
        uint songId,
        SongLoopData songLoopData
    )
    {
        var songName = songNameService.GetSongName(songId);
        pluginLog.Debug($"Song Id {songId} has name \'{songName}\'");

        var song = new Song(
            id: songId,
            name: songName,
            duration: songLoopData.TotalDurationMillis / 1000f,
            loopStart: songLoopData.LoopStartMillis / 1000f
        );

        cancelLoadLyrics.Cancel();
        cancelLoadLyrics = new();
        _ = Task.Run(
            async () => await loadSongLyricsAsync(song, cancelLoadLyrics.Token),
            cancelLoadLyrics.Token
        );

        return song;
    }

    private static object parseBgmIdOffsets(string offsetStr)
    {
        if (BgmIdOffsetRegex().Matches(offsetStr) is not { Count: > 0 } matches)
            throw new FormatException($"No offset strings found for tag: '{offsetStr}'");

        var offsets = new (uint Id, float Offset)[matches.Count];
        for (var i = 0; i < matches.Count; i++)
        {
            var id = uint.Parse(matches[i].Groups[1].Value);
            var offset = float.Parse(matches[i].Groups[2].Value);
            offsets[i] = (id, offset);
        }
        return offsets;
    }

    private static object parseBgmIds(string idsStr)
    {
        var idStrs = idsStr.Split(';');
        var ids = new uint[idStrs.Length];
        for (var i = 0; i < ids.Length; i++)
            ids[i] = uint.Parse(idStrs[i]);

        return ids;
    }

    private static object parseGlobalOffset(string offsetStr)
    {
        var direction = offsetStr.StartsWith('-')
            ? 1 : -1;
        var seconds = float.Parse(offsetStr.TrimStart('+', '-'));
        return seconds * direction;
    }

    [GeneratedRegex(@"([0-9]*):([^;\]]*)")]
    private static partial Regex BgmIdOffsetRegex();


    [GeneratedRegex(@"^\[([a-z,A-Z,#]*):([^]]*)\]")]
    private static partial Regex TagRegex();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ReloadingLyrics = true;
        try
        {
            songLyricFiles = await readAllLyricFiles(cancellationToken);
        }
        finally
        {
            ReloadingLyrics = false;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
