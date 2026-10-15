namespace Scriber.Inference.Diarization;

/// <summary>
/// Configuration specifying model file paths for NemotronDiarizer.
/// </summary>
public sealed record NemotronDiarizerConfig
{
    /// <summary>
    /// Path to the exported ONNX model graph (e.g. nemotron3_stream_step.onnx).
    /// </summary>
    public required string ModelPath { get; init; }

    /// <summary>
    /// Directory containing feature weights: window_hann_400.bin, mel_filters_128x257.bin,
    /// embedder_projection_weight_512x1024.bin, and silence_embeds_512.bin.
    /// </summary>
    public required string AssetsDir { get; init; }
}
