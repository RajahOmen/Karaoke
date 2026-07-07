using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Karaoke.Services;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Karaoke.Windows;

public class LyricPlayerWindow : Window, IDisposable
{
    private readonly IPluginLog pluginLog;
    private readonly Configuration configuration;
    private readonly BGMService bgmService;
    private readonly SongLoaderService songLoaderService;
    private readonly IDalamudPluginInterface pluginInterface;
    public bool OpenedManually = false;

    private const int DefaultWidth = 300;
    private const int MinBufferWidth = 40;

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public LyricPlayerWindow(
        IPluginLog pluginLog,
        Configuration configuration,
        BGMService bgmService,
        SongLoaderService songLoaderService,
        IDalamudPluginInterface pluginInterface
    ) : base("Karaoke###karaoke_lyric_player")
    {
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        if (configuration.LyricWindowNoTitleBar)
        {
            Flags |= ImGuiWindowFlags.NoTitleBar;
        }
        if (configuration.LyricWindowBackgroundOpacity is not null)
        {
            BgAlpha = configuration.LyricWindowBackgroundOpacity;
        }

        this.AllowPinning = true;
        this.AllowClickthrough = true;
        Size = new Vector2(DefaultWidth, 0);
        SizeCondition = ImGuiCond.Always;
        this.pluginLog = pluginLog;
        this.configuration = configuration;
        this.bgmService = bgmService;
        this.songLoaderService = songLoaderService;
        this.pluginInterface = pluginInterface;
        this.bgmService.OnSongChange += handleOnSongChange;
        this.songLoaderService.OnLyricLoad += handleOnLyricLoad;
    }

    public void Dispose()
    {
        bgmService.OnSongChange -= handleOnSongChange;
        songLoaderService.OnLyricLoad -= handleOnLyricLoad;
    }

    private void openWithSong(Song song)
    {
        var maxLength = Math.Max(DefaultWidth * ImGuiHelpers.GlobalScale, ImGui.CalcTextSize(song.Name).X + MinBufferWidth);
        foreach (var lyric in song.Lyrics ?? [])
            maxLength = Math.Max(maxLength, ImGui.CalcTextSize(lyric.Text).X + MinBufferWidth);

        Size = new Vector2(maxLength / ImGuiHelpers.GlobalScale, 0);
        IsOpen = true;
    }

    private void handleOnLyricLoad()
    {
        var prevState = IsOpen;

        try
        {
            if (bgmService.CurrentSong is not Song song)
                return;

            if (configuration.OpenWindowOn == OpenWindowOn.None)
                return;

            if ((song.Lyrics?.Length ?? 0) == 0)
                return;

            openWithSong(song);
        }
        finally
        {
            pluginLog.Debug($"handleOnLyricLoad: {prevState} => {IsOpen}. [{OpenedManually}][{bgmService.CurrentSong}]");
        }
    }

    private void handleOnSongChange()
    {
        var prevState = IsOpen;

        try
        {
            if (OpenedManually)
                return;
            if (bgmService.CurrentSong is not Song song)
            {
                IsOpen = false;
            }
            else
            {
                if (
                    configuration.OpenWindowOn == OpenWindowOn.SongChangeLyrics
                    && (bgmService.CurrentSong.Lyrics?.Length ?? 0) == 0
                )
                    IsOpen = false;
                else if (configuration.OpenWindowOn != OpenWindowOn.None)
                {
                    openWithSong(song);
                }
            }

        }
        finally
        {
            pluginLog.Debug($"handleOnSongChange: {prevState} => {IsOpen}. [{OpenedManually}][{bgmService.CurrentSong}]");
        }
    }

    public override void PreDraw() { }

    private const int MUSIC_LINE_IDX = -2;

