using Dalamud.Configuration;
using Dalamud.Interface.GameFonts;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace Karaoke;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public float GlobalLyricDelay { get; set; } = 0.0f;
    public DtrBarLyricDisplayType DtrBarLyricDisplayMode { get; set; } = DtrBarLyricDisplayType.LineHighlightSweep;
    public ushort DtrBarTextColor { get; set; } = 518;
    public ushort DtrBarGlowColor { get; set; } = 19;
    public bool DisplayLyricInToast { get; set; } = false;
    public bool DisplayLyricInFlyText { get; set; } = false;
    public float? LyricFontSize { get; set; } = null;
    public GameFontFamily? LyricFont { get; set; } = null;
    public uint NumLyricsBehind { get; set; } = 1;
    public uint NumLyricsAhead { get; set; } = 1;
    public OpenWindowOn OpenWindowOn { get; set; } = OpenWindowOn.SongChange;
    public bool ShowSongName { get; set; } = true;
    public bool ShowSongTime { get; set; } = true;
    public bool ShowLoopStartTime { get; set; } = true;
    public bool ShowLyrics { get; set; } = true;
    public HighlightLyricType HighlightLyrics { get; set; } = HighlightLyricType.ProgressSweep;
    public bool LyricWindowNoTitleBar { get; set; } = false;
    public float? LyricWindowBackgroundOpacity { get; set; } = null;
    public bool EmphasizeCurrentLine { get; set; } = true;
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
