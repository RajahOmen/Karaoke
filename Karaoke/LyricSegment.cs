namespace Karaoke;

public readonly struct LyricSegment(
    float startTime, int startIdx, int endIdx
)
{
    public readonly float StartTime = startTime;
    public readonly int StartIdx = startIdx;
    public readonly int EndIdx = endIdx;

    public LyricSegment(
        LyricSegment original,
        float offset
    ) : this(
        original.StartTime + offset,
        original.StartIdx,
        original.EndIdx
    )
    { }
}