    private void drawLyrics(Song song, float lyricTime)
    {
        var curLyricIdx = song.GetLyricIdxAtTime(lyricTime);
        if (curLyricIdx < 0 || song.Lyrics is null)
            return;

        var lyricCount = song.Lyrics.Length;
        var noMoreLyrics = curLyricIdx == lyricCount;

        LyricLine? curLyric = !noMoreLyrics ? song.Lyrics[curLyricIdx] : null;

        var ahead = configuration.NumLyricsAhead;
        var behind = configuration.NumLyricsBehind;
        var thresh = configuration.MusicLineInsertThreshold;

        var lyricIdxs = new int[behind + ahead + 1];
        lyricIdxs[behind] = curLyricIdx;
        var aheadIdxStart = (int)behind + 1;
        var behindIdxStart = (int)behind - 1;

        var alreadyLooped = bgmService.TotalElapsedTime - configuration.GlobalLyricDelay >= song.Lyrics[^1].StartTime;
        var prevLyricIdx = song.GetNextLyricIdx(
            curLyricIdx, reverse: true, wrapToEnd: alreadyLooped
        );
        float emptyTime;
        if (prevLyricIdx < 0)
            emptyTime = curLyric?.StartTime ?? 0;
        else
            emptyTime = song.Lyrics[prevLyricIdx].TimeUntilNext;

        if (!noMoreLyrics && song.LoopStart is float loopStart && lyricTime > song.Lyrics[curLyricIdx].EndTime)
            lyricTime = loopStart - (song.Duration - lyricTime);

        if (emptyTime > thresh)
        {
            // currently in inactive time
            if (lyricTime < (curLyric?.StartTime ?? 0))
            {
                lyricIdxs[behind] = MUSIC_LINE_IDX;
                if (ahead > 0)
                {
                    lyricIdxs[aheadIdxStart] = curLyricIdx;
                    aheadIdxStart++;
                }
            }
            // had inactive time before this lyric
            else
            {
                if (behind > 0)
                {
                    lyricIdxs[behindIdxStart] = MUSIC_LINE_IDX;
                    behindIdxStart--;
                }
            }
        }

        if ((curLyric?.TimeUntilNext ?? 0) > thresh && ahead > 0)
        {
            lyricIdxs[aheadIdxStart] = MUSIC_LINE_IDX;
            aheadIdxStart++;
        }


        var lastCheckedIdx = curLyricIdx;

        for (var i = behindIdxStart; i >= 0; i--)
        {
            var newIdx = song.GetNextLyricIdx(lastCheckedIdx, reverse: true, wrapToEnd: alreadyLooped);
            lyricIdxs[i] = newIdx;
            if (lyricIdxs[i + 1] >= 0)
            {
                if (newIdx >= 0)
                {
                    if (song.Lyrics[newIdx].TimeUntilNext > thresh)
                    {
                        lyricIdxs[i] = MUSIC_LINE_IDX;
                        if (i > 0)
                        {
                            lyricIdxs[i - 1] = newIdx;
                            i--;
                        }
                    }
                } else if (song.Lyrics[lastCheckedIdx].StartTime > thresh)
                {
                    lyricIdxs[i] = MUSIC_LINE_IDX;
                }
            }
            lastCheckedIdx = newIdx;
        }

        lastCheckedIdx = curLyricIdx;

        for (var j = aheadIdxStart; j < lyricIdxs.Length; j++)
        {
            var newIdx = song.GetNextLyricIdx(lastCheckedIdx, reverse: false);
            lyricIdxs[j] = newIdx;
            lastCheckedIdx = newIdx;
            if (newIdx > 0 && song.Lyrics[newIdx].TimeUntilNext > thresh && j < lyricIdxs.Length - 1)
            {
                lyricIdxs[j + 1] = MUSIC_LINE_IDX;
                j++;
            }
        }


        for (var i = 0; i < lyricIdxs.Length; i++)
        {
            var lyricIdx = lyricIdxs[i];

            if (i == behind)
            {
                if (lyricIdx == MUSIC_LINE_IDX)
                {
                    LyricLine? prevLyricVal = prevLyricIdx >= 0
                        ? song.Lyrics[prevLyricIdx]
                        : null;

                    float startTime;
                    if (prevLyricVal is not LyricLine prevLyric)
                    {
                        startTime = 0;
                    }
                    else if ((curLyric?.StartTime ?? 0) >= prevLyric.StartTime)
                    {
                        startTime = prevLyric.EndTime;
                    }
                    else
                    {
                        startTime = (curLyric?.StartTime ?? 0) - prevLyric.TimeUntilNext;
                    }

                    Components.DrawCurrentText(
                        configuration.MusicLineInsert,
                        lyricTime,
                        startTime,
                        endTime: curLyric?.StartTime ?? 0,
                        configuration.DebugMode,
                        configuration.HighlightLyrics
                        );
                    continue;
                }

                if (curLyric is LyricLine lyric)
                {
                    Components.DrawCurrentLyric(
                        lyricTime,
                        lyric,
                        pluginLog,
                        configuration.DebugMode,
                        configuration.HighlightLyrics
                    );
                }
                else
                {
                    ImGui.NewLine();
                }
            }
            else
            {
                if (lyricIdx < 0)
                {
                    if (lyricIdx == MUSIC_LINE_IDX)
                    {
                        using (ImRaii.Disabled(configuration.EmphasizeCurrentLine))
                            ImGuiHelpers.CenteredText(configuration.MusicLineInsert);
                    }
                    else
                    {
                        ImGui.NewLine();
                    }
                }
                else
                {
                    var lyric = song.Lyrics[lyricIdx];
                    var isActive = false;
                    if (lyric.OverlappingLineIdx is int lineIdx && lineIdx >= 0)
                    {
                        if (
                            (
                                lyricIdxs[behind] == lineIdx ||
                                (lyricIdxs[behind] != lineIdx && song.Lyrics[lineIdx].StartTime <= lyricTime)
                            ) && lyricTime <= lyric.EndTime
                        )
                            isActive = lyric.StartTime - lyricTime < 0.7;
                    }
                    if (isActive)
                    {
                        Components.DrawCurrentLyric(
                            lyricTime,
                            lyric,
                            pluginLog,
                            configuration.DebugMode,
                            configuration.HighlightLyrics
                        );
                    }
                    else {
                        using (ImRaii.Disabled(configuration.EmphasizeCurrentLine))
                            ImGuiHelpers.CenteredText(lyric.ToDisplayString());
                    }
                }
            }
        }
    }

