using Dalamud.Plugin;
using Karaoke.Windows;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace Karaoke.Services;

public class InstallerWindowService(
    IDalamudPluginInterface pluginInterface,
    ConfigWindow configWindow,
    LyricPlayerWindow lyricPlayerWindow
) : IHostedService
{
    private readonly IDalamudPluginInterface pluginInterface = pluginInterface;
    private readonly ConfigWindow configWindow = configWindow;
    private readonly LyricPlayerWindow lyricPlayerWindow = lyricPlayerWindow;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        pluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        pluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        return Task.CompletedTask;
    }

    private void ToggleMainUi()
    {
        lyricPlayerWindow.Toggle();
    }

    private void ToggleConfigUi()
    {
        configWindow.Toggle();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        return Task.CompletedTask;
    }
}
