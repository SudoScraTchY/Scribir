using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Scriber.Core.Audio;

namespace Scriber.Pipeline.Import;

/// <summary>
/// Decodes audio files to normalized 16 kHz mono float32 PCM by shelling out to ffmpeg.
/// ffmpeg is a local, offline dependency (bundled with the app on each platform; see ADR-0011 scope).
/// </summary>
public sealed class FfmpegAudioDecoder : IAudioDecoder
{
    /// <summary>The sample rate every downstream model expects.</summary>
    public const int TargetSampleRate = 16000;

    private const int TargetChannels = 1;

    private readonly string _ffmpegPath;

    /// <param name="ffmpegPath">
    /// Path to the ffmpeg executable. Defaults to the SCRIBER_FFMPEG environment variable, then "ffmpeg" on PATH.
    /// </param>
    public FfmpegAudioDecoder(string? ffmpegPath = null)
    {
        _ffmpegPath = ffmpegPath
            ?? Environment.GetEnvironmentVariable("SCRIBER_FFMPEG")
            ?? "ffmpeg";
    }

    public async Task<AudioBuffer> DecodeAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Audio file not found: {filePath}", filePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"-hide_banner -loglevel error -i {Quote(filePath)} -vn -f f32le -acodec pcm_f32le -ar {TargetSampleRate} -ac {TargetChannels} -",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new AudioDecodeException(
                $"ffmpeg executable not found ('{_ffmpegPath}'). Install ffmpeg or set the SCRIBER_FFMPEG environment variable.", ex);
        }

        try
        {
            var output = new MemoryStream();
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.StandardOutput.BaseStream.CopyToAsync(output, 81920, ct).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var detail = stderr.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase)
                    ? "the file contains no decodable audio stream"
                    : Clip(stderr);
                throw new AudioDecodeException(
                    $"ffmpeg failed decoding '{filePath}' (exit {process.ExitCode}): {detail}");
            }

            var bytes = output.ToArray();
            if (bytes.Length == 0)
            {
                throw new AudioDecodeException(
                    $"ffmpeg produced no audio for '{filePath}' — the file has no decodable audio stream.");
            }
            if (bytes.Length % sizeof(float) != 0)
            {
                throw new AudioDecodeException(
                    $"ffmpeg produced a truncated sample stream for '{filePath}' ({bytes.Length} bytes, not a whole number of float32 samples).");
            }

            var samples = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length); // f32le; all Scriber target platforms are little-endian
            return new AudioBuffer(samples, TargetSampleRate, TargetChannels);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static string Quote(string path) => "\"" + path.Replace("\"", "\\\"") + "\"";

    private static string Clip(string s)
    {
        var trimmed = s.Trim();
        return trimmed.Length > 1000 ? trimmed[..1000] + "…" : trimmed;
    }
}
