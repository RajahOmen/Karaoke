using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SamplePlugin.Windows;

namespace SamplePlugin;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IHost _host;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        ITextureProvider textureProvider)
    {
        _host = new HostBuilder()
                .UseContentRoot(pluginInterface.ConfigDirectory.FullName)
                .ConfigureLogging(lb =>
                {
                    lb.ClearProviders();
                    lb.SetMinimumLevel(LogLevel.Trace);
                })
                .ConfigureServices(collection =>
                {
                    //Add dalamud services
                    collection.AddSingleton(pluginInterface);
                    collection.AddSingleton(commandManager);
                    collection.AddSingleton(textureProvider);
                    collection.AddSingleton<WindowService>();
                    collection.AddSingleton<InstallerWindowService>();
                    collection.AddSingleton<CommandService>();
                    collection.AddSingleton<MainWindow>();
                    collection.AddSingleton<ConfigWindow>();
                    
                    //Easier to do using autofac
                    collection.AddSingleton<Window>(provider => provider.GetRequiredService<ConfigWindow>());
                    collection.AddSingleton<Window>(provider => provider.GetRequiredService<MainWindow>());

                    //Add configuration
                    collection.AddSingleton((s) =>
                    {
                       var dalamudPluginInterface = s.GetRequiredService<IDalamudPluginInterface>();
                       var configuration = dalamudPluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
                       configuration.Initialize(dalamudPluginInterface);
                       return configuration;
                    });
                    
                    //Add window system
                    collection.AddSingleton(new WindowSystem("SamplePlugin"));
                    
                    //Services to automatically start when the plugin does
                    collection.AddHostedService(p => p.GetRequiredService<WindowService>());
                    collection.AddHostedService(p => p.GetRequiredService<CommandService>());
                    collection.AddHostedService(p => p.GetRequiredService<InstallerWindowService>());
                }).Build();

        _ = _host.StartAsync();;
    }

    public void Dispose()
    {
        _host.StopAsync().GetAwaiter().GetResult();
        _host.Dispose();
    }


}
