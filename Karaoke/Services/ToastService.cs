using Dalamud.Game.Gui.Toast;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Karaoke.Services;

public class ToastService(
    IFramework framework,
    IToastGui toastGui,
    BGMService bgmService,
    IPluginLog pluginLog,
    Configuration configuration
) : IHostedService
{
    private readonly IFramework framework = framework;
    private readonly IToastGui toastGui = toastGui;
    private readonly BGMService bgmService = bgmService;
    private readonly IPluginLog pluginLog = pluginLog;
    private readonly Configuration configuration = configuration;

    private LyricLine? currentLyric = null;

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
        if (!configuration.DisplayLyricInToast)
            return;

        if (!updateCurrentLyric())
            return;

        if (currentLyric is not LyricLine newLyric)
            return;

        toastGui.ShowQuest($"\"{newLyric.Text}\"");
    }

    private LyricLine? getCurrentLyric()
    {
        if (!configuration.DisplayLyricInToast)
            return null;

        if (bgmService.CurrentSong is { Lyrics.Length: > 0 } song)
        {
            var lyricIdx = song.GetLatestActiveLyricIdxAtTime(bgmService.CurrentLyricTime + 0.15f);
            if (lyricIdx < 0 || lyricIdx >= song.Lyrics.Length)
                return null;

            var lyric = song.Lyrics[lyricIdx];

            return lyric;
        }

        return null;
    }

    private bool updateCurrentLyric()
    {
        if (!configuration.DisplayLyricInToast)
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
