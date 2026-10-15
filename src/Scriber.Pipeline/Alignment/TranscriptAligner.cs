using Scriber.Core.Domain;
using Scriber.Inference.Asr;
using Scriber.Inference.Diarization;

namespace Scriber.Pipeline.Alignment;

/// <summary>
/// Merges diarization speaker turns and ASR recognized words into an ordered, speaker-attributed transcript.
/// Implements PH1-CORE-006.
/// </summary>
public sealed class TranscriptAligner : ITranscriptAligner
{
    public IReadOnlyList<Segment> Align(IReadOnlyList<SpeakerTurn> turns, IReadOnlyList<TranscriptSegment> asrSegments)
    {
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(asrSegments);

        var words = asrSegments.SelectMany(s => s.Words).OrderBy(w => w.Start).ToList();
        return Align(turns, words);
    }

    public IReadOnlyList<Segment> Align(IReadOnlyList<SpeakerTurn> turns, IReadOnlyList<Word> words)
    {
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(words);

        if (words.Count == 0)
            return Array.Empty<Segment>();

        // Fallback: If no speaker turns exist, assign all words to default speaker-0
        if (turns.Count == 0)
        {
            var orderedWords = words.OrderBy(w => w.Start).ToList();
            var start = orderedWords[0].Start;
            var end = orderedWords[^1].End;
            if (end <= start) end = start + TimeSpan.FromMilliseconds(50);
            return [new Segment("speaker-0", start, end, orderedWords)];
        }

        // 1. Attribute each word to its best matching SpeakerTurn
        var attributed = new List<(Word Word, SpeakerTurn Turn)>(words.Count);

        foreach (var word in words)
        {
            var turn = FindBestTurn(word, turns);
            attributed.Add((word, turn));
        }

        // 2. Group contiguous words belonging to the same speaker into Segments
        var segments = new List<Segment>();
        var currentWords = new List<Word>();
        string? currentSpeaker = null;
        TimeSpan turnStart = TimeSpan.Zero;
        TimeSpan turnEnd = TimeSpan.Zero;

        foreach (var (word, turn) in attributed)
        {
            if (currentSpeaker == null || turn.SpeakerId != currentSpeaker)
            {
                if (currentWords.Count > 0 && currentSpeaker != null)
                {
                    segments.Add(CreateSegment(currentSpeaker, turnStart, turnEnd, currentWords));
                    currentWords.Clear();
                }

                currentSpeaker = turn.SpeakerId;
                turnStart = turn.Start;
                turnEnd = turn.End;
            }
            else
            {
                // Expand turn boundaries for consecutive turns of the same speaker
                if (turn.Start < turnStart) turnStart = turn.Start;
                if (turn.End > turnEnd) turnEnd = turn.End;
            }

            currentWords.Add(word);
        }

        if (currentWords.Count > 0 && currentSpeaker != null)
        {
            segments.Add(CreateSegment(currentSpeaker, turnStart, turnEnd, currentWords));
        }

        return segments;
    }

    private static SpeakerTurn FindBestTurn(Word word, IReadOnlyList<SpeakerTurn> turns)
    {
        SpeakerTurn? bestTurn = null;
        double bestOverlapMs = 0;
        double wStartMs = word.Start.TotalMilliseconds;
        double wEndMs = word.End.TotalMilliseconds;

        foreach (var turn in turns)
        {
            double tStartMs = turn.Start.TotalMilliseconds;
            double tEndMs = turn.End.TotalMilliseconds;

            double overlapStart = Math.Max(wStartMs, tStartMs);
            double overlapEnd = Math.Min(wEndMs, tEndMs);
            double overlap = overlapEnd - overlapStart;

            if (overlap > 0)
            {
                // PH1-CORE-006: A word spanning a speaker-turn boundary is attributed
                // to the turn with greatest time overlap. For adjacent turns, this implements
                // midpoint split (the word is assigned to whichever side holds > 50% of its duration).
                //
                // PH1-CORE-006: Overlapping speech (both models may represent it) has an explicit
                // documented v1 simplification: when multiple turns overlap a word, the turn with
                // greatest overlap duration wins; exact ties are broken in favor of the earlier-starting
                // (first/dominant) turn.
                if (overlap > bestOverlapMs)
                {
                    bestOverlapMs = overlap;
                    bestTurn = turn;
                }
                else if (Math.Abs(overlap - bestOverlapMs) < 0.001 && bestTurn != null)
                {
                    // Tie-breaker: dominant/first turn
                    if (turn.Start < bestTurn.Start)
                    {
                        bestTurn = turn;
                    }
                }
            }
        }

        // If the word fell in a silence gap between turns or outside turn boundaries,
        // attribute it to the nearest turn by midpoint distance.
        if (bestTurn == null)
        {
            double wordMidpoint = (wStartMs + wEndMs) / 2.0;
            double minDistance = double.MaxValue;

            foreach (var turn in turns)
            {
                double turnMidpoint = (turn.Start.TotalMilliseconds + turn.End.TotalMilliseconds) / 2.0;
                double dist = Math.Abs(wordMidpoint - turnMidpoint);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestTurn = turn;
                }
            }
        }

        return bestTurn ?? turns[0];
    }

    private static Segment CreateSegment(string speakerId, TimeSpan turnStart, TimeSpan turnEnd, List<Word> words)
    {
        // Enforce Scriber.Core.Domain.Segment invariants:
        // - End > Start
        // - Every word must fall inside [Start, End]
        var segStart = turnStart;
        var segEnd = turnEnd;

        if (words.Count > 0)
        {
            if (words[0].Start < segStart)
                segStart = words[0].Start;
            if (words[^1].End > segEnd)
                segEnd = words[^1].End;
        }

        if (segEnd <= segStart)
            segEnd = segStart + TimeSpan.FromMilliseconds(50);

        return new Segment(speakerId, segStart, segEnd, words.ToList());
    }
}
