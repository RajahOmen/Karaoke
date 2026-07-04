using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Karaoke.Windows;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace Karaoke.Services;

public class CommandService(
    ICommandManager commandManager,
    BGMService bgmService,
    DebugWindow debugWindow,
    LyricPlayerWindow lyricPlayerWindow,
    ConfigWindow configWindow
    ) : IHostedService
{
    private const string CommandName = "/karaoke";
    private readonly LyricPlayerWindow lyricPlayerWindow = lyricPlayerWindow;
    private readonly ConfigWindow configWindow = configWindow;
    private readonly BGMService bgmService = bgmService;
    private readonly DebugWindow debugWindow = debugWindow;

    public ICommandManager CommandManager { get; } = commandManager;

    private const string HelpMessage = """
    Open the lyric player window
        - config: open config window
        - reload: reload lyrics cache
        - debug: open debug window
    """;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = HelpMessage,
        });
        return Task.CompletedTask;
    }

    private void OnCommand(string command, string arguments)
    {
        if (arguments == "config")
        {
            configWindow.Toggle();
        }
        else if (arguments == "debug")
        {
            debugWindow.Toggle();
        }
        else if (arguments == "reload")
        {
            bgmService.ReloadCurrentSongLyrics();
        }
        else
        {
            lyricPlayerWindow.OpenedManually = !lyricPlayerWindow.IsOpen;
            lyricPlayerWindow.Toggle();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        CommandManager.RemoveHandler(CommandName);
        return Task.CompletedTask;
    }
}
