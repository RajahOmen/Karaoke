using Dalamud.Game.Gui.FlyText;
using Dalamud.Game.Gui.Toast;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkModule;
using static FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkModule.Delegates;

namespace Karaoke.Services;

public class FlyTextService(
    IFramework framework,
    IFlyTextGui flyTextGui,
    BGMService bgmService,
    IPluginLog pluginLog,
    Configuration configuration
) : IHostedService
{
    private readonly IFramework framework = framework;
    private readonly IFlyTextGui flyTextGui = flyTextGui;
    private readonly BGMService bgmService = bgmService;
    private readonly IPluginLog pluginLog = pluginLog;
    private readonly Configuration configuration = configuration;

    private LyricLine? currentLyric = null;
    private int curSegIdx = -1;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        framework.Update += onFrameworkUpdate;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        framework.Update -= onFrameworkUpdate;
        return Task.CompletedTask;
    }

    public void onFrameworkUpdate(IFramework framework)
    {
        if (!configuration.DisplayLyricInFlyText)
            return;

        if (!updateCurrentLyric())
            return;

        if (currentLyric is not LyricLine newLyric)
            return;

        flyTextGui.AddFlyText(FlyTextKind.Named, 1, 0, 0, newLyric.Text, "", 0, 0, 0);
    }

    private LyricLine? getCurrentLyric()
    {
        if (!configuration.DisplayLyricInFlyText)
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
        if (!configuration.DisplayLyricInFlyText)
        {
            var changed = currentLyric is not null;
            currentLyric = null;
            return changed;
        }

        var newLyric = getCurrentLyric();

        if (!newLyric.Equals(currentLyric))
        {
            currentLyric = newLyric;
            return true;
        }

        return false;
    }
}
