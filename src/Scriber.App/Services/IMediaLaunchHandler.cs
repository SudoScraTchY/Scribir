namespace Scriber.App.Services;

/// <summary>
/// Handles external media launches, share-sheet intents, and custom protocol activations (PH2.5-PLAT-001).
/// </summary>
public interface IMediaLaunchHandler
{
    /// <summary>
    /// Checks whether the incoming URI can be handled as media or protocol action.
    /// </summary>
    Task<bool> CanHandleAsync(Uri uri);

    /// <summary>
    /// Resolves external stream / path, stages to local cache if necessary, and navigates to the transcription workflow.
    /// </summary>
    Task HandleIncomingMediaAsync(Uri uri, CancellationToken ct = default);
}
