using System;
using System.Collections.Generic;
using System.Linq;

namespace Karaoke;

public class Song(
    uint id,
    string? name,
    float duration,
    float? loopStart,
    LyricLine[]? lyrics = null
    )
{
    /// <summary>
    /// Index placeholder for when no lyric should be considered active
    /// (i.e. between lyrics)
    /// </summary>
    public static readonly int EMPTY_LYRIC_IDX = -2;

    /// <summary>
    /// RowId in the BGM sheet, also the ID the spreadsheet
    /// </summary>
    public readonly uint Id = id;

    /// <summary>
    /// Name of the song, in EN. May be null if no data was
    /// found for this song Id in the spreadsheet
    /// </summary>
    public string? Name { get; private set; } = name;

    /// <summary>
    /// How long the song is in game, in seconds.
    /// </summary>
    public float Duration { get; private set; } = duration;

    /// <summary>
    /// When in the song does it loop back to when it reaches the end.
    /// Null if the song does not loop
    /// </summary>
    public float? LoopStart { get; private set; } = loopStart;

    /// <summary>
    /// What lyric index the song loops onto when it reaches the end.
    /// Equal to -1 if the song does not loop
    /// </summary>
    public int LoopLyricIdx { get; private set; }

    /// <summary>
    /// The lyrics for this particular song. May be null
    /// if no lyrics are found for this song
    /// </summary>
    public LyricLine[]? Lyrics { get; private set; } = lyrics;

    public string? LoadedFileName { get; private set; } = null;
    public string[] AvailableFileNames { get; private set; } = [];


    public IReadOnlyDictionary<SongTag, object> Tags { get; private set; } =
        new Dictionary<SongTag, object>();
    public bool LoadingLyrics = true;

    public void LoadLyricsAndTags(
        LyricLine[] lyrics,
        Dictionary<SongTag, object> tags,
        string? lyricFileName,
        string[] lyricFileNames
    )
    {
        LoadingLyrics = true;
        Tags = tags.ToDictionary();
        var offset = 0f;
        if (Tags.GetValueOrDefault(SongTag.Offset) is float globalOffset)
            offset = globalOffset;

        if (offset != 0)
        {
            for (var i = 0; i < lyrics.Length; i++)
            {
                lyrics[i] = new LyricLine(lyrics[i], offset);
            }
        }

        Lyrics = [.. lyrics.Where(l => l.StartTime >= 0 && Math.Max(l.StartTime, l.EndTime) < Duration).OrderBy(l => l.StartTime)];
        LoadedFileName = lyricFileName;
        AvailableFileNames = lyricFileNames;

        if (Tags.GetValueOrDefault(SongTag.Duration) is float taggedDur)
            Duration = taggedDur + offset;
        if (Tags.GetValueOrDefault(SongTag.Name) is string name)
            Name = name;
        if (Tags.GetValueOrDefault(SongTag.LoopLineIndex) is int loopLineIdx)
        {
            if (LoopStart is not float curLoopStart)
                throw new Exception("Cannot specify a loop line idx for a song that does not loop");
            var newLoopStart = Lyrics[loopLineIdx].StartTime;
            LoopLyricIdx = loopLineIdx;
            Duration += newLoopStart - curLoopStart;
            LoopStart = newLoopStart;
            Lyrics = [.. Lyrics.Where(l => l.StartTime >= 0 && Math.Max(l.StartTime, l.EndTime) < Duration)];
        }
        else if (LoopStart is not float curLoopStart)
        {
            LoopLyricIdx = -1;
        }
        else if (Lyrics.Length > 0)
        {
            LoopLyricIdx = -1;
            var loopLyricIdx = getLyricIdxAtTime(curLoopStart) ?? 0;

            if (loopLyricIdx >= 0 && loopLyricIdx < Lyrics.Length)
            {
                LoopLyricIdx = loopLyricIdx;
                if (Lyrics[loopLyricIdx].StartTime < LoopStart)
                    LoopLyricIdx++;
            }
            else
            {
                LoopLyricIdx = -1;
            }
        }
        else
        {
            LoopLyricIdx = 0;
        }

        for (var i = 0; i < Lyrics.Length; i++)
        {
            var lyric = Lyrics[i];

            var nextLyricIdx = GetNextLyricIdx(i);
            if (nextLyricIdx >= 0 && nextLyricIdx < Lyrics.Length)
            {
                var nextLyric = Lyrics[nextLyricIdx];

                var timeBetweenLyrics = nextLyric.StartTime - lyric.StartTime;

                if (lyric.StartTime > nextLyric.StartTime && LoopStart is float actualLoopStart)
                    timeBetweenLyrics = (Duration - lyric.StartTime) + (nextLyric.StartTime - actualLoopStart);

                Lyrics[i].AddNextLyricTiming(timeBetweenLyrics);
            }
        }

        for (var i = 1; i < Lyrics.Length; i++)
        {
            for (var j = 0; j < i; j++)
            {
                if (Lyrics[j].EndTime > Lyrics[i].StartTime)
                {
                    Lyrics[j].OverlappingLineIdx ??= j;
                    Lyrics[i].OverlappingLineIdx = j;
                    break;
                }
            }
        }

        LoadingLyrics = false;
    }

    public float LoopElapsedTime(
        float rawElapsedTime,
        float overflowOffset = 0
    ) => LoopTime(
        rawElapsedTime,
        Duration,
        overflowOffset,
        LoopStart
    );

    public static float LoopTime(
        float totalTime,
        float duration,
        float overflowOffset = 0,
        float? loopStartTime = null
    )
    {
        // for songs that don't loop
        if (loopStartTime is not float loopStart)
            return totalTime + overflowOffset;

        float loopElapsedTime;

        if (overflowOffset < 0)
        {
            var timeSinceBoundary = totalTime;
            if (timeSinceBoundary >= loopStart)
                timeSinceBoundary -= loopStart;

            var overflowPastBoundary = timeSinceBoundary + overflowOffset;
            loopElapsedTime = overflowPastBoundary <= 0
                ? duration + (overflowPastBoundary % (duration - loopStart))
                : (totalTime + overflowOffset);
        }
        else
        {
            var offsetElapsedTime = totalTime + overflowOffset;
            loopElapsedTime = offsetElapsedTime >= duration
                ? ((offsetElapsedTime - loopStart) % (duration - loopStart)) + loopStart
                : offsetElapsedTime;
        }

        return loopElapsedTime;
    }

    private int? getLyricIdxAtTime(float time)
    {
        if (Lyrics is null || Lyrics.Length == 0)
            return null;

        if (time < Lyrics[0].StartTime)
            return 0;

        if (time > Lyrics[^1].EndTime)
            return LoopLyricIdx >= 0 ? LoopLyricIdx : Lyrics.Length;

        var lo = 0;
        var hi = Lyrics.Length - 1;
        var result = Lyrics.Length;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (Lyrics[mid].EndTime > time)
            {
                result = mid;
                hi = mid - 1; // keep searching for earlier match
            }
            else
            {
                lo = mid + 1;
            }
        }

        if (result == Lyrics.Length)
            return null;

        if (result < 0)
            return Lyrics.Length - 1;

        return result;
    }

    public int GetLyricIdxAtTime(float time)
    {
        return getLyricIdxAtTime(time) ?? -1;
    }

    public int GetLatestActiveLyricIdxAtTime(float time)
    {
        if (Lyrics is null || Lyrics.Length == 0)
            return -1;

        if (time < Lyrics[0].StartTime)
            return -1;

        if (time > Lyrics[^1].EndTime)
        {
            if (LoopLyricIdx < 0)
                return -1;

            if (time - Lyrics[^1].EndTime >= Lyrics[^1].TimeUntilNext)
                return LoopLyricIdx;

            return -1;
        }

        var lo = 0;
        var hi = Lyrics.Length - 1;
        var result = -1;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (Lyrics[mid].StartTime <= time)
            {
                if (Lyrics[mid].EndTime >= time)
                    result = mid;
                lo = mid + 1; // keep searching for later match
            }
            else
            {
                hi = mid - 1;
            }
        }

        return result;
    }

    public int GetNextLyricIdx(
        int lyricIdx,
        bool reverse = false,
        float wrapTimeAllowance = 0
    )
    {
        // check lyric idx is valid
        if (lyricIdx < 0 || lyricIdx > (Lyrics?.Length ?? lyricIdx))
            return -1;

        if (Lyrics?.Length is int lyricLen && lyricIdx == lyricLen)
            return reverse ? lyricLen - 1 : -1;

        // wrap back to end of song if configured to do so
        if (lyricIdx == LoopLyricIdx && reverse)
        {
            var lyric = Lyrics![^1];
            if (lyric.Duration + lyric.TimeUntilNext <= wrapTimeAllowance)
                return Lyrics!.Length - 1;

            return -1;
        }

        // wrap to start of loop if at end of lyrics
        if (lyricIdx == Lyrics?.Length - 1 && !reverse)
            return LoopLyricIdx;

        // only back up if there is enough time allowance to do so
        if (reverse)
        {
            // can't go back any farther
            if (lyricIdx == 0)
                return -1;

            var lyric = Lyrics![lyricIdx - 1];
            if (lyric.Duration + lyric.TimeUntilNext <= wrapTimeAllowance)
                return lyricIdx - 1;

            return -1;
        }

        // only case left is next lyric in sequence
        return lyricIdx + 1;
    }
}
