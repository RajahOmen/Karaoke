using System;
using System.Collections.Generic;

namespace Karaoke;

// taken from https://github.com/perchbirdd/OrchestrionPlugin

public struct SongStrings
{
    public string Name;
    public string AlternateName;
    public string SpecialModeName;
    public string Locations;
    public string AdditionalInfo;
}

public struct SpreadsheetSong
{
    public int Id;
    public Dictionary<string, SongStrings> Strings;
    public bool DisableRestart;
    public byte SpecialMode;
    public string FilePath = string.Empty;
    public bool FileExists;
    public TimeSpan Duration;

    public SpreadsheetSong(Dictionary<string, SongStrings> strings)
    {
        Strings = strings;
    }

    public SpreadsheetSong()
    {
        Strings = [];
    }

    public readonly string Name => Strings.GetValueOrDefault(SpreadsheetUtil.Lang(), Strings["en"]).Name;
    public readonly string AlternateName => Strings.GetValueOrDefault(SpreadsheetUtil.Lang(), Strings["en"]).AlternateName;
    public readonly string SpecialModeName => Strings.GetValueOrDefault(SpreadsheetUtil.Lang(), Strings["en"]).SpecialModeName;
    public readonly string Locations => Strings.GetValueOrDefault(SpreadsheetUtil.Lang(), Strings["en"]).Locations;
    public readonly string AdditionalInfo => Strings.GetValueOrDefault(SpreadsheetUtil.Lang(), Strings["en"]).AdditionalInfo;
}

public static class SpreadsheetUtil
{
    public static string Lang() => "en";
}
