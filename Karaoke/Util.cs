using System;
using System.Text.RegularExpressions;

namespace Karaoke;

public static partial class Util
{
    public static float ParseTime(string timeStr)
    {
        var match = TimeFormatRegex().Match(timeStr);
        if (!match.Success)
            throw new FormatException($"Invalid format: '{timeStr}'. Expected MM:SS.X[XXX]");

        var minutes = int.Parse(match.Groups[1].Value);
        var seconds = float.Parse(match.Groups[2].Value);
        return (minutes * 60) + seconds;
    }

    public static string FormatTime(float seconds, int decPlaces = 0, bool padMins = true)
    {
        var minutes = Math.Floor(seconds / 60);
        var remSeconds = Math.Round(seconds % 60, decPlaces);
        if (remSeconds >= 60)
        {
            remSeconds = 0;
            minutes += 1;
        }

        var mins = padMins
            ? $"{minutes:00}"
            : $"{minutes}";

        if (decPlaces == 0)
            return $"{mins}:{remSeconds:00}";

        var fullSecs = Math.Floor(remSeconds);
        var decSecsStr = (remSeconds - fullSecs)
            .ToString("F5")[2..]
            .TrimEnd('0')
            .PadRight(decPlaces, '0');

        return $"{mins}:{fullSecs:00}.{decSecsStr}";
    }

    [GeneratedRegex(@"(\d{1,2}):(\d{2}\.\d{1,5})$")]
    private static partial Regex TimeFormatRegex();
}