    private bool drawSongFileSelector(Song song)
    {
        using var popup = ImRaii.Popup("###lyric_file_selector");
        if (!popup)
            return false;

        var defaultName = song.AvailableFileNames[0].Replace($"{pluginInterface.ConfigDirectory.FullName}\\", "");
        var usingDefault = !configuration.BgmIdLyricFileLoads.ContainsKey(song.Id);
        if (ImGui.Selectable($"Auto ({defaultName})", selected: usingDefault))
        {
            configuration.BgmIdLyricFileLoads.Remove(song.Id);
            configuration.Save();
            bgmService.ReloadCurrentSongLyrics();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Uses first match following priority: user-defined > official > unofficial");
        }
        foreach (var fileName in song.AvailableFileNames)
        {
            var strippedFileName = fileName.Replace($"{pluginInterface.ConfigDirectory.FullName}\\", "");
            if (ImGui.Selectable(strippedFileName, selected: song.LoadedFileName == fileName && !usingDefault))
            {
                configuration.BgmIdLyricFileLoads[song.Id] = fileName;
                configuration.Save();
                bgmService.ReloadCurrentSongLyrics();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(fileName);
            }
        }

        return true;
    }

    private void drawSongHeader(Song song)
    {
        if (configuration.ShowSongName || configuration.ShowSongTime)
        {
            ImGui.Spacing();

            var cursorPos = ImGui.GetCursorPos();
            var hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

            if (configuration.ShowSongName)
            {
                ImGuiHelpers.CenteredText(song.Name ?? "Unknown Song");
                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.Text($"Title: {song.Name}");

                        if (song.Tags.GetValueOrDefault(SongTag.SongAuthor) is string author)
                            ImGui.Text($"Author(s): {author}");
                        if (song.Tags.GetValueOrDefault(SongTag.Album) is string album)
                            ImGui.Text($"Album: {album}");
                        if (song.Tags.GetValueOrDefault(SongTag.Artist) is string artist)
                            ImGui.Text($"Performer(s): {artist}");
                        if (song.Tags.GetValueOrDefault(SongTag.Lyricist) is string lyricist)
                            ImGui.Text($"Lyricist(s): {lyricist}");
                        ImGui.Text($"Length: {Util.FormatTime(song.Duration, decPlaces: 0, padMins: false)}");
                        if (song.Tags.GetValueOrDefault(SongTag.LrcAuthor) is string lrcAuthor)
                            ImGui.Text($"Sync By: {lrcAuthor}");
                        if (song.Tags.GetValueOrDefault(SongTag.Comment) is string comment)
                            ImGui.Text($"Note: {comment}");
                    }
                }

                var nextPos = ImGui.GetCursorPos();

                var buttonSize = ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.X;

                var fileSelectorOpen = drawSongFileSelector(song);

                ImGui.SetCursorPos(cursorPos + new Vector2(ImGui.GetContentRegionAvail().X - buttonSize, -ImGui.GetStyle().ItemSpacing.Y));

                if (hovered || fileSelectorOpen)
                {
                    using (ImRaii.Disabled(song.AvailableFileNames.Length <= 0))
                    {
                        if (ImGuiComponents.IconButton("###lyric_file_selector_button", FontAwesomeIcon.CaretDown))
                        {
                            ImGui.OpenPopup("###lyric_file_selector");
                        }
                    }
                }

                if (song.AvailableFileNames.Length > 0)
                {
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Lyric File: {song.LoadedFileName?.Replace($"{pluginInterface.ConfigDirectory.FullName}\\", "")}");
                    }
                }
                else
                {
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.SetTooltip($"No lyric file(s) found");
                    }
                }

                ImGui.SetCursorPos(nextPos);

            }

            if (configuration.ShowSongTime)
            {
                ImGui.Spacing();
                Components.DrawPlaybackBar(
                    bgmService.CurrentElapsedTime,
                    song.Duration,
                    song.LoopStart,
                    configuration.LyricWindowBackgroundOpacity ?? 0.93f,
                    configuration.ShowLoopStartTime
                );
                ImGui.Spacing();
            }
            ImGui.Spacing();
        }
    }

    public override void Draw()
    {
        if (!configuration.ShowSongName && !configuration.ShowSongTime && !configuration.ShowLyrics)
        {
            ImGui.NewLine();
            ImGuiHelpers.CenteredText("Toggle display options in config");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("/karaoke config");
            ImGui.NewLine();
            ImGui.Spacing();
            return;
        }

        if (bgmService.CurrentSong is Song song && bgmService.CurrentSongLoopData is not null)
        {
            drawSongHeader(song);

            if (configuration.ShowLyrics && (song.Lyrics?.Length ?? 0) > 0)
            {
                if (configuration.ShowSongName || configuration.ShowSongTime)
                {
                    ImGui.Separator();
                    ImGui.Spacing();
                }

                if (song.LoadingLyrics)
                {
                    for (var i = 0; i < configuration.NumLyricsBehind; i++)
                        ImGui.NewLine();
                    ImGuiHelpers.CenteredText("Loading Lyrics...");
                    for (var i = 0; i < configuration.NumLyricsAhead; i++)
                        ImGui.NewLine();
                }
                else if (song.Lyrics?.Length > 0)
                {
                    var lyricTime = song.LoopElapsedTime(bgmService.CurrentElapsedTime, -configuration.GlobalLyricDelay);
                    drawLyrics(song, lyricTime);
                }
                else
                {
                    return;
                }
                ImGui.Spacing();
            }
            else if (!configuration.ShowSongName && !configuration.ShowSongTime && configuration.ShowLyrics)
            {
                ImGui.NewLine();
                ImGuiHelpers.CenteredText("No Lyrics Available");
                ImGui.NewLine();
                ImGui.Spacing();
            }
        }
        else if (bgmService.LoadingSong)
        {
            ImGui.NewLine();
            ImGuiHelpers.CenteredText("Loading Song Data...");
            ImGui.Spacing();
            ImGui.NewLine();
        }
        else
        {
            ImGui.NewLine();
            ImGuiHelpers.CenteredText("No song data");
            ImGui.Spacing();
            ImGui.NewLine();
        }
    }
}
