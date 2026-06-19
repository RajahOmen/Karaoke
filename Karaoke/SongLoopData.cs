namespace Karaoke;

public readonly struct SongLoopData(
    uint totalDurationMillis,
    uint loopStartMillis,
    uint loopDurationMillis
)
{
    public readonly uint TotalDurationMillis = totalDurationMillis;
    public readonly uint LoopStartMillis = loopStartMillis;
    public readonly uint LoopDurationMillis = loopDurationMillis;
}
