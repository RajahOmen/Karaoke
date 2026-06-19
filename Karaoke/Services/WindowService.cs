using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Karaoke.Services;

public class WindowService(
    IDalamudPluginInterface pluginInterface,
    IEnumerable<Window> pluginWindows,
    WindowSystem windowSystem
    ) : IHostedService
{
    public IDalamudPluginInterface PluginInterface { get; } = pluginInterface;
    public IEnumerable<Window> PluginWindows { get; } = pluginWindows;
    public WindowSystem WindowSystem { get; } = windowSystem;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var pluginWindow in PluginWindows)
        {
            WindowSystem.AddWindow(pluginWindow);
        }

        PluginInterface.UiBuilder.Draw += UiBuilderOnDraw;


        return Task.CompletedTask;
    }

    private void UiBuilderOnDraw()
    {
        WindowSystem.Draw();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        PluginInterface.UiBuilder.Draw -= UiBuilderOnDraw;
        WindowSystem.RemoveAllWindows();
        return Task.CompletedTask;
    }
}
