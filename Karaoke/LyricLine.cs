using Dalamud.Utility;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Karaoke;

/// <summary>
/// A single line of the lyrics for a song. Has an start time when the
/// lyric first begins to be sung, a disappear time if there is
/// a long delay between this lyric and the next one,
/// the text of the lyric itself, and the lyric broken up
/// by word, if available.
/// </summary>
public partial struct LyricLine
{
    public readonly float StartTime;
    public readonly string Text;
    public string? TranslatedText = null;
    public float EndTime { get; private set; } = 0.0f;
    public readonly float Duration => EndTime - StartTime;
    public float TimeUntilNext { get; private set; } = 0.0f;
    public int? OverlappingLineIdx { get; set; } = null;
    public readonly LyricSegment[] Segments;

    public LyricLine(
        float startTime,
        string text,
        LyricSegment[]? segments = null
    )
    {
        StartTime = startTime;
        EndTime = startTime;
        Text = text;

        Segments = segments ?? [
            new LyricSegment(startTime, 0, Text.Length)
        ];

        if (Segments[^1].StartIdx == Text.Length)
            EndTime = Segments[^1].StartTime;
    }

    public LyricLine(
        LyricLine original,
        float offset
    )
    {
        StartTime = original.StartTime + offset;
        Text = original.Text;
        TranslatedText = original.TranslatedText;
        EndTime = original.EndTime + offset;
        Segments = original
            .Segments
            .Select(s => new LyricSegment(s, offset))
            .ToArray();
    }

    public LyricLine(string lyricStr)
    {

        if (LyricLineRegex().Match(lyricStr) is not { Success: true } match)
            throw new ArgumentException($"Line doesn't match lyric regex: '{lyricStr}'");

        var timestampTagStr = match.Groups[1].Value;
        var textStr = match.Groups[2].Value;

        try
        {
            StartTime = Util.ParseTime(timestampTagStr);
        }
        catch
        {
            throw new ArgumentException($"Invalid lyric timestamp: '{timestampTagStr}'");
        }

        try
        {
            (Text, Segments) = parseText(textStr, StartTime);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid lyric text/segments: '{textStr}\n{ex.Message}\n{ex.StackTrace}'");
        }

        if (Segments[^1].StartIdx == Text.Length)
            EndTime = Segments[^1].StartTime;
    }

    private static (string Text, LyricSegment[] Segments) parseText(string text, float lyricStartTime)
    {
        var resultText = text.Trim();

        var matches = SegmentRegex().Matches(resultText);

        if (matches.Count == 0)
            return (
                resultText,
                [new LyricSegment(lyricStartTime, 0, resultText.Length)]
            );

        var nonTagText = new StringBuilder();
        LyricSegment[] segments;
        int initSegment;
        var charIdxOffset = matches[0].Index;
        if (charIdxOffset > 0)
        {
            segments = new LyricSegment[matches.Count + 1];

            var segText = resultText[0..charIdxOffset].Trim();
            var isSyllable = segText.EndsWith('-');
            if (isSyllable)
                segText = segText[0..^1];

            if (matches.Count > 1 && !isSyllable)
                segText += ' ';

            charIdxOffset = segText.Length;

            segments[0] = new LyricSegment(lyricStartTime, 0, charIdxOffset);
            nonTagText.Append(segText);
            initSegment = 1;
        }
        else
        {
            segments = new LyricSegment[matches.Count];
            initSegment = 0;
        }

        var lastTextIdx = matches[^1].Groups[2].Value.IsNullOrWhitespace()
            ? matches.Count - 2
            : matches.Count - 1;

        for (var matchIdx = 0; matchIdx < matches.Count; matchIdx++)
        {
            var match = matches[matchIdx];
            var timeStr = match.Groups[1].Value;

            var lyricText = match.Groups[2].Value.Trim();
            var isSyllable = lyricText.EndsWith('-');
            if (isSyllable)
                lyricText = lyricText[0..^1];

            nonTagText.Append(lyricText);
            var segmentLength = lyricText.Length;
            if (matchIdx < lastTextIdx && lyricText != string.Empty && !isSyllable)
            {
                nonTagText.Append(' ');
                segmentLength += 1;
            }

            segments[matchIdx + initSegment] = new(
                Util.ParseTime(timeStr),
                charIdxOffset,
                charIdxOffset + segmentLength
            );

            charIdxOffset += segmentLength;
        }
        return (nonTagText.ToString(), segments);
    }

    public readonly int GetSegmentIdxAtTime(float currentTime)
    {
        for (var segIdx = Segments.Length - 1; segIdx >= 0; segIdx--)
        {
            if (currentTime >= Segments[segIdx].StartTime)
                return segIdx;
        }

        return 0;
    }

    public readonly string ToDisplayString()
    {
        if (Text != string.Empty)
            return Text;
        return "<???>";
    }

    public override string ToString()
    {
        var text = Text;
        return (
            $"[{Util.FormatTime(StartTime, 4)} -> {Util.FormatTime(EndTime, 4)} ({TimeUntilNext:F2}s) " +
            $"[{(OverlappingLineIdx?.ToString() ?? " ")}]] " +
            $"'{string.Join('|', Segments.Select(s => $"<{Util.FormatTime(s.StartTime, 2)}>{text[s.StartIdx..s.EndIdx]}"))}'" +
            $"{(TranslatedText is string transText ? $" [TRANSLATION: {transText}]" : string.Empty)}"
        );
    }

    public void AddNextLyricTiming(float timeTilNextLyric)
    {
        if (EndTime == 0.0f)
            EndTime = timeTilNextLyric;
        TimeUntilNext = Math.Max(timeTilNextLyric - Duration, 0f);
    }

    [GeneratedRegex(@"^\s*\[([^]]*)\]\s*(.*?)\s*$")]
    private static partial Regex LyricLineRegex();

    [GeneratedRegex(@"<(\d+:\d{2}\.\d{1,5})>([^<]*)")]
    private static partial Regex SegmentRegex();
}
