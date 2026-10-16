namespace Scriber.Core.Diff;

public sealed record SegmentDiff(
    int SegmentIndex,
    string OldSpeakerId,
    string NewSpeakerId,
    bool SpeakerChanged,
    TimeSpan Start,
    TimeSpan End,
    IReadOnlyList<WordDiffItem> Words
);
