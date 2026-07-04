using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Karaoke.Services;
using System;
using System.Numerics;

namespace Karaoke.Windows;

public class DebugWindow : Window, IDisposable
{
    public Configuration Configuration { get; }
    public ITextureProvider TextureProvider { get; }
    public IDalamudPluginInterface PluginInterface { get; }
    private readonly BGMService bgmService;
    private readonly ConfigWindow configWindow;
    private readonly IPluginLog pluginLog;
    private readonly IFramework framework;

    public DebugWindow(
        Configuration configuration,
        ITextureProvider textureProvider,
        IDalamudPluginInterface pluginInterface,
        BGMService bgmService,
        SongLoaderService lyricService,
        ConfigWindow configWindow,
        IPluginLog pluginLog,
        IFramework framework
        )
        : base("Karaoke Debug##karaoke_debug_window", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Configuration = configuration;
        TextureProvider = textureProvider;
        PluginInterface = pluginInterface;
        this.bgmService = bgmService;
        this.configWindow = configWindow;
        this.pluginLog = pluginLog;
        this.framework = framework;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        //var file = new FileInfo(Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png"));
        //GoatImage = textureProvider.GetFromFile(file);
    }

    private float offset = 0.0f;

    public void Dispose() { }

    public override void Draw()
    {

        if (ImGui.Button("Config"))
            configWindow.Toggle();

        ImGui.Separator();

        ImGui.Spacing();

        ImGui.Text($"has looped:{bgmService.CurrentSongHasLooped}");

        var nowTime = DateTime.Now.TimeOfDay;
        var curTime = bgmService.CurrentElapsedTime;
        var rawTime = bgmService.RawElapsedTime;
        var rawOffsetTime = rawTime + bgmService.ElapsedTimeRawOverflowOffset;
        var totalTime = bgmService.TotalElapsedTime;
        var totalAdjust = rawOffsetTime - totalTime;
        var curUnadjust = bgmService.CurrentSong?.LoopElapsedTime(rawOffsetTime, Configuration.GlobalLyricDelay) ?? 0f;
        var curAdjust = bgmService.CurrentSong?.LoopElapsedTime(totalTime, Configuration.GlobalLyricDelay) ?? 0f;
        ImGui.Text($"{framework.LastUpdate.Hour}:{framework.LastUpdate.Minute:00}.{framework.LastUpdate.Second:00}.{framework.LastUpdate.Millisecond:000}{framework.LastUpdate.Microsecond:000}{framework.LastUpdate.Nanosecond}");
        ImGui.Text($"LastUpdate (s): {framework.LastUpdate.TimeOfDay.TotalMicroseconds / 1000000d:F10}");
        ImGui.Text($"Now (s): {nowTime.TotalMicroseconds / 1000000d:F10}");
        ImGui.Text($"current: {curTime:F10}");
        ImGui.Text($"rawElapsedTime: {rawTime:F10}");
        ImGui.Text($"totalTime: {totalTime:F10}");
        ImGui.Text($"raw + offset: {rawOffsetTime:F10}");
        ImGui.Text($"(raw + offset) - totalTime: {totalAdjust:F10}");
        ImGui.Text($"rateMult: {Configuration.TimeRateMultiplier:F25}");
        ImGui.Text($"offset: {bgmService.ElapsedTimeRawOverflowOffset:F10}");
        ImGui.Text($"CurrentElapsedAdjusted: {curAdjust:F10}");
        ImGui.Text($"CurrentElapsedUnadjusted: {curUnadjust:F10}");
        ImGui.Text($"current diff: {curUnadjust - curAdjust:F10}");

        if (bgmService.CurrentSong is Song curSong)
        {
            ImGui.Text($"=== Raw ===");
            var idxUnadjust = curSong.GetLyricIdxAtTime(curUnadjust);
            if (idxUnadjust >= 0)
            {
                var nextLyric = curSong.Lyrics![idxUnadjust];
                LyricLine? curLyric = null;
                var curLyricIdx = 0;

                if (nextLyric.StartTime <= curUnadjust)
                {
                    curLyric = nextLyric;
                    curLyricIdx = idxUnadjust;
                    idxUnadjust = curSong.GetNextLyricIdx(idxUnadjust);
                    nextLyric = curSong.Lyrics[idxUnadjust];
                }

                var seg = nextLyric.Segments[0];
                var firstDisplayTime = seg.StartTime;

                ImGui.Text($"Next Lyric: [{idxUnadjust}]{nextLyric}");
                ImGui.Text($"First Segment '{nextLyric.Text[seg.StartIdx..seg.EndIdx]}': {firstDisplayTime:F10}");
                ImGui.Text($"start - curUnaj: {nextLyric.StartTime - curUnadjust:F10}");
                ImGui.Text($"seg - curUnaj: {firstDisplayTime - curUnadjust:F10}");
                if (curLyric is LyricLine prevLyric)
                {
                    var lastSeg = prevLyric.Segments[0];
                    var lastDispTime = lastSeg.StartTime;
                    ImGui.Spacing();
                    ImGui.Spacing();
                    ImGui.Text($"Prev/Cur Lyric: [{curLyricIdx}]{prevLyric}");
                    ImGui.Text($"First Segment '{prevLyric.Text[lastSeg.StartIdx..lastSeg.EndIdx]}': {lastDispTime:F10}");
                    ImGui.Text($"start - curUnaj: {prevLyric.StartTime - curUnadjust:F10}");
                    ImGui.Text($"seg - curUnaj: {lastDispTime - curUnadjust:F10}");
                    ImGui.Spacing();
                }
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            ImGui.Text("=== Rate ===");
            var idxAdjust = curSong.GetLyricIdxAtTime(curAdjust);
            if (idxAdjust >= 0)
            {
                var nextLyric = curSong.Lyrics![idxAdjust];
                LyricLine? curLyric = null;
                var curLyricIdx = 0;

                if (nextLyric.StartTime <= curAdjust)
                {
                    curLyric = nextLyric;
                    curLyricIdx = idxAdjust;
                    idxAdjust = curSong.GetNextLyricIdx(idxAdjust);
                    nextLyric = curSong.Lyrics[idxAdjust];
                }

                var seg = nextLyric.Segments[0];
                var firstDisplayTime = seg.StartTime;

                ImGui.Text($"Next Lyric: [{idxAdjust}]{nextLyric}");
                ImGui.Text($"First Segment '{nextLyric.Text[seg.StartIdx..seg.EndIdx]}': {firstDisplayTime:F10}");
                ImGui.Text($"start - curAdj: {nextLyric.StartTime - curAdjust:F10}");
                ImGui.Text($"seg - curAdj: {firstDisplayTime - curAdjust:F10}");
                if (curLyric is LyricLine prevLyric)
                {
                    var lastSeg = prevLyric.Segments[0];
                    var lastDispTime = lastSeg.StartTime;
                    ImGui.Spacing();
                    ImGui.Spacing();
                    ImGui.Text($"Prev/Cur Lyric: [{curLyricIdx}]{prevLyric}");
                    ImGui.Text($"First Segment '{prevLyric.Text[lastSeg.StartIdx..lastSeg.EndIdx]}': {lastDispTime:F10}");
                    ImGui.Text($"start - curUnaj: {prevLyric.StartTime - curAdjust:F10}");
                    ImGui.Text($"seg - curUnaj: {lastDispTime - curAdjust:F10}");
                    ImGui.Spacing();
                }
                ImGui.Spacing();
                ImGui.Separator();
            }
        }

        ImGui.Spacing();
        if (bgmService.CurrentSong is Song song)
        {
            ImGui.Spacing();
            ImGui.Text(song.Name ?? "???");
            ImGui.Spacing();
            var loopTime = song.Duration - song.LoopStart;
            var actualOffset = offset;

            if (bgmService.CurrentElapsedTime < song.LoopStart)
            {
                var resultTime = bgmService.CurrentElapsedTime - offset;
                if (resultTime < 0)
                {
                    actualOffset = -resultTime;
                }
                else if (song.LoopStart is float loopStart && resultTime >= loopStart)
                {
                    actualOffset = offset - (loopStart - curTime);
                    curTime = loopStart;
                }
            }

            var lyricTime = curTime - (actualOffset % loopTime);
            if (lyricTime < song.LoopStart)
                lyricTime += loopTime;
            if (lyricTime > song.Duration)
                lyricTime -= loopTime;

            //ImGui.Text(
            //    $"{FormatTime(bgmService.CurrentElapsedTime)} " +
            //    $"/ {FormatTime(song.Duration)} " +
            //    $"(Loops @ {FormatTime(song.LoopStart)})"
            //);

            ImGui.Text(
                $"{Util.FormatTime(bgmService.CurrentElapsedTime, 2)} " +
                $"/ {Util.FormatTime(song.Duration, 2)} " +
                $"(Loops @ {Util.FormatTime(song.LoopStart ?? -1, 2)})\n" +
                $"{bgmService.CurrentElapsedTime < song.LoopStart}\n" +
                $"Offset:       {offset:F1}\n" +
                $"CurElapTime:  {bgmService.CurrentElapsedTime:F1}\n" +
                $"lyricTime:    {lyricTime:F1}\n" +
                $"curTime:      {curTime:F1}\n" +
                $"actualOffset: {actualOffset:F1}\n" +
                $"loopTime: {loopTime}\n" +
                $"(actualOffset % loopTime): {actualOffset % loopTime}\n"
            );
            ImGui.Spacing();
        }
        else if (bgmService.LoadingSong)
        {
            ImGui.Spacing();
            ImGui.Text("Loading song data...");
            ImGui.Spacing();
            ImGui.NewLine();
            ImGui.Spacing();
            ImGui.NewLine();
            ImGui.Spacing();
        }
        else
        {
            ImGui.Spacing();
            ImGui.Text("No song data");
            ImGui.Spacing();
            ImGui.NewLine();
            ImGui.Spacing();
            ImGui.NewLine();
            ImGui.Spacing();
        }

        ImGui.Separator();


        var maxDelay = 350;
        ImGui.Spacing();
        ImGui.SliderFloat($"Lyric Delay", ref offset, -maxDelay, maxDelay, $"%.1fs {(offset < 0 ? "(earlier)" : "(later)")}");
        ImGui.Spacing();

        ImGui.Separator();

        ImGui.Spacing();
        ImGui.Spacing();
        if (ImGui.Button("Log BGM"))
        {
            bgmService.LogData();
        }

        ImGui.Spacing();
    }
}
