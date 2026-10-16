using Scriber.Core.Domain;

namespace Scriber.Storage;

/// <summary>
/// Persistence repository for transcripts, speakers, segments, words, and FTS5 search.
/// Implements PH1-STORE-001.
/// </summary>
public interface ITranscriptRepository
{
    Task<Transcript?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TranscriptSummary>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(Transcript transcript, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Archives the current state of a transcript into version history before re-transcribing or major edits (PH2-INT-005).
    /// </summary>
    Task<Guid> ArchiveVersionAsync(Guid transcriptId, string note = "", CancellationToken ct = default);

    /// <summary>
    /// Gets all archived historical versions for a transcript.
    /// </summary>
    Task<IReadOnlyList<TranscriptVersionInfo>> GetVersionsAsync(Guid transcriptId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific historical transcript version.
    /// </summary>
    Task<Transcript?> GetVersionAsync(Guid versionId, CancellationToken ct = default);
}

public record TranscriptVersionInfo(
    Guid Id,
    Guid TranscriptId,
    int VersionNumber,
    DateTimeOffset CreatedAt,
    string Note);
