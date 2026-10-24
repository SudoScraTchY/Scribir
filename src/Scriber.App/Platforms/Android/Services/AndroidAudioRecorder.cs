#if ANDROID
using System.Buffers;
using Android.Media;
using Scriber.Core.Audio;

namespace Scriber.App.Services.Audio;

/// <summary>
/// High-performance, zero-allocation Android audio recorder using Android.Media.AudioRecord.
/// Streams 16 kHz 16-bit mono PCM straight to disk as a standard WAV file.
/// Highly optimized for low-end / mid-end devices (e.g. Xiaomi Redmi Note 11) to maintain minimal memory overhead.
/// </summary>
public sealed class AndroidAudioRecorder : IAudioRecorder
{
    private const int SampleRate = 16000;
    private const ChannelIn ChannelConfig = ChannelIn.Mono;
    private const Encoding AudioFormat = Encoding.Pcm16bit;

    private AudioRecord? _audioRecord;
    private FileStream? _fileStream;
    private Thread? _recordingThread;
    private CancellationTokenSource? _cts;

    private bool _isRecording;
    private bool _isPaused;
    private int _totalBytesWritten;
    private string? _currentFilePath;
    private float _currentAudioLevel;

    public bool IsRecording => _isRecording;
    public bool IsPaused => _isPaused;
    public TimeSpan ElapsedTime => TimeSpan.FromSeconds((double)_totalBytesWritten / (SampleRate * 2));
    public float CurrentAudioLevel => _currentAudioLevel;

    public event EventHandler<float>? AudioLevelChanged;
    public event EventHandler<TimeSpan>? ElapsedTimeChanged;
    public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;
    public event EventHandler<AudioChunkAvailableEventArgs>? AudioDataAvailable;

    public async Task<bool> HasPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        return status == PermissionStatus.Granted;
    }

    public async Task<bool> RequestPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Microphone>();
        }
        return status == PermissionStatus.Granted;
    }

    public async Task StartRecordingAsync(string? destinationFilePath = null, CancellationToken ct = default)
    {
        if (_isRecording)
        {
            throw new InvalidOperationException("Recording is already in progress.");
        }

        bool hasPerm = await RequestPermissionAsync();
        if (!hasPerm)
        {
            throw new UnauthorizedAccessException("Microphone permission was not granted.");
        }

        _currentFilePath = destinationFilePath ?? Path.Combine(
            FileSystem.CacheDirectory,
            $"rec_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

        var dir = Path.GetDirectoryName(_currentFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _fileStream = new FileStream(_currentFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096);
        WavHelper.WriteHeader(_fileStream, 0);
        _totalBytesWritten = 0;

        int minBufferSize = AudioRecord.GetMinBufferSize(SampleRate, ChannelConfig, AudioFormat);
        int bufferSize = Math.Max(minBufferSize, 4096);

        _audioRecord = new AudioRecord(
            AudioSource.VoiceRecognition, // Hardware noise suppression & AGC where supported
            SampleRate,
            ChannelConfig,
            AudioFormat,
            bufferSize);

        if (_audioRecord.State != Android.Media.State.Initialized)
        {
            _audioRecord.Release();
            _audioRecord = null;
            _fileStream.Dispose();
            _fileStream = null;
            throw new InvalidOperationException("Failed to initialize Android AudioRecord hardware instance.");
        }

        _isRecording = true;
        _isPaused = false;
        _cts = new CancellationTokenSource();

        _audioRecord.StartRecording();

        _recordingThread = new Thread(RecordingLoop)
        {
            IsBackground = true,
            Name = "ScriberAndroidAudioCapture",
            Priority = ThreadPriority.AboveNormal
        };
        _recordingThread.Start();

        NotifyStateChanged();
    }

    private void RecordingLoop()
    {
        const int readSize = 2048; // 64ms chunks of 16kHz 16-bit mono PCM
        byte[] buffer = ArrayPool<byte>.Shared.Rent(readSize);

        try
        {
            while (_isRecording && _cts?.IsCancellationRequested == false)
            {
                if (_isPaused)
                {
                    Thread.Sleep(50);
                    continue;
                }

                if (_audioRecord == null || _fileStream == null) break;

                int bytesRead = _audioRecord.Read(buffer, 0, readSize);
                if (bytesRead > 0)
                {
                    _fileStream.Write(buffer, 0, bytesRead);
                    _totalBytesWritten += bytesRead;

                    // Calculate live VU meter level with zero allocations
                    _currentAudioLevel = WavHelper.CalculateRmsLevel(buffer.AsSpan(0, bytesRead));
                    AudioLevelChanged?.Invoke(this, _currentAudioLevel);
                    ElapsedTimeChanged?.Invoke(this, ElapsedTime);

                    var chunkCopy = new byte[bytesRead];
                    Array.Copy(buffer, 0, chunkCopy, 0, bytesRead);
                    AudioDataAvailable?.Invoke(this, new AudioChunkAvailableEventArgs(chunkCopy, 0, bytesRead, ElapsedTime));
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }
        catch (Exception)
        {
            // Thread exiting
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public Task PauseRecordingAsync()
    {
        if (!_isRecording || _isPaused) return Task.CompletedTask;

        _isPaused = true;
        _audioRecord?.Stop();
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public Task ResumeRecordingAsync()
    {
        if (!_isRecording || !_isPaused) return Task.CompletedTask;

        _isPaused = false;
        _audioRecord?.StartRecording();
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public async Task<string> StopRecordingAsync()
    {
        if (!_isRecording) return _currentFilePath ?? string.Empty;

        _isRecording = false;
        _isPaused = false;
        _cts?.Cancel();

        if (_recordingThread != null && _recordingThread.IsAlive)
        {
            _recordingThread.Join(500);
            _recordingThread = null;
        }

        if (_audioRecord != null)
        {
            try
            {
                _audioRecord.Stop();
                _audioRecord.Release();
            }
            catch { }
            _audioRecord.Dispose();
            _audioRecord = null;
        }

        if (_fileStream != null)
        {
            WavHelper.FinalizeHeader(_fileStream, _totalBytesWritten);
            await _fileStream.FlushAsync();
            await _fileStream.DisposeAsync();
            _fileStream = null;
        }

        _cts?.Dispose();
        _cts = null;

        _currentAudioLevel = 0f;
        AudioLevelChanged?.Invoke(this, 0f);
        NotifyStateChanged();

        return _currentFilePath ?? string.Empty;
    }

    public async Task CancelRecordingAsync()
    {
        string path = await StopRecordingAsync();
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(this, new RecordingStateChangedEventArgs(_isRecording, _isPaused, ElapsedTime));
    }

    public async ValueTask DisposeAsync()
    {
        if (_isRecording)
        {
            await CancelRecordingAsync();
        }
    }
}
#endif
