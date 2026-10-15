using System.Timers;

namespace Scriber.App.Services;

/// <summary>
/// Cross-platform playback clock and position synchronizer for transcript seeking.
/// </summary>
public sealed class PlaybackService : IPlaybackService, IDisposable
{
    private readonly System.Timers.Timer _timer;
    private TimeSpan _currentPosition = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.Zero;
    private bool _isPlaying;
    private DateTimeOffset _lastTick;

    public bool IsPlaying => _isPlaying;
    public TimeSpan CurrentPosition => _currentPosition;
    public TimeSpan Duration => _duration;

    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<bool>? PlayStateChanged;

    public PlaybackService()
    {
        _timer = new System.Timers.Timer(100); // 10 Hz position sync
        _timer.Elapsed += OnTimerElapsed;
    }

    public void Load(string audioFilePath, TimeSpan duration)
    {
        Stop();
        _duration = duration;
        _currentPosition = TimeSpan.Zero;
        PositionChanged?.Invoke(this, _currentPosition);
    }

    public void Play()
    {
        if (_isPlaying || _duration <= TimeSpan.Zero) return;

        if (_currentPosition >= _duration)
        {
            _currentPosition = TimeSpan.Zero;
        }

        _isPlaying = true;
        _lastTick = DateTimeOffset.UtcNow;
        _timer.Start();
        PlayStateChanged?.Invoke(this, true);
    }

    public void Pause()
    {
        if (!_isPlaying) return;

        _isPlaying = false;
        _timer.Stop();
        PlayStateChanged?.Invoke(this, false);
    }

    public void Seek(TimeSpan position)
    {
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (position > _duration) position = _duration;

        _currentPosition = position;
        _lastTick = DateTimeOffset.UtcNow;
        PositionChanged?.Invoke(this, _currentPosition);
    }

    public void Stop()
    {
        Pause();
        _currentPosition = TimeSpan.Zero;
        PositionChanged?.Invoke(this, _currentPosition);
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (!_isPlaying) return;

        var now = DateTimeOffset.UtcNow;
        var elapsed = now - _lastTick;
        _lastTick = now;

        _currentPosition += elapsed;
        if (_currentPosition >= _duration)
        {
            _currentPosition = _duration;
            Pause();
        }

        PositionChanged?.Invoke(this, _currentPosition);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
