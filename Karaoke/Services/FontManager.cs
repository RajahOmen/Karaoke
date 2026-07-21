using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using Dalamud.Utility;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Karaoke.Services;

public class FontManager(IDalamudPluginInterface pluginInterface, Configuration configuration) : IHostedService
{
    public event Action? OnFontChange;
    private readonly IDalamudPluginInterface pluginInterface = pluginInterface;
    private readonly Configuration configuration = configuration;
    private IFontAtlas fontAtlas => pluginInterface.UiBuilder.FontAtlas;

    private IFontHandle? customLyricFont = null;
    public IFontHandle LyricFont => customLyricFont ?? pluginInterface.UiBuilder.DefaultFontHandle;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await BuildFonts();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        customLyricFont?.Dispose();
        return Task.CompletedTask;
    }

    public async Task BuildFonts()
    {
        var oldFont = customLyricFont;
        var fontSize = configuration.LyricFontSize;
        var fontType = configuration.LyricFont;
        if (fontSize is not null || fontType is not null)
        {
            fontSize ??= pluginInterface.UiBuilder.FontDefaultSizePt;
            fontType ??= GameFontFamily.Axis;
            var newFont = fontAtlas.NewGameFontHandle(new GameFontStyle(
                fontType ?? GameFontFamily.Axis,
                SizeInPx(Math.Max(1f, fontSize ?? 1f))
            ));
            await newFont.WaitAsync();
            customLyricFont = newFont;
        }
        else
        {
            customLyricFont = null;
        }
        oldFont?.Dispose();

        OnFontChange?.Invoke();
    }

    public static float SizeInPt(float px) => (float)(px * 3.0 / 4.0);
    public static float SizeInPx(float pt) => (float)(pt * 4.0 / 3.0);
}
