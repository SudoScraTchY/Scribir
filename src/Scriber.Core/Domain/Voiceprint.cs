namespace Scriber.Core.Domain;

/// <summary>
/// A registered speaker voiceprint profile used for cross-session speaker matching (ADR-0010).
/// </summary>
public sealed record Voiceprint
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; }
    public ReadOnlyMemory<float> Embedding { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public Voiceprint(Guid id, string displayName, ReadOnlyMemory<float> embedding, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName, nameof(displayName));

        if (embedding.Length == 0)
        {
            throw new ArgumentException("Embedding cannot be empty.", nameof(embedding));
        }

        Id = id;
        DisplayName = displayName.Trim();
        Embedding = embedding;
        CreatedAt = createdAt;
    }
}
