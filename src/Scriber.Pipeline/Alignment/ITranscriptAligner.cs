using Scriber.Core.Domain;
using Scriber.Inference.Asr;
using Scriber.Inference.Diarization;

namespace Scriber.Pipeline.Alignment;

/// <summary>
/// Merges diarization speaker turns and ASR recognized words into an ordered, speaker-attributed transcript.
/// Implements PH1-CORE-006.
/// </summary>
public interface ITranscriptAligner
{
    /// <summary>
    /// Attributes a sequence of recognized words to speaker turns and merges them into chronological segments.
    /// </summary>
    IReadOnlyList<Segment> Align(IReadOnlyList<SpeakerTurn> turns, IReadOnlyList<Word> words);

    /// <summary>
    /// Attributes words from ASR transcript segments to speaker turns.
    /// </summary>
    IReadOnlyList<Segment> Align(IReadOnlyList<SpeakerTurn> turns, IReadOnlyList<TranscriptSegment> asrSegments);
}
