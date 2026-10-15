namespace Scriber.Inference.Asr;

/// <summary>
/// A discrete chunk of PCM audio for streaming transcription.
/// </summary>
/// <param name="Samples">Normalized float32 PCM samples.</param>
/// <param name="SampleRate">Sample rate in Hz (default 16000).</param>
/// <param name="Channels">Number of audio channels (default 1 = mono).</param>
public record AudioChunk(float[] Samples, int SampleRate = 16000, int Channels = 1);
