namespace Scriber.Inference.Asr;

/// <summary>
/// Configuration for the llama.cpp / whisper.cpp ASR backend on Android and Desktop.
/// </summary>
public sealed record LlamaCppConfig
{
    /// <summary>Path to the GGUF model file (e.g. ggml-small-q5_1.bin or whisper-base.gguf).</summary>
    public required string ModelPath { get; init; }

    /// <summary>Backend identifier.</summary>
    public string Id { get; init; } = "llama-cpp-whisper-base-gguf";

    /// <summary>Display name for UI selector.</summary>
    public string DisplayName { get; init; } = "Whisper Base (GGUF) via llama.cpp / whisper.cpp";

    /// <summary>Optional path to whisper-cli / llama-cli binary if available on Desktop or Android.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Number of CPU threads to allocate for inference.</summary>
    public int NumThreads { get; init; } = Math.Max(2, Environment.ProcessorCount / 2);

    /// <summary>Context window length in tokens.</summary>
    public int ContextSize { get; init; } = 4096;

    /// <summary>Inference acceleration provider (e.g. "cpu", "vulkan", "opencl", "nnapi").</summary>
    public string Provider { get; init; } = "cpu";
}
