using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Karaoke.Services;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Numerics;

namespace Karaoke.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly IPluginLog pluginLog;
    private readonly Configuration configuration;
    private readonly BGMService bgmService;
    private readonly LyricPlayerWindow lyricPlayerWindow;
    private static readonly FrozenDictionary<OpenWindowOn, (string Name, string Tooltip)> OpenWindowOnNames = new Dictionary<OpenWindowOn, (string Name, string Tooltip)>()
    {
        { OpenWindowOn.None, ("Never", "Never automatically open the lyric player window") },
        { OpenWindowOn.SongChange, ("On New Song (Any)", "Open the lyric player window when any new song is played") },
        { OpenWindowOn.SongChangeLyrics, ("On New Song (with Lyrics)", "Open the lyric player window when a new song with known lyrics is played") }
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<HighlightLyricType, (string Name, string Tooltip)> HighlightLyricNames = new Dictionary<HighlightLyricType, (string Name, string Tooltip)>()
    {
        { HighlightLyricType.None, ("None", "Don't highlight the current word/line") },
        { HighlightLyricType.ProgressSweep, ("Line Progress", "Color sweep across current line as lyric words play") },
        { HighlightLyricType.Word, ("Current Word", "Highlight current word on the line that is playing") }
    }.ToFrozenDictionary();

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(
        IPluginLog pluginLog, Configuration configuration, BGMService bgmService, LyricPlayerWindow lyricPlayerWindow
    ) : base("Karaoke Config###karaoke_configuration_window")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(400, 0);
        SizeCondition = ImGuiCond.Always;
        this.pluginLog = pluginLog;
        this.configuration = configuration;
        this.bgmService = bgmService;
        this.lyricPlayerWindow = lyricPlayerWindow;
    }

    private const float MaxDelay = 5f;
    private const float MaxEmptyThreshold = 20f;
    private const uint MaxLyricsDisplayed = 10;

    public void Dispose() { }

    public override void PreDraw()
    {
    }

    private void drawOpenConditionCombo()
    {
        var openOn = configuration.OpenWindowOn;
        var (openOnText, openOnTooltip) = OpenWindowOnNames.GetValueOrDefault(openOn, ("???", "unknown value"));

        using var combo = ImRaii.Combo("Open Window", openOnText);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"When to automatically open the lyric player\nCurrently: {openOnTooltip}");
        if (!combo)
            return;

        foreach (var (value, (text, tooltip)) in OpenWindowOnNames)
        {
            if (ImGui.Selectable(text, openOn == value))
            {
                configuration.OpenWindowOn = value;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(tooltip);
        }
    }

    private void drawHighlightLyricCombo()
    {
        using var _ = ImRaii.Disabled(!configuration.ShowLyrics);

        var highlightLyric = configuration.ShowLyrics
            ? configuration.HighlightLyrics
            : HighlightLyricType.None;
        var (highlightLyricText, highlightLyricTooltip) = HighlightLyricNames.GetValueOrDefault(highlightLyric, ("???", "unknown value"));

        using var combo = ImRaii.Combo("Highlight Lyric", highlightLyricText);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"How to highlight the current lyric line/word that is playing\nCurrently: {highlightLyricTooltip}");
        if (!combo)
            return;

        foreach (var (value, (text, tooltip)) in HighlightLyricNames)
        {
            if (ImGui.Selectable(text, highlightLyric == value))
            {
                configuration.HighlightLyrics = value;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(tooltip);
        }
    }

    public override void Draw()
    {
        ImGui.Text("General Settings");

        ImGui.Spacing();
        drawOpenConditionCombo();
        ImGui.Spacing();

        var showSongName = configuration.ShowSongName;
        if (ImGui.Checkbox("Show Song Info", ref showSongName))
        {
            configuration.ShowSongName = showSongName;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Display playing song name and lyric file selector (on hover)");

        var showSongTime = configuration.ShowSongTime;
        if (ImGui.Checkbox("Show Song Progress Bar", ref showSongTime))
        {
            configuration.ShowSongTime = showSongTime;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Display bar showing current time into playing song");

        var showLyrics = configuration.ShowLyrics;
        if (ImGui.Checkbox("Show Lyrics", ref showLyrics))
        {
            configuration.ShowLyrics = showLyrics;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Display time-synced lyrics to playing song, if available");

        ImGui.Spacing();
        drawHighlightLyricCombo();
        ImGui.Spacing();

        var titleBar = !configuration.LyricWindowNoTitleBar;
        if (ImGui.Checkbox($"Window Title Bar", ref titleBar))
        {
            configuration.LyricWindowNoTitleBar = !titleBar;
            if (!titleBar)
            {
                lyricPlayerWindow.Flags |= ImGuiWindowFlags.NoTitleBar;
            }
            else
            {
                lyricPlayerWindow.Flags ^= ImGuiWindowFlags.NoTitleBar;
            }
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Whether to have a title bar on the lyric player window");

        var setWindowOpacity = configuration.LyricWindowBackgroundOpacity ?? BgAlpha;
        unsafe
        {
            var curWindowBg = ImGui.GetStyleColorVec4(ImGuiCol.WindowBg);
            if (curWindowBg is not null)
                setWindowOpacity ??= curWindowBg->W;
        }
        var windowOpacity = setWindowOpacity ?? 1f;
        windowOpacity *= 100;

        if (ImGui.SliderFloat($"Window Opacity", ref windowOpacity, 0f, 100f, "%.0f%%"))
        {
            configuration.LyricWindowBackgroundOpacity = windowOpacity / 100;
            lyricPlayerWindow.BgAlpha = windowOpacity / 100;
            configuration.Save();
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            configuration.LyricWindowBackgroundOpacity = null;
            lyricPlayerWindow.BgAlpha = null;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Changes the background transparency of the lyric player window\nRight click to set to default dalamud window transparency");

        ImGui.Spacing();

        var debugMode = configuration.DebugMode;
        if (ImGui.Checkbox("Debug Mode", ref debugMode))
        {
            configuration.DebugMode = debugMode;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Changes lyric highlighting / enables debug functionality");

        ImGui.Spacing();

        if (debugMode)
        {
            var timeRateMultiplier = (configuration.TimeRateMultiplier - 1) * (60 * 60);
            if (ImGui.InputDouble($"Time Rate Mult", ref timeRateMultiplier, format: "%.8f s/hr"))
            {
                configuration.TimeRateMultiplier = (timeRateMultiplier / (60 * 60)) + 1;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "ADVANCED: Adjust rate of time for lyric playback to account for sound lag, " +
                    "in units of seconds/hr of playback.\nWARNING: Only change this if you've " +
                    "measured a consistent/steady time desync over a long period of playback."
                );
            }

            ImGui.Spacing();
        }

        using (ImRaii.Disabled(bgmService.ReloadingCurrentSongLyrics))
        {
            if (ImGui.Button("Reload Lyric Files"))
            {
                bgmService.ReloadCurrentSongLyrics();
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Fetches any updates from remote and updates index of local lyric files");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Lyrics Playback");

        using (ImRaii.Disabled(!showLyrics))
        {
            var delay = configuration.GlobalLyricDelay;
            ImGui.Spacing();
            var textLabel = delay switch
            {
                > 0 => $"{delay:F1}s late",
                < 0 => $"{Math.Abs(delay):F1}s early",
                _ => "0.0s"
            };
            if (ImGui.SliderFloat($"Lyrics Offset (s)", ref delay, -MaxDelay, MaxDelay, textLabel))
            {
                configuration.GlobalLyricDelay = delay;
                configuration.Save();
                pluginLog.Verbose($"changed lyric delay to {delay}");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("How many seconds ahead/behind to offset lyric time sync");
            ImGui.Spacing();

            var lyricsBehind = configuration.NumLyricsBehind;
            if (ImGui.SliderUInt("# Lyrics Behind", ref lyricsBehind, 0, MaxLyricsDisplayed))
            {
                configuration.NumLyricsBehind = lyricsBehind;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("How many lyrics to display before the current lyric");

            ImGui.Spacing();

            var lyricsAhead = configuration.NumLyricsAhead;
            if (ImGui.SliderUInt("# Lyrics Ahead", ref lyricsAhead, 0, MaxLyricsDisplayed))
            {
                configuration.NumLyricsAhead = lyricsAhead;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("How many lyrics to display ahead of the current lyric");
            ImGui.Spacing();

            var thresh = configuration.MusicLineInsertThreshold;
            if (ImGui.SliderFloat($"Music Line Insert (s)", ref thresh, 1f, MaxEmptyThreshold, ">= %.1fs"))
            {
                configuration.MusicLineInsertThreshold = thresh;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("How long of a gap between lyrics to place a music line insert");
            ImGui.Spacing();

            var insert = configuration.MusicLineInsert;
            if (ImGui.InputTextWithHint("Music Line Text", "<instrumental>", ref insert))
            {
                configuration.MusicLineInsert = insert;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Sets the appearance of a music line insert");
        }

    }
}
