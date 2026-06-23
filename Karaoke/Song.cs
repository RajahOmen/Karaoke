using System.Collections.Generic;
using System.Linq;

namespace Karaoke;

public class Song(
    uint id,
    string? name,
    float duration,
    float loopStart,
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
    /// </summary>
    public float LoopStart { get; private set; } = loopStart;

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
        Tags = tags.ToDictionary();
        var offset = 0f;
        if (Tags.GetValueOrDefault(SongTag.BgmIdOffsets) is (uint Id, float Offset)[] offsets)
        {
            foreach (var (id, idOffset) in offsets)
            {
                if (id == Id)
                {
                    offset = idOffset;
                    break;
                }
            }
        }

        if (offset > 0)
        {
            Duration += offset;
            LoopStart += offset;
            for (var i = 0; i < lyrics.Length; i++)
            {
                lyrics[i] = new LyricLine(lyrics[i], offset);
            }
        }


        Lyrics = [.. lyrics];
        LoadedFileName = lyricFileName;
        AvailableFileNames = lyricFileNames;

        if (Tags.GetValueOrDefault(SongTag.Duration) is float taggedDur)
            Duration = taggedDur + offset;
        if (Tags.GetValueOrDefault(SongTag.Name) is string name)
            Name = name;
        if (Tags.GetValueOrDefault(SongTag.LoopLineIndex) is int loopLineIdx)
        {
            var newLoopStart = Lyrics[loopLineIdx].StartTime;
            LoopLyricIdx = loopLineIdx;
            Duration += newLoopStart - LoopStart;
            LoopStart = newLoopStart;
        }
        else
        {
            var loopLyricIdx = getLyricIdxAtTime(LoopStart) ?? 0;
            if (Lyrics[loopLyricIdx].StartTime < LoopStart)
                loopLyricIdx++;
            LoopLyricIdx = loopLyricIdx;
        }

        for (var i = 0; i < Lyrics.Length; i++)
        {
            var lyric = Lyrics[i];
            var nextLyric = Lyrics[GetNextLyricIdx(i)];

            var timeBetweenLyrics = lyric.StartTime <= nextLyric.StartTime
                ? nextLyric.StartTime - lyric.StartTime
                : (Duration - lyric.StartTime) + (nextLyric.StartTime - LoopStart);
            Lyrics[i].AddNextLyricTiming(timeBetweenLyrics);
        }

        LoadingLyrics = false;
    }

    public float LoopElapsedTime(
        float rawElapsedTime,
        float overflowOffset = 0
    )
    {
        float loopElapsedTime;

        if (overflowOffset < 0)
        {
            var timeSinceBoundary = rawElapsedTime;
            if (timeSinceBoundary >= LoopStart)
                timeSinceBoundary -= LoopStart;

            var overflowPastBoundary = timeSinceBoundary + overflowOffset;
            loopElapsedTime = overflowPastBoundary <= 0
                ? Duration + (overflowPastBoundary % (Duration - LoopStart))
                : (rawElapsedTime + overflowOffset);
        }
        else
        {
            var offsetElapsedTime = rawElapsedTime + overflowOffset;
            loopElapsedTime = offsetElapsedTime >= Duration
                ? ((offsetElapsedTime - LoopStart) % (Duration - LoopStart)) + LoopStart
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
            return LoopLyricIdx;

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

    public int GetNextLyricIdx(
        int lyricIdx,
        bool reverse = false,
        bool wrapToEnd = false
    )
    {
        // check lyric idx is valid
        if (lyricIdx < 0 || lyricIdx >= (Lyrics?.Length ?? lyricIdx))
            return -1;

        // wrap back to end of song if configured to do so
        if (lyricIdx == LoopLyricIdx && reverse && wrapToEnd)
            return Lyrics!.Length - 1;

        // wrap to start of loop if at end of lyrics
        if (lyricIdx == Lyrics?.Length - 1 && !reverse)
            return LoopLyricIdx;

        // else, increment in direction specified
        return reverse ? lyricIdx - 1 : lyricIdx + 1;
    }
}
