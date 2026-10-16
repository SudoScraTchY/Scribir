namespace Scriber.Storage;

using Scriber.Core.Domain;

/// <summary>
/// Repository interface for storing, loading, and managing enrolled speaker voiceprints (ADR-0010).
/// </summary>
public interface IVoiceprintRepository
{
    /// <summary>Returns all enrolled voiceprints, ordered newest first.</summary>
    Task<IReadOnlyList<Voiceprint>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Gets a specific voiceprint by unique ID, or null if not found.</summary>
    Task<Voiceprint?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Saves or updates an enrolled voiceprint.</summary>
    Task SaveAsync(Voiceprint voiceprint, CancellationToken ct = default);

    /// <summary>Permanently deletes an enrolled voiceprint.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
