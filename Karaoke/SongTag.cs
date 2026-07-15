namespace Karaoke;

public enum SongTag
{
    Name,
    Artist,
    Album,
    SongAuthor,
    // can also credit work done by community/fans to decipher lyrics
    Lyricist,
    LrcAuthor,
    Offset,
    Duration,
    LoopLineIndex,
    BgmIds,
    Comment,
    // not a song tag in the lyric file itself, but the name of the subdirectory it comes from
    LyricCategory,
}
