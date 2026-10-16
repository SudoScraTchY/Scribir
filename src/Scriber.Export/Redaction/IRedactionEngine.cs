namespace Scriber.Export.Redaction;

using Scriber.Core.Domain;

public interface IRedactionEngine
{
    /// <summary>
    /// Scans raw text for PII candidates according to options.
    /// </summary>
    IReadOnlyList<RedactionCandidate> ScanText(string text, RedactionOptions? options = null);

    /// <summary>
    /// Scans a full transcript, returning candidate redactions across all segments.
    /// </summary>
    IReadOnlyList<RedactionCandidate> ScanTranscript(Transcript transcript, RedactionOptions? options = null);

    /// <summary>
    /// Applies accepted redactions to a string.
    /// </summary>
    string Apply(string text, IEnumerable<RedactionCandidate> candidates);

    /// <summary>
    /// Produces a redacted copy of a transcript with accepted redactions applied.
    /// Original transcript in database is unchanged.
    /// </summary>
    Transcript RedactTranscript(Transcript transcript, IEnumerable<RedactionCandidate> candidates);
}
