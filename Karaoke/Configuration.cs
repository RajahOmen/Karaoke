using Dalamud.Configuration;
using Dalamud.Plugin;
using Karaoke.Font;
using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Style;

namespace Karaoke;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public float GlobalLyricDelay { get; set; } = 0.0f;
    public DtrBarLyricDisplayType DtrBarLyricDisplayMode { get; set; } = DtrBarLyricDisplayType.LineHighlightSweep;
    public ushort DtrBarTextColor { get; set; } = 518;
    public ushort DtrBarGlowColor { get; set; } = 19;
    public float DtrBarPreviewLength { get; set; } = 0.75f;
    public bool DisplayLyricInToast { get; set; } = false;
    public bool DisplayLyricInFlyText { get; set; } = false;
    public bool LyricWindowRightJustified { get; set; } = false;

    /// <summary>
    /// Defaults to <see cref="Dalamud.Bindings.ImGui.ImGuiCol.Text"/> value for active Dalamud style.
    /// </summary>
    public Vector4? LyricActiveLineColor { get; set; } = null;
    /// <summary>
    /// Defaults to <see cref="Dalamud.Interface.Style.DalamudColors.DalamudGrey2"/> with full opacity.
    /// </summary>
    public Vector4? LyricInactiveLineColor { get; set; } = null;
    /// <summary>
    /// Defaults to <see cref="Dalamud.Interface.Style.DalamudColors.DalamudRed"/> with full opacity.
    /// </summary>
    public Vector4? LyricHighlightColor { get; set; } = null;

    public Vector4 GetLyricColor(LyricColor type)
    {
        return type switch
        {
            LyricColor.ActiveLine => LyricActiveLineColor ?? ImGui.GetStyle().Colors[(int)ImGuiCol.Text] with { W = 1f },
            LyricColor.InactiveLine => LyricInactiveLineColor ?? (StyleModel.GetFromCurrent().BuiltInColors?.DalamudGrey2 is { } grey2 ? grey2 with { W = 1f } : Vector4.One),
            LyricColor.Highlight => LyricHighlightColor ?? (StyleModel.GetFromCurrent().BuiltInColors?.DalamudRed is { } grey2 ? grey2 with { W = 1f } : Vector4.One),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }
    
    public void SetLyricColor(LyricColor type, Vector4? value)
    {
        switch (type)
        {
            case LyricColor.ActiveLine:
                LyricActiveLineColor = value;
                break;
            case LyricColor.InactiveLine:
                LyricInactiveLineColor = value;
                break;
            case LyricColor.Highlight:
                LyricHighlightColor = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }
    
    public float? LyricFontSize { get; set; } = null;
    public FontType? LyricFont { get; set; } = FontType.DalamudDefault;
    public string? CustomLyricFontPath { get; set; } = null;
    public uint NumLyricsBehind { get; set; } = 1;
    public uint NumLyricsAhead { get; set; } = 1;
    public OpenWindowOn OpenWindowOn { get; set; } = OpenWindowOn.SongChangeLyrics;
    public bool ShowSongName { get; set; } = true;
    public bool ShowSongTime { get; set; } = true;
    public bool ShowLoopStartTime { get; set; } = true;
    public bool ShowLyrics { get; set; } = true;
    public HighlightLyricType HighlightLyrics { get; set; } = HighlightLyricType.ProgressSweep;
    public bool LyricWindowNoTitleBar { get; set; } = false;
    public float? LyricWindowBackgroundOpacity { get; set; } = null;
    public bool DebugMode { get; set; } = false;

    /// <summary>
    /// Overrides default priorities to load specific files, used to ensure selections on
    /// lyrics to load are preserved between sessions/plays.
    /// </summary>
    public Dictionary<uint, string> BgmIdLyricFileLoads { get; private set; } = [];

    /// <summary>
    /// Change the rate of time as it appears to song playback. Added
    /// to adjust for the slight discrepency between elapsed time and 
    /// "true" elapsed time, as measured.
    /// 
    /// Default set as value that matched personal testing.
    /// </summary>
    public double TimeRateMultiplier { get; set; } = 1.0000527777777777;

    /// <summary>
    /// Minimum amount of time between lyrics for there to be an indicator placed
    /// in the lyrics
    /// </summary>
    public float MusicLineInsertThreshold { get; set; } = 5f;
    public string MusicLineInsert { get; set; } = "==========";

    // the below exist just to make saving less cumbersome
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public void Save()
    {
        this.pluginInterface!.SavePluginConfig(this);
    }
}


public enum LyricColor
{
    ActiveLine,
    InactiveLine,
    Highlight
}
