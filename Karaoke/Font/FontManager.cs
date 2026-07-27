using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Karaoke.Font;

public class FontManager(IDalamudPluginInterface pluginInterface, Configuration configuration, IPluginLog pluginLog) : IHostedService
{
    public event Action? OnFontChange;
    private readonly IDalamudPluginInterface pluginInterface = pluginInterface;
    private readonly Configuration configuration = configuration;
    private readonly IPluginLog pluginLog = pluginLog;

    private IFontAtlas fontAtlas => pluginInterface.UiBuilder.FontAtlas;

    private IFontHandle? customLyricFont = null;
    public IFontHandle LyricFont => customLyricFont ?? pluginInterface.UiBuilder.DefaultFontHandle;

    public static FrozenDictionary<FontType, (string Name, string? Path)> LyricFontNames = new Dictionary<FontType, (string Name, string? Path)>() {
        { FontType.GameAxis, ("Axis", null) },
        { FontType.GameJupiter, ("Jupiter", null) },
        { FontType.GameTrumpGothic, ("Trump Gothic", null) },
        { FontType.GameMiedingerMid, ("Miedinger Mid", null) }
    }.ToFrozenDictionary();

    private float lyricFontSizePt => Math.Max(
        configuration.LyricFontSize ?? pluginInterface.UiBuilder.FontDefaultSizePt,
        1f
    );

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await BuildFonts();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        customLyricFont?.Dispose();
        return Task.CompletedTask;
    }

    private Stream getResourceStream(string path)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path);
        if (stream is null)
        {
            pluginLog.Debug($"Font stream is null for path: {path}");
        }
        return stream!;
    }

    private Stream getFontFileStream(string path)
    {
        if (!Path.Exists(path))
        {
            pluginLog.Debug($"Font file stream is null for file path: [{path}]");
            return null!;
        }

        return File.OpenRead(path);
    }
    

    private IFontHandle buildDelegateFontHandle(string fontPath, bool embedded = true)
    {
        var debugName = embedded
            ? $"Karaoke-{fontPath.Split('.')[^2]}"
            : $"Karaoke-{(fontPath != string.Empty ? new FileInfo(fontPath).Name : $"UNK-CUSTOM-FONT-{fontPath}")}";

        return fontAtlas.NewDelegateFontHandle(step => {
            var fontStream = embedded
                ? getResourceStream(fontPath)
                : getFontFileStream(fontPath);
            step.OnPreBuild(tk => {
                var conf = new SafeFontConfig
                {
                    SizePt = lyricFontSizePt
                };
                conf.MergeFont = tk.AddFontFromStream(fontStream, conf, false, debugName);
                conf.MergeFont = tk.AddGameSymbol(conf);
                conf.MergeFont = tk.SetFontScaleMode(conf.MergeFont, FontScaleMode.UndoGlobalScale);
                tk.Font = conf.MergeFont;
            });
        });
    }

    private IFontHandle buildGameFontHandle(GameFontFamily fontFamily) =>
        fontAtlas.NewGameFontHandle(new(fontFamily, sizePtToPx(lyricFontSizePt)));

    private IFontHandle buildDalamudFontHandle() =>
        fontAtlas.NewDelegateFontHandle(step =>
            step.OnPreBuild(tk => tk.AddDalamudDefaultFont(sizePtToPx(lyricFontSizePt)))
        );

    public async Task BuildFonts()
    {
        var oldFont = customLyricFont;

        var newFont = (configuration.LyricFont, configuration.LyricFontSize, configuration.CustomLyricFontPath) switch
        {
            (FontType.Custom, _, string path) when Path.Exists(path) => buildDelegateFontHandle(path, embedded: false),
            (FontType.GameAxis, _, _) => buildGameFontHandle(GameFontFamily.Axis),
            (FontType.GameJupiter, _, _) => buildGameFontHandle(GameFontFamily.Jupiter),
            (FontType.GameTrumpGothic, _, _) => buildGameFontHandle(GameFontFamily.TrumpGothic),
            (FontType.GameMiedingerMid, _, _) => buildGameFontHandle(GameFontFamily.MiedingerMid),
            (FontType.OpenDyslexic, _, _) => buildDelegateFontHandle("Karaoke.Font.OpenDyslexic3-Regular.ttf"),
            (FontType.AtkinsonHyperlegible, _, _) => buildDelegateFontHandle("Karaoke.Font.AtkinsonHyperlegible-Regular.ttf"),
            (FontType.ComicRelief, _, _) => buildDelegateFontHandle("Karaoke.Font.ComicRelief-Regular.ttf"),
            (FontType.VcrOsdMono, _, _) => buildDelegateFontHandle("Karaoke.Font.VCR_OSD_MONO_1.001.ttf"),
            (_, float, _) => buildDalamudFontHandle(),
            _ => null,
        };

        await (newFont?.WaitAsync() ?? Task.CompletedTask);

        if (newFont?.LoadException is Exception ex)
        {
            pluginLog.Warning(ex, $"Error loading font (type: {configuration.LyricFont}, custom path: {configuration.CustomLyricFontPath})");
            newFont.Dispose();
            newFont = null;
        }

        customLyricFont = newFont;
        oldFont?.Dispose();
        OnFontChange?.Invoke();
    }
    private static float sizePtToPx(float pt) => pt * 4/3;
}
