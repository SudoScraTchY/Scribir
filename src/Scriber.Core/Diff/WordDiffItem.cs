namespace Scriber.Core.Diff;

public sealed record WordDiffItem(
    DiffOperation Operation,
    string? OldWord,
    string? NewWord,
    TimeSpan? Start = null,
    TimeSpan? End = null
);
