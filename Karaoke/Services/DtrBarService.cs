using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Karaoke.Windows;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Karaoke.Services;

public class DtrBarService(
    IFramework framework,
    IDtrBar dtrBar,
    BGMService bgmService,
    Configuration configuration,
    LyricPlayerWindow lyricPlayerWindow,
    IPluginLog pluginLog
    ) : IHostedService
{
    private readonly IFramework framework = framework;
    private readonly IDtrBar dtrBar = dtrBar;
    private readonly BGMService bgmService = bgmService;
    private readonly Configuration configuration = configuration;
    private readonly LyricPlayerWindow lyricPlayerWindow = lyricPlayerWindow;
    private readonly IPluginLog pluginLog = pluginLog;
    private const string DtrBarTitle = "Karaoke Lyric";
    private IDtrBarEntry? dtrBarEntry { get; set; } = null;

    private LyricLine? currentLyric = null;
    private int curSegmentIdx = -1;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        dtrBarEntry = dtrBar.Get(DtrBarTitle);
        dtrBarEntry.OnClick = (_) => lyricPlayerWindow.Toggle();
        framework.Update += onFrameworkUpdate;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        dtrBar.Remove(DtrBarTitle);
        framework.Update -= onFrameworkUpdate;
        return Task.CompletedTask;
    }

    public void ClearCache()
    {
        currentLyric = null;
        curSegmentIdx = -1;
    }

    private void onFrameworkUpdate(IFramework framework)
    {
        if (dtrBarEntry is null)
            return;

        if (!updateCurrentLyric())
            return;
        

        if (currentLyric is LyricLine lyric)
        {
            var builder = new SeStringBuilder();

            var startIdx = 0;
            var endIdx = 0;

            if (curSegmentIdx >= 0)
            {
                var seg = lyric.Segments[curSegmentIdx];
                endIdx = seg.EndIdx;
                startIdx = configuration.DtrBarLyricDisplayMode switch
                {
                    DtrBarLyricDisplayType.LineHighlightWord => seg.StartIdx,
                    _ => 0
                };
            }

            if (startIdx > 0 || endIdx == 0)
                builder.AddText($"\"{lyric.Text[..startIdx]}");

            builder.AddUiForeground(configuration.DtrBarTextColor);
            builder.AddUiGlow(configuration.DtrBarGlowColor);

            if (startIdx == 0 && endIdx > 0)
                builder.AddText("\"");
            builder.AddText(lyric.Text[startIdx..endIdx]);

            if (curSegmentIdx >= lyric.Segments.Length - 2)
            {
                builder.AddText("\"");
                builder.AddUiForegroundOff();
                builder.AddUiGlowOff();
            }
            else
            {
                builder.AddUiForegroundOff();
                builder.AddUiGlowOff();
                builder.AddText($"{lyric.Text[endIdx..]}\"");
            }

            dtrBarEntry.Text = builder.Build();
            dtrBarEntry.Tooltip = $"[Karaoke] {lyric.TranslatedText}".Trim();
        }
        else
        {
            dtrBarEntry.Text = "";
            dtrBarEntry.Tooltip = "";
        }

    }

    private LyricLine? getCurrentLyric()
    {
        if (configuration.DtrBarLyricDisplayMode == DtrBarLyricDisplayType.None)
            return null;

        if (bgmService.CurrentSong is { Lyrics.Length: > 0 } song)
        {
            var lyricIdx = song.GetLatestActiveLyricIdxAtTime(bgmService.CurrentLyricTime);
            if (lyricIdx < 0 || lyricIdx >= song.Lyrics.Length)
                return null;

            var lyric = song.Lyrics[lyricIdx];

            return lyric;
        }

        return null;
    }

    private bool updateCurrentLyric()
    {
        if (configuration.DtrBarLyricDisplayMode == DtrBarLyricDisplayType.None)
        {
            var changed = currentLyric is not null;
            currentLyric = null;
            curSegmentIdx = -1;
            return changed;
        }

        var newLyric = getCurrentLyric();

        if (newLyric is null && currentLyric is not null)
        {
            currentLyric = null;
            curSegmentIdx = -1;
            return true;
        }

        if (newLyric is not null && currentLyric is null)
        {
            currentLyric = newLyric;

            var segIdx = -1;
            if (configuration.DtrBarLyricDisplayMode != DtrBarLyricDisplayType.LinePlain)
            {
                var newSegIdx = newLyric.Value.GetSegmentIdxAtTime(bgmService.CurrentLyricTime);
                if (newLyric.Value.Segments[newSegIdx].StartTime <= bgmService.CurrentLyricTime)
                    segIdx = newSegIdx;
            }

            curSegmentIdx = segIdx;
            return true;
        }

        if (newLyric is not null && currentLyric is not null)
        {
            if (configuration.DtrBarLyricDisplayMode == DtrBarLyricDisplayType.LinePlain)
            {
                if (newLyric.Equals(currentLyric))
                    return false;

                currentLyric = newLyric;
                curSegmentIdx = -1;
                return true;
            } 

            var newSegIdx = newLyric.Value.GetSegmentIdxAtTime(bgmService.CurrentLyricTime);
            if (newLyric.Value.Segments[newSegIdx].StartTime > bgmService.CurrentLyricTime)
                newSegIdx = -1;
            if (!newLyric.Equals(currentLyric))
            {
                currentLyric = newLyric;
                curSegmentIdx = newSegIdx;
                return true;
            }
            if (newSegIdx != curSegmentIdx)
            {
                curSegmentIdx = newSegIdx;
                return true;
            }
        }

        return false;
    }
}
