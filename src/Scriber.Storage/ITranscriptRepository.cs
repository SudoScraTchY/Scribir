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
}
