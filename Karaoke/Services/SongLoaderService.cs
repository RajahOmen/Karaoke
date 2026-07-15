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
    }.ToFrozenDictionary();

    private const string TranslationTag = "tr";

    private const int DefaultPriority = 2;
    private static readonly FrozenDictionary<string, int> SubcategoryPriority = new Dictionary<string, int>()
    {
        { "official", DefaultPriority - 1 },
        { "unofficial", DefaultPriority - 2 },
    }.ToFrozenDictionary();
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
                var (songLyrics, songTags) = parseLyricFileLines(lyricLines, song.Id);

                if (getSongFileCateogry(lyricsFile, pluginInterface.ConfigDirectory) is string category)
                    songTags[SongTag.LyricCategory] = category;

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
        List<Task> extractTasks = [];
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
                continue;
            pluginLog.Verbose($"Loading repo file: {entry.FullName}");
            var path = Path.Combine(lyricsDirectory, entry.FullName.Replace(LyricRepoRootDir, ""));
            new FileInfo(path).Directory?.Create();
            extractTasks.Add(entry.ExtractToFileAsync(path, overwrite: true, cancellationToken: cancellationToken));
        }
        await Task.WhenAll(extractTasks);
    }

    private static string? getSongFileCateogry(string filePath, DirectoryInfo configDir)
    {
        if (
            Path.GetRelativePath(configDir.FullName, filePath) is string path and { Length: > 0 }
            && !path.StartsWith("..")
            && !Path.IsPathRooted(path)
            && Path.GetDirectoryName(path) is string subdirectories
            && !subdirectories.IsNullOrEmpty()
            && subdirectories.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is string[] subDirs and { Length: > 1 }
        )
            return subDirs[1];
        return null;
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

        var bgmIdFileMapping = new Dictionary<uint, List<(string FileName, int Priority)>>();

        var tasks = files.Select(f => parseBgmIdsFromFile(f, cancellationToken));
        var bgmIds = await Task.WhenAll(tasks);

        pluginLog.Verbose($"Song Id => [Priority] Lyric File:");
        for (var i = 0; i < files.Length; i++)
        {
            var fileName = files[i];
            foreach (var bgmId in bgmIds[i])
            {
                var priority = SubcategoryPriority.GetValueOrDefault(
                    getSongFileCateogry(fileName, pluginInterface.ConfigDirectory) ?? "",
                    DefaultPriority
                );
                pluginLog.Verbose($"{bgmId} => [{priority}] {fileName}");

                if (!bgmIdFileMapping.TryGetValue(bgmId, out var lyricFiles))
                {
                    lyricFiles = [];
                    bgmIdFileMapping[bgmId] = lyricFiles;
                }

                lyricFiles.Add((fileName, priority));
            }
        }

        var bgmIdOrderedMapping = new Dictionary<uint, List<string>>();
        foreach (var (bgmId, bgmFileList) in bgmIdFileMapping)
        {
            bgmIdOrderedMapping[bgmId] = bgmFileList
                .OrderByDescending(bgm => bgm.Priority)
                .Select(bgm => bgm.FileName)
                .ToList();
        }

        return bgmIdOrderedMapping;
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

    private (List<LyricLine> Lyrics, Dictionary<SongTag, object> Tags) parseLyricFileLines(List<string> lyricLines, uint songId)
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
                            var parsedVal = val;
                            if (parseFunc is not null)
                            {
                                pluginLog.Debug($"Parsing tag values of {match.Value}");
                                parsedVal = parseTagVal(
                                    match.Groups[2].Value,
                                    parseFunc,
                                    songId
                                );
                            }

                            if (parsedVal is not null)
                                tags.Add(type, parsedVal);
                            else
                                pluginLog.Debug($"No value for song id {songId} found in value {match.Value}");
                        }
                        catch (Exception ex)
                        {
                            pluginLog.Warning($"Unable to parse song tag value '{val}' (type: '{type}'): {line}\n{ex.Message}");
                        }
                    }
                    else if (tagTypeStr == TranslationTag)
                    {
                        if (lyrics.Count > 0 && lyrics[^1].TranslatedText is null)
                        {
                            lyrics[^1] = lyrics[^1] with { TranslatedText = match.Groups[2].Value };
                        }
                        else
                        {
                            pluginLog.Warning($"Invalid translation tag (line {lineIdx}/{lyricLines.Count - 1}), previous lyric not found or already has a translation");
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
        SongLoopData songLoopData,
        bool loadLyrics = true
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

        if (loadLyrics)
        {
            cancelLoadLyrics.Cancel();
            cancelLoadLyrics = new();
            _ = Task.Run(
                async () => await loadSongLyricsAsync(song, cancelLoadLyrics.Token),
                cancelLoadLyrics.Token
            );
        }

        return song;
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
            ? -1 : 1;
        var seconds = float.Parse(offsetStr.TrimStart('+', '-'));
        return seconds * direction;
    }

    private object? parseTagVal(string tagValStr, Func<string, object> valParseFunc, uint songId)
    {
        if (PerIdTagRegex().Matches(tagValStr) is not { Count: > 0 } matches)
        {
            pluginLog.Debug($"Parsed non-id-specific tag value: {tagValStr}");
            return valParseFunc(tagValStr);
        }

        for (var i = 0; i < matches.Count; i++)
        {
            var parsedSongId = uint.Parse(matches[i].Groups[1].Value);
            if (parsedSongId == songId)
            {
                pluginLog.Debug($"Parsed id-specific tag value: {matches[i].Value}");
                return valParseFunc(matches[i].Groups[2].Value);
            }
            else
            {
                pluginLog.Debug($"Found value for wrong song id: {matches[i].Value}");
            }
        }
        return null;
    }

    [GeneratedRegex(@"([0-9]*):([^;\]]*)")]
    private static partial Regex BgmIdOffsetRegex();

    [GeneratedRegex(@"([0-9]*):([^;\]]*)")]
    private static partial Regex PerIdTagRegex();

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
