namespace Scriber.Core.Diff;

public enum ReconciliationDecision
{
    Pending,
    KeepEdited,
    UseNew
}

public sealed class ReconciliationConflict
{
    public int SegmentIndex { get; init; }
    public string OriginalEditedText { get; init; }
    public string NewModelText { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public ReconciliationDecision Decision { get; set; } = ReconciliationDecision.Pending;

    public ReconciliationConflict(
        int segmentIndex,
        string originalEditedText,
        string newModelText,
        TimeSpan start,
        TimeSpan end,
        ReconciliationDecision decision = ReconciliationDecision.Pending)
    {
        SegmentIndex = segmentIndex;
        OriginalEditedText = originalEditedText;
        NewModelText = newModelText;
        Start = start;
        End = end;
        Decision = decision;
    }
}
