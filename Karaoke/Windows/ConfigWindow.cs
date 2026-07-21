using Dalamud.Bindings.ImGui;
using Dalamud.Game.Config;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ImGuiSeStringRenderer;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Common.Component.Excel;
using Karaoke.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System;
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Karaoke.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly IPluginLog pluginLog;
    private readonly Configuration configuration;
    private readonly BGMService bgmService;
    private readonly LyricPlayerWindow lyricPlayerWindow;
    private readonly DtrBarService dtrBarService;
    private readonly FontManager fontManager;
    private readonly IDataManager dataManager;
    private readonly IGameConfig gameConfig;
    private static readonly FrozenDictionary<OpenWindowOn, (string Name, string Tooltip)> OpenWindowOnNames = new Dictionary<OpenWindowOn, (string Name, string Tooltip)>()
    {
        { OpenWindowOn.None, ("Never", "Never automatically open the lyric player window") },
        { OpenWindowOn.SongChange, ("On New Song (Any)", "Open the lyric player window when any new song is played") },
        { OpenWindowOn.SongChangeLyrics, ("On New Song (with Lyrics)", "Open the lyric player window when a new song with known lyrics is played") }
    }.ToFrozenDictionary();
    private static readonly FrozenDictionary<DtrBarLyricDisplayType, (string Name, string Tooltip)> DtrBarModeNames = new Dictionary<DtrBarLyricDisplayType, (string Name, string Tooltip)>()
    {
        { DtrBarLyricDisplayType.None, ("None", "Do not display anything") },
        { DtrBarLyricDisplayType.LinePlain, ("Plain", "Display the current lyric line") },
        { DtrBarLyricDisplayType.LineHighlightSweep, ("Highlight Line Progress", "Display the current lyric and the progress through the line") },
        { DtrBarLyricDisplayType.LineHighlightWord, ("Highlight Word", "Display the current lyric and word") },
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<HighlightLyricType, (string Name, string Tooltip)> HighlightLyricNames = new Dictionary<HighlightLyricType, (string Name, string Tooltip)>()
    {
        { HighlightLyricType.None, ("None", "Don't highlight the current word/line") },
        { HighlightLyricType.ProgressSweep, ("Line Progress", "Sweep across current line as lyric words play") },
        { HighlightLyricType.Word, ("Current Word", "Highlight current word on the line that is playing") }
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<GameFontFamily, string> LyricFontNames = new Dictionary<GameFontFamily, string>
    {
        { GameFontFamily.Axis, "Axis" },
        { GameFontFamily.Jupiter, "Jupiter" },
        { GameFontFamily.TrumpGothic, "Trump Gothic" },
        { GameFontFamily.MiedingerMid, "Miedinger Mid" }
    }.ToFrozenDictionary();

    public ConfigWindow(
        IPluginLog pluginLog,
        Configuration configuration,
        BGMService bgmService,
        LyricPlayerWindow lyricPlayerWindow,
        DtrBarService dtrBarService,
        FontManager fontManager,
        IDataManager dataManager,
        IGameConfig gameConfig
    ) : base("Karaoke Config###karaoke_configuration_window")
    {
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(400, 0);
        SizeCondition = ImGuiCond.Appearing;
        this.pluginLog = pluginLog;
        this.configuration = configuration;
        this.bgmService = bgmService;
        this.lyricPlayerWindow = lyricPlayerWindow;
        this.dtrBarService = dtrBarService;
        this.fontManager = fontManager;
        this.dataManager = dataManager;
        this.gameConfig = gameConfig;
        this.colorSheet = dataManager.GetExcelSheet<UIColor>();
    }
    private readonly ExcelSheet<UIColor> colorSheet;

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

    private void drawDtrBarCombo()
    {
        var dtrMode = configuration.DtrBarLyricDisplayMode;
        var (dtrModeName, dtrModeTooltip) = DtrBarModeNames.GetValueOrDefault(dtrMode, ("???", "unknown value"));

        using var combo = ImRaii.Combo("Server Info Bar", dtrModeName);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"What to display in the server info bar\nCurrently: {dtrModeTooltip}");
        if (!combo)
            return;

        foreach (var (value, (text, tooltip)) in DtrBarModeNames)
        {
            if (ImGui.Selectable(text, dtrMode == value))
            {
                configuration.DtrBarLyricDisplayMode = value;
                dtrBarService.ClearCache();
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(tooltip);
        }
    }

    private uint getColorValOrDefault(uint colorRowId, uint defaultVal)
    {
        if (!colorSheet.TryGetRow(colorRowId, out var colorRow))
            return defaultVal;

        gameConfig.TryGet(SystemConfigOption.ColorThemeType, out uint colorThemeId);

        return colorThemeId switch
        {
            0 => colorRow.Dark,
            1 => colorRow.Light,
            2 => colorRow.ClassicFF,
            3 => colorRow.ClearBlue,
            4 => colorRow.ClearWhite,
            5 => colorRow.ClearGreen,
            6 => colorRow.Unknown2, // ClearGrey
            7 => colorRow.Unknown3, // ClearPink
            _ => defaultVal,
        };
    }

    private uint fmtColorVal(uint colorVal)
        => BinaryPrimitives.ReverseEndianness(colorVal) | 0xFF000000u;

    private void drawDtrLinePreview(uint textColorId, uint glowColorId)
    {
        var defaultTextColor = new ByteColor { R = 255, G = 255, B = 255, A = 255 }.RGBA;
        var defaultGlowColor = new ByteColor { R = 255, G = 12, B = 106, A = 142 }.RGBA;
        var textColor = defaultTextColor;
        var glowColor = defaultGlowColor;

        var mode = configuration.DtrBarLyricDisplayMode;
        var isHighlightMode = mode == DtrBarLyricDisplayType.LineHighlightSweep || mode == DtrBarLyricDisplayType.LineHighlightWord;
        if (isHighlightMode)
        {
            textColor = getColorValOrDefault(textColorId, defaultTextColor);
            glowColor = getColorValOrDefault(glowColorId, defaultGlowColor);
        }

        var unhighlightedParams = new SeStringDrawParams()
        {
            Edge = true,
            Color = fmtColorVal(defaultTextColor),
            EdgeColor = fmtColorVal(defaultGlowColor),
            WrapWidth = float.PositiveInfinity,
        };

        var highlightedParams = unhighlightedParams with
        {
            Color = fmtColorVal(textColor),
            EdgeColor = fmtColorVal(glowColor),
        };

        var exampleText = "\"Example lyric line\"";
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
        {
            if (mode == DtrBarLyricDisplayType.LineHighlightWord)
            {
                ImGuiHelpers.SeStringWrapped(Encoding.UTF8.GetBytes("\"Example "), unhighlightedParams);
                exampleText = " lyric line\"";
                ImGui.SameLine();
            }
            if (isHighlightMode)
            {
                ImGuiHelpers.SeStringWrapped(Encoding.UTF8.GetBytes(string.Join(' ', exampleText.Split(' ')[..^1])), highlightedParams);
                exampleText = " line\"";
                ImGui.SameLine();
            }
        }

        ImGuiHelpers.SeStringWrapped(Encoding.UTF8.GetBytes(exampleText), unhighlightedParams);
    }


    private bool drawDtrColorPicker(
        string popupId,
        ref ushort textColorId,
        ref ushort glowColorId,
        bool isTextColor
    )
    {
        using var popup = ImRaii.Popup(popupId, ImGuiWindowFlags.NoMove);
        if (!popup)
            return false;

        var buttonSize = new Vector2(100 * ImGuiHelpers.GlobalScale, ImGui.GetFrameHeightWithSpacing());

        using var child = ImRaii.Child($"{popupId}_child", buttonSize with { Y = buttonSize.Y * 10 });

        using var table = ImRaii.Table($"{popupId}_table", 1, flags: ImGuiTableFlags.NoPadInnerX | ImGuiTableFlags.NoPadOuterX);
        if (!table)
            return false;

        ImGui.TableSetupColumn($"{popupId}_table_column", ImGuiTableColumnFlags.WidthFixed, buttonSize.X);
        var drawList = ImGui.GetWindowDrawList();
        var clipper = ImGui.ImGuiListClipper();
        using var centeredText = ImRaii.PushStyle(ImGuiStyleVar.SelectableTextAlign, Vector2.One * 0.5f);
        clipper.Begin(colorSheet.Count, ImGui.GetFrameHeightWithSpacing());
        try
        {
            while (clipper.Step())
            {
                buttonSize = buttonSize with { X = ImGui.GetContentRegionAvail().X };
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    var rowId = colorSheet.GetRowAt(i).RowId;
                    var isSelected = isTextColor ? rowId == textColorId : rowId == textColorId;

                    var pos = ImGui.GetCursorScreenPos();
                    drawList.AddRectFilled(pos, pos + buttonSize, fmtColorVal(getColorValOrDefault(rowId, 0)));

                    if (ImGui.Selectable($"{rowId}###karaoke_select_dtr_color_{i}_{isTextColor}", isSelected, flags: ImGuiSelectableFlags.SpanAllColumns, buttonSize))
                    {
                        if (isTextColor)
                            textColorId = (ushort)rowId;
                        else
                            glowColorId = (ushort)rowId;
                        return true;
                    }
                    if (ImGui.IsItemHovered())
                    {
                        using var _ = ImRaii.Tooltip();
                        if (isTextColor)
                            drawDtrLinePreview(rowId, glowColorId);
                        else
                            drawDtrLinePreview(textColorId, rowId);
                    }
                }
            }

            return false;
        }
        finally
        {
            clipper.Destroy();
        }
    }

    private void drawDtrColorSelector()
    {
        var textColorId = configuration.DtrBarTextColor;
        var glowColorId = configuration.DtrBarGlowColor;

        var buttonSize = new Vector2(ImGui.GetTextLineHeightWithSpacing());
        var drawList = ImGui.GetWindowDrawList();
        var rounding = ImGui.GetStyle().FrameRounding;

        var textColorPos = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("###karaoke_dtr_text_button", buttonSize))
            ImGui.OpenPopup("###karaoke_dtr_text_popup");

        drawList.AddRectFilled(textColorPos, textColorPos + buttonSize, fmtColorVal(getColorValOrDefault(textColorId, 0u)), rounding);

        ImGui.SameLine();
        ImGui.Text($"Text");
        ImGui.SameLine();

        var glowColorPos = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("###karaoke_dtr_glow_button", buttonSize))
            ImGui.OpenPopup("###karaoke_dtr_glow_popup");

        drawList.AddRectFilled(glowColorPos, glowColorPos + buttonSize, fmtColorVal(getColorValOrDefault(glowColorId, 0u)), rounding);

        ImGui.SameLine();
        ImGui.Text($"Glow");

        drawDtrLinePreview(textColorId, glowColorId);

        if (drawDtrColorPicker(
            "###karaoke_dtr_text_popup",
            ref textColorId,
            ref glowColorId,
            isTextColor: true
        ))
        {
            configuration.DtrBarTextColor = textColorId;
            configuration.Save();
        }

        if (drawDtrColorPicker(
            "###karaoke_dtr_glow_popup",
            ref textColorId,
            ref glowColorId,
            isTextColor: false
        ))
        {
            configuration.DtrBarGlowColor = glowColorId;
            dtrBarService.ClearCache();
            configuration.Save();
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

    private void drawFontTypeCombo()
    {
        var fontType = configuration.LyricFont ?? GameFontFamily.Axis;

        var fontName = LyricFontNames.GetValueOrDefault(fontType, "???");

        using var combo = ImRaii.Combo("Lyric Font", fontName);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"What font to display lyrics in\nWarning: Some fonts may not support all characters");
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            configuration.LyricFont = null;
            configuration.Save();
            _ = fontManager.BuildFonts();
        }
        if (!combo)
            return;

        foreach (var (font, name) in LyricFontNames)
        {
            if (ImGui.Selectable(name, fontType == font))
            {
                configuration.LyricFont = font;
                configuration.Save();
                _ = fontManager.BuildFonts();
            }
        }
    }

    public override void Draw()
    {
        ImGui.Text("Lyric Window General");

        ImGui.Spacing();
        drawOpenConditionCombo();

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

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Lyric Window Customization");
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

        using (ImRaii.Disabled(!showSongTime))
        {
            var showLoopStart = configuration.ShowLoopStartTime;
            if (ImGui.Checkbox("Loop Start Time", ref showLoopStart))
            {
                configuration.ShowLoopStartTime = showLoopStart;
                configuration.Save();
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Mark the point the song loops on the progress bar");

        var showLyrics = configuration.ShowLyrics;
        if (ImGui.Checkbox("Show Lyrics", ref showLyrics))
        {
            configuration.ShowLyrics = showLyrics;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Display time-synced lyrics to playing song, if available");

        var emphasizeCurrent = configuration.EmphasizeCurrentLine;
        using (ImRaii.Disabled(!showLyrics))
        {
            if (ImGui.Checkbox($"Emphasize current lyric line", ref emphasizeCurrent))
            {
                configuration.EmphasizeCurrentLine = emphasizeCurrent;
                configuration.Save();
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Whether lyric lines ahead/behind of the current playing line should darkened slightly");

        drawHighlightLyricCombo();

        using (ImRaii.Disabled(!showLyrics))
        {
            var fontSize = configuration.LyricFontSize ?? UiBuilder.DefaultFontSizePt;
            if (ImGui.InputFloat($"Lyrics Font Size", ref fontSize, 2, 4, "%.0f"))
            {
                configuration.LyricFontSize = Math.Clamp(fontSize, 4f, 96f);
                configuration.Save();
                _ = fontManager.BuildFonts();
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                configuration.LyricFontSize = null;
                configuration.Save();
                _ = fontManager.BuildFonts();
            }

            drawFontTypeCombo();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Lyric Window Playback");

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
            if (ImGui.InputTextWithHint("Music Line Text", "<music>", ref insert))
            {
                configuration.MusicLineInsert = insert;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Sets the appearance of a music line insert");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Server Info Bar Lyrics");
        ImGui.Spacing();

        drawDtrBarCombo();
        ImGui.Spacing();

        var usesHighlightColor = (
            configuration.DtrBarLyricDisplayMode == DtrBarLyricDisplayType.LineHighlightWord
            || configuration.DtrBarLyricDisplayMode == DtrBarLyricDisplayType.LineHighlightSweep
        );
        if (usesHighlightColor)
        {
            ImGui.Text($"Highlight Color/Glow");
            using (ImRaii.PushIndent())
                drawDtrColorSelector();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Other Lyric Display Options");
        ImGui.Spacing();

        var questToast = configuration.DisplayLyricInToast;
        if (ImGui.Checkbox("Show Lyric as Toast", ref questToast))
        {
            configuration.DisplayLyricInToast = questToast;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("If lyrics should pop up as a notification");

        var flyText = configuration.DisplayLyricInFlyText;
        if (ImGui.Checkbox("Show Lyric as Fly Text", ref flyText))
        {
            configuration.DisplayLyricInFlyText = flyText;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("If lyrics should pop up as fly text");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Miscellaneous");
        ImGui.Spacing();

        using (ImRaii.Disabled(bgmService.ReloadingCurrentSongLyrics))
        {
            if (ImGui.Button("Reload Lyric Files"))
            {
                bgmService.ReloadCurrentSongLyrics();
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Fetches any updates from remote and updates index of local lyric files");

        var debugMode = configuration.DebugMode;
        if (ImGui.Checkbox("Debug Mode", ref debugMode))
        {
            configuration.DebugMode = debugMode;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Changes lyric highlighting / enables debug functionality");

        var timeRateMultiplier = (configuration.TimeRateMultiplier - 1) * (60 * 60);
        if (ImGui.InputDouble($"Time rate mult.", ref timeRateMultiplier, format: "%.8f s/hr"))
        {
            configuration.TimeRateMultiplier = (timeRateMultiplier / (60 * 60)) + 1;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "ADVANCED: Adjust rate of time for lyric playback to account for sound lag, " +
                "in units of seconds/hr of playback.\nOnly change this if you've measured " +
                "a consistent rate of time desync over a long period of playback."
            );
        }

        ImGui.Spacing();
    }
}
