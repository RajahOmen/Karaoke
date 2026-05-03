using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;
using SamplePlugin.Windows;

namespace SamplePlugin;

public class CommandService : IHostedService
{
    private const string CommandName = "/pmycommand";
    public ICommandManager CommandManager { get; }
    public MainWindow MainWindow { get; }

    public CommandService(ICommandManager commandManager, MainWindow mainWindow)
    {
        CommandManager = commandManager;
        MainWindow = mainWindow;
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "A useful message to display in /xlhelp"
        });
        return Task.CompletedTask;
    }

    private void OnCommand(string command, string arguments)
    {
        MainWindow.Toggle();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        CommandManager.RemoveHandler(CommandName);
        return Task.CompletedTask;
    }
}
