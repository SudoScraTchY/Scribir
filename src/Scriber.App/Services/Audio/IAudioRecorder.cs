namespace Scriber.App.Services.Audio;

public record RecordingStateChangedEventArgs(bool IsRecording, bool IsPaused, TimeSpan ElapsedTime);
public record AudioChunkAvailableEventArgs(byte[] Buffer, int Offset, int Count, TimeSpan Timestamp);

/// <summary>
/// Cross-platform microphone audio recorder capturing standard 16 kHz 16-bit mono PCM.
/// Designed for zero heap churn and low memory overhead to maximize headroom for on-device AI.
/// </summary>
public interface IAudioRecorder : IAsyncDisposable
{
    bool IsRecording { get; }
    bool IsPaused { get; }
    TimeSpan ElapsedTime { get; }
    float CurrentAudioLevel { get; }

    event EventHandler<float>? AudioLevelChanged;
    event EventHandler<TimeSpan>? ElapsedTimeChanged;
    event EventHandler<RecordingStateChangedEventArgs>? StateChanged;
    event EventHandler<AudioChunkAvailableEventArgs>? AudioDataAvailable;

    Task<bool> HasPermissionAsync();
    Task<bool> RequestPermissionAsync();

    Task StartRecordingAsync(string? destinationFilePath = null, CancellationToken ct = default);
    Task PauseRecordingAsync();
    Task ResumeRecordingAsync();
    Task<string> StopRecordingAsync();
    Task CancelRecordingAsync();
}
