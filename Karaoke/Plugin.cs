using Dalamud.Interface.Windowing;
using Dalamud.Networking.Http;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Karaoke.Services;
using Karaoke.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Karaoke;

public sealed class Plugin : IAsyncDalamudPlugin
{
    private readonly IHost host;

    public Plugin(
        IFramework framework,
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        ITextureProvider textureProvider,
        IDataManager dataManager,
        IPluginLog pluginLog
        )
    {
        host = new HostBuilder()
            .UseContentRoot(pluginInterface.ConfigDirectory.FullName)
            .ConfigureLogging(lb =>
            {
                lb.ClearProviders();
                lb.SetMinimumLevel(LogLevel.Trace);
            })
            .ConfigureServices(collection =>
            {
                //Add dalamud services
                collection.AddSingleton(framework);
                collection.AddSingleton(pluginInterface);
                collection.AddSingleton(commandManager);
                collection.AddSingleton(textureProvider);
                collection.AddSingleton(dataManager);
                collection.AddSingleton(pluginLog);
                collection.AddSingleton<WindowService>();
                collection.AddSingleton<InstallerWindowService>();
                collection.AddSingleton<CommandService>();
                collection.AddSingleton<DebugWindow>();
                collection.AddSingleton<ConfigWindow>();
                collection.AddSingleton<LyricPlayerWindow>();
                collection.AddSingleton<BGMService>();
                collection.AddSingleton<SongNameService>();
                collection.AddSingleton<SongLoaderService>();

                // fetch from web
                collection.AddSingleton<HappyEyeballsCallback>();
                collection.AddSingleton(sp => new HttpClient(
                    new SocketsHttpHandler
                    {
                        ConnectCallback = sp.GetService<HappyEyeballsCallback>()!.ConnectCallback
                    }
                ));

                collection.AddSingleton<Window>(provider => provider.GetRequiredService<ConfigWindow>());
                collection.AddSingleton<Window>(provider => provider.GetRequiredService<DebugWindow>());
                collection.AddSingleton<Window>(provider => provider.GetRequiredService<LyricPlayerWindow>());

                //Add configuration
                collection.AddSingleton((s) =>
                {
                    var dalamudPluginInterface = s.GetRequiredService<IDalamudPluginInterface>();
                    var configuration = dalamudPluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
                    configuration.Initialize(dalamudPluginInterface);
                    return configuration;
                });

                //Add window system
                collection.AddSingleton(new WindowSystem("Karaoke"));

                //Services to automatically start when the plugin does
                collection.AddHostedService(p => p.GetRequiredService<WindowService>());
                collection.AddHostedService(p => p.GetRequiredService<CommandService>());
                collection.AddHostedService(p => p.GetRequiredService<InstallerWindowService>());
                collection.AddHostedService(p => p.GetRequiredService<BGMService>());
                collection.AddHostedService(p => p.GetRequiredService<SongNameService>());
                collection.AddHostedService(p => p.GetRequiredService<SongLoaderService>());

            }).Build();
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await host.StartAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await host.StopAsync();
        host.Dispose();
    }
}
