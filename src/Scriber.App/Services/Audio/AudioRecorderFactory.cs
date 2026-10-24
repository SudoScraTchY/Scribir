namespace Scriber.App.Services.Audio;

public static class AudioRecorderFactory
{
    public static IAudioRecorder Create()
    {
#if ANDROID
        return new AndroidAudioRecorder();
#elif WINDOWS
        return new WindowsAudioRecorder();
#else
        return new SimulatedAudioRecorder();
#endif
    }
}
