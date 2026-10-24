using Scriber.Core.Audio;

namespace Scriber.App.Services.Audio;

/// <summary>
/// Fallback audio recorder for environments where hardware microphones are unavailable or in testing scenarios.
/// Generates a valid 16 kHz 16-bit mono WAV file with realistic timing.
/// </summary>
public sealed class SimulatedAudioRecorder : IAudioRecorder
{
    private System.Timers.Timer? _timer;
    private FileStream? _fileStream;
    private string? _filePath;
    private bool _isRecording;
    private bool _isPaused;
    private int _totalBytes;
    private float _currentLevel;

    public bool IsRecording => _isRecording;
    public bool IsPaused => _isPaused;
    public TimeSpan ElapsedTime => TimeSpan.FromSeconds((double)_totalBytes / 32000.0);
    public float CurrentAudioLevel => _currentLevel;

    public event EventHandler<float>? AudioLevelChanged;
    public event EventHandler<TimeSpan>? ElapsedTimeChanged;
    public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;
    public event EventHandler<AudioChunkAvailableEventArgs>? AudioDataAvailable;

    public Task<bool> HasPermissionAsync() => Task.FromResult(true);
    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);

    public Task StartRecordingAsync(string? destinationFilePath = null, CancellationToken ct = default)
    {
        _filePath = destinationFilePath ?? Path.Combine(FileSystem.CacheDirectory, $"sim_rec_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _fileStream = new FileStream(_filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        WavHelper.WriteHeader(_fileStream, 0);
        _totalBytes = 0;

        _isRecording = true;
        _isPaused = false;

        _timer = new System.Timers.Timer(100);
        var rand = new Random();
        _timer.Elapsed += (_, _) =>
        {
            if (!_isRecording || _isPaused || _fileStream == null) return;

            // Generate 100ms of simulated speech tone (1600 samples * 2 = 3200 bytes)
            byte[] chunk = new byte[3200];
            for (int i = 0; i < 1600; i++)
            {
                short sample = (short)(Math.Sin(2 * Math.PI * 300 * i / 16000) * 12000 * rand.NextDouble());
                BitConverter.GetBytes(sample).CopyTo(chunk, i * 2);
            }

            _fileStream.Write(chunk, 0, chunk.Length);
            _totalBytes += chunk.Length;

            _currentLevel = WavHelper.CalculateRmsLevel(chunk);
            AudioLevelChanged?.Invoke(this, _currentLevel);
            ElapsedTimeChanged?.Invoke(this, ElapsedTime);
            AudioDataAvailable?.Invoke(this, new AudioChunkAvailableEventArgs(chunk, 0, chunk.Length, ElapsedTime));
        };
        _timer.Start();

        StateChanged?.Invoke(this, new RecordingStateChangedEventArgs(true, false, TimeSpan.Zero));
        return Task.CompletedTask;
    }

    public Task PauseRecordingAsync()
    {
        _isPaused = true;
        StateChanged?.Invoke(this, new RecordingStateChangedEventArgs(_isRecording, _isPaused, ElapsedTime));
        return Task.CompletedTask;
    }

    public Task ResumeRecordingAsync()
    {
        _isPaused = false;
        StateChanged?.Invoke(this, new RecordingStateChangedEventArgs(_isRecording, _isPaused, ElapsedTime));
        return Task.CompletedTask;
    }

    public async Task<string> StopRecordingAsync()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;

        _isRecording = false;
        _isPaused = false;

        if (_fileStream != null)
        {
            WavHelper.FinalizeHeader(_fileStream, _totalBytes);
            await _fileStream.FlushAsync();
            await _fileStream.DisposeAsync();
            _fileStream = null;
        }

        _currentLevel = 0f;
        AudioLevelChanged?.Invoke(this, 0f);
        StateChanged?.Invoke(this, new RecordingStateChangedEventArgs(false, false, ElapsedTime));

        return _filePath ?? string.Empty;
    }

    public async Task CancelRecordingAsync()
    {
        string p = await StopRecordingAsync();
        if (!string.IsNullOrEmpty(p) && File.Exists(p))
        {
            try { File.Delete(p); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isRecording)
        {
            await CancelRecordingAsync();
        }
    }
}
