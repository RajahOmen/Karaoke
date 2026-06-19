using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Karaoke.Services;

// taken from https://github.com/perchbirdd/OrchestrionPlugin
public class SongNameService(
    IPluginLog pluginLog,
    IDataManager dataManager,
    IDalamudPluginInterface pluginInterface,
    HttpClient httpClient
) : IHostedService
{
    private const string SheetPath = @"https://docs.google.com/spreadsheets/d/1s-xJjxqp6pwS7oewNy1aOQnr3gaJbewvIBbyYchZ6No/gviz/tq?tqx=out:csv&sheet={0}";
    private const string SheetFileName = "xiv_bgm_{0}.csv";
    private readonly Dictionary<int, SpreadsheetSong> songs = [];
    private readonly HttpClient client = httpClient;
    private readonly IPluginLog pluginLog = pluginLog;
    private readonly IDataManager dataManager = dataManager;
    private readonly IDalamudPluginInterface pluginInterface = pluginInterface;

    private Task<string> GetRemoteSheet(string code)
    {
        return client.GetStringAsync(string.Format(SheetPath, code));
    }

    private string GetLocalSheet(string code)
    {
        return File.ReadAllText(Path.Combine(pluginInterface.AssemblyLocation.DirectoryName!, string.Format(SheetFileName, code)));
    }

    private void SaveLocalSheet(string text, string code)
    {
        File.WriteAllText(Path.Combine(pluginInterface.AssemblyLocation.DirectoryName!, string.Format(SheetFileName, code)), text);
    }

    private void LoadMetadataSheet(string sheetText)
    {
        songs.Clear();
        var bgms = dataManager.Excel.GetSheet<BGM>().ToDictionary(k => k.RowId, v => v);
        var sheetLines = sheetText.Split('\n'); // gdocs provides \n
        for (var i = 1; i < sheetLines.Length; i++)
        {
            // The formatting is odd here because gdocs adds quotes around columns and doubles each single quote
            var elements = sheetLines[i].Split(["\","], StringSplitOptions.None);
            var id = int.Parse(elements[0][1..]);
            var durationStr = elements[1][1..^1].Replace("\"\"", "\"");
            var parsed = double.TryParse(durationStr, out var durationDbl);
            var duration = parsed ? TimeSpan.FromSeconds(durationDbl) : TimeSpan.Zero;

            if (!bgms.TryGetValue((uint)id, out var bgm)) continue;
            var song = new SpreadsheetSong
            {
                Id = id,
                FilePath = bgm.File.ExtractText(),
                SpecialMode = bgm.SpecialMode,
                DisableRestart = bgm.DisableRestart,
                FileExists = dataManager.FileExists(bgm.File.ExtractText()),
                Duration = duration,
            };

            songs[id] = song;
        }
        SaveLocalSheet(sheetText, "metadata");
    }

    private void LoadLangSheet(string sheetText, string code)
    {
        var sheetLines = sheetText.Split('\n'); // gdocs provides \n
        for (var i = 1; i < sheetLines.Length; i++)
        {
            // The formatting is odd here because gdocs adds quotes around columns and doubles each single quote
            var elements = sheetLines[i].Split(["\","], StringSplitOptions.None);
            var id = int.Parse(elements[0][1..]);
            var name = elements[1][1..];
            var altName = elements[2][1..];
            var specialName = elements[3][1..];
            var locations = elements[4][1..];
            var addtlInfo = elements[5][1..^1].Replace("\"\"", "\"");

            if (!songs.TryGetValue(id, out var song))
                continue;

            if ((code == "en" && string.IsNullOrEmpty(name)) || name == "Null BGM" || name == "test")
                songs.Remove(id);

            song.Strings[code] = new SongStrings
            {
                Name = name,
                AlternateName = altName,
                SpecialModeName = specialName,
                Locations = locations,
                AdditionalInfo = addtlInfo,
            };
        }
        SaveLocalSheet(sheetText, code);
    }

    public string? GetSongName(uint id)
    {
        return songs.TryGetValue((int)id, out var song) ? song.Name : null;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            pluginLog.Information("[SongList] Checking for updated bgm sheets");

            List<string> langs = ["en", "ja", "de", "fr", "zh"];
            List<(string, Task<string>)> tasks = [];

            tasks.Add(("metadata", GetRemoteSheet("metadata")));
            foreach (var lang in langs)
                tasks.Add((lang, GetRemoteSheet(lang)));
            foreach (var (name, task) in tasks)
            {
                var result = await task;
                if (name == "metadata")
                    LoadMetadataSheet(result);
                else
                    LoadLangSheet(result, name);
            }
            pluginLog.Information($"[SongList] Update check complete");
        }
        catch (Exception e)
        {
            pluginLog.Error(e, "[SongList] Failed to update bgm sheet; using previous version");
            LoadMetadataSheet(GetLocalSheet("metadata"));
            LoadLangSheet(GetLocalSheet("en"), "en");
            LoadLangSheet(GetLocalSheet("ja"), "ja");
            LoadLangSheet(GetLocalSheet("de"), "de");
            LoadLangSheet(GetLocalSheet("fr"), "fr");
            LoadLangSheet(GetLocalSheet("zh"), "zh");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.Dispose();
        return Task.CompletedTask;
    }
}
