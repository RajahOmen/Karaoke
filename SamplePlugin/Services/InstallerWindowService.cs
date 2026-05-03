using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Microsoft.Extensions.Hosting;
using SamplePlugin.Windows;

namespace SamplePlugin;

public class InstallerWindowService : IHostedService
{
    public IDalamudPluginInterface PluginInterface { get; }
    public ConfigWindow ConfigWindow { get; }
    public MainWindow MainWindow { get; }

    public InstallerWindowService(IDalamudPluginInterface pluginInterface, ConfigWindow configWindow, MainWindow mainWindow)
    {
        PluginInterface = pluginInterface;
        ConfigWindow = configWindow;
        MainWindow = mainWindow;
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        return Task.CompletedTask;
    }

    private void ToggleMainUi()
    {
        MainWindow.Toggle();
    }

    private void ToggleConfigUi()
    {
        ConfigWindow.Toggle();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        return Task.CompletedTask;
    }
}
