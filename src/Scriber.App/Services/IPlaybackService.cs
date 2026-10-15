namespace Scriber.App.Services;

public interface IPlaybackService
{
    bool IsPlaying { get; }
    TimeSpan CurrentPosition { get; }
    TimeSpan Duration { get; }

    event EventHandler<TimeSpan>? PositionChanged;
    event EventHandler<bool>? PlayStateChanged;

    void Load(string audioFilePath, TimeSpan duration);
    void Play();
    void Pause();
    void Seek(TimeSpan position);
    void Stop();
}
