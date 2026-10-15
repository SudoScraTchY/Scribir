using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Scriber.Core.Audio;
using Scriber.Inference.Diarization.Internal;

namespace Scriber.Inference.Diarization;

/// <summary>
/// On-device speaker diarization engine powered by NVIDIA Nemotron-3-Diarization
/// running over Microsoft.ML.OnnxRuntime.
/// </summary>
public sealed class NemotronDiarizer : IDiarizer, IDisposable
{
    public const int SampleRate = 16000;
    public const int LowLatencyRight = 4;
    public const int OfflineRight = 40;
    public const int FirstChunkSamples = 16680;
    public const int PerChunkSamples = 17040;
    public const int MelFramesPerStep = 72;
    public const int Hidden = 512;

    private readonly MelFrontend _mel;
    private readonly Embedder _embedder;
    private readonly InferenceSession _session;
    private readonly float[] _silence;
    private bool _disposed;

    public NemotronDiarizer(string modelPath, string assetsDir, SessionOptions? sessionOptions = null)
        : this(new NemotronDiarizerConfig { ModelPath = modelPath, AssetsDir = assetsDir }, sessionOptions)
    {
    }

    public NemotronDiarizer(NemotronDiarizerConfig config, SessionOptions? sessionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.ModelPath, nameof(config.ModelPath));
        ArgumentException.ThrowIfNullOrWhiteSpace(config.AssetsDir, nameof(config.AssetsDir));

        if (!File.Exists(config.ModelPath))
            throw new FileNotFoundException($"Nemotron diarizer ONNX model not found: {config.ModelPath}", config.ModelPath);
        if (!Directory.Exists(config.AssetsDir))
            throw new DirectoryNotFoundException($"Diarizer assets directory not found: {config.AssetsDir}");

        _mel = new MelFrontend(config.AssetsDir);
        _embedder = new Embedder(config.AssetsDir);
        _silence = BinIO.ReadFloats(Path.Combine(config.AssetsDir, "silence_embeds_512.bin"));

        _session = sessionOptions != null
            ? new InferenceSession(config.ModelPath, sessionOptions)
            : new InferenceSession(config.ModelPath);
    }

    public async Task<IReadOnlyList<SpeakerTurn>> DiarizeAsync(AudioBuffer audio, DiarizationOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(options);

        if (audio.SampleRate != SampleRate)
            throw new ArgumentException($"Diarizer requires {SampleRate} Hz PCM, got {audio.SampleRate}.", nameof(audio));
        if (audio.Channels != 1)
            throw new ArgumentException($"Diarizer requires mono PCM, got {audio.Channels} channels.", nameof(audio));

        return await Task.Run(() => DiarizeCore(audio.Samples, options, ct), ct).ConfigureAwait(false);
    }

    private IReadOnlyList<SpeakerTurn> DiarizeCore(float[] audio, DiarizationOptions options, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (audio.Length == 0)
            return Array.Empty<SpeakerTurn>();

        ct.ThrowIfCancellationRequested();

        int n = audio.Length;
        var chunks = EnumerateChunks(n, options.Mode);
        var cache = CreateSpeakerCache(options.Mode, options.NumSpeakers);
        var pieces = new List<float[]>();
        int right = options.Mode == DiarizationMode.Offline ? OfflineRight : LowLatencyRight;

        foreach (var ch in chunks)
        {
            ct.ThrowIfCancellationRequested();

            var slice = new float[ch.Len];
            Array.Copy(audio, ch.Start, slice, 0, ch.Len);

            var mel = _mel.Compute(slice, ch.First);
            int tm = mel.GetLength(0);
            if (tm <= 0)
                continue;

            var emb = _embedder.Forward(mel, out int e);
            var cached = cache.GetEmbeds();
            int l = cache.StateLength;

            var full = RunGraph(mel, tm, cached, l);

            int numChunkFrames = ch.Last ? e : Math.Max(0, e - right);
            int fromFrame = l * 8;
            int count = Math.Min(numChunkFrames * 8, tm);

            if (count > 0)
            {
                var frames = new float[count * 8];
                Array.Copy(full, fromFrame * 8, frames, 0, count * 8);
                pieces.Add(frames);
            }

            var chunkInput = new float[(l + e) * Hidden];
            Array.Copy(cached, 0, chunkInput, 0, l * Hidden);
            Array.Copy(emb, 0, chunkInput, l * Hidden, e * Hidden);
            cache.Update(chunkInput, l + e, full, (l + e) * 8, _silence, numChunkFrames);
        }

        int totalFrames = pieces.Sum(p => p.Length / 8);
        if (totalFrames == 0)
            return Array.Empty<SpeakerTurn>();

        var cat = new float[totalFrames * 8];
        int off = 0;
        foreach (var p in pieces)
        {
            Array.Copy(p, 0, cat, off, p.Length);
            off += p.Length;
        }

        var rawSegments = ExtractSegments(cat, totalFrames, options.NumSpeakers, options.Threshold);

        // Map raw model speaker indices into stable appearance-ordered IDs
        var turns = new List<SpeakerTurn>(rawSegments.Count);
        var speakerMap = new Dictionary<int, string>();
        int nextSpeakerIndex = 0;

        foreach (var seg in rawSegments)
        {
            if (!speakerMap.TryGetValue(seg.Speaker, out var speakerId))
            {
                speakerId = $"{options.SpeakerIdPrefix}{nextSpeakerIndex++}";
                speakerMap[seg.Speaker] = speakerId;
            }

            turns.Add(new SpeakerTurn(
                speakerId,
                TimeSpan.FromSeconds(seg.Start),
                TimeSpan.FromSeconds(seg.End)
            ));
        }

        return turns;
    }

    private static SpeakerCache CreateSpeakerCache(DiarizationMode mode, int numSpeakers)
    {
        if (mode == DiarizationMode.Offline)
        {
            return new SpeakerCache(
                speakerCacheLength: 264,
                fifoLength: 40,
                updatePeriod: 300,
                numSilenceFrames: 1,
                scoreThreshold: 0.25f,
                latestBoost: 0.05f,
                numSpeakers: numSpeakers
            );
        }

        return new SpeakerCache(
            speakerCacheLength: 264,
            fifoLength: 264,
            updatePeriod: 222,
            numSilenceFrames: 1,
            scoreThreshold: 0.25f,
            latestBoost: 0.05f,
            numSpeakers: numSpeakers
        );
    }

    private static List<(int Start, int Len, bool First, bool Last)> EnumerateChunks(int total, DiarizationMode mode)
    {
        var chunks = new List<(int Start, int Len, bool First, bool Last)>();

        if (mode == DiarizationMode.Offline)
        {
            // Offline geometry: 340 encoder frames (2720 mel frames) + 40 right look-ahead
            const int offlineChunkSamples = 340 * 8 * 160; // 435,200 samples (~27.2s)
            int cursor = 0;
            bool first = true;
            while (cursor < total)
            {
                int len = Math.Min(offlineChunkSamples, total - cursor);
                bool last = cursor + len >= total;
                chunks.Add((cursor, len, first, last));
                cursor += len;
                first = false;
            }
            return chunks;
        }

        // Streaming low-latency chunking
        chunks.Add((0, Math.Min(FirstChunkSamples, total), true, total <= FirstChunkSamples));
        int melCursor = MelFramesPerStep;
        int s = melCursor * 160 - 256;
        while (s + PerChunkSamples <= total)
        {
            chunks.Add((s, PerChunkSamples, false, false));
            melCursor += MelFramesPerStep;
            s = melCursor * 160 - 256;
        }
        if (s < total)
        {
            chunks.Add((s, total - s, false, true));
        }
        return chunks;
    }

    private float[] RunGraph(float[,] mel, int tm, float[] cached, int l)
    {
        var feats = new float[tm * 128];
        for (int i = 0; i < tm; i++)
        {
            for (int m = 0; m < 128; m++)
            {
                feats[i * 128 + m] = mel[i, m];
            }
        }

        var tFeats = new DenseTensor<float>(feats, new[] { 1, tm, 128 });
        var tCached = new DenseTensor<float>(cached, new[] { 1, l, 512 });
        var inputs = new List<NamedOnnxValue>(2)
        {
            NamedOnnxValue.CreateFromTensor("input_features", tFeats),
            NamedOnnxValue.CreateFromTensor("cached_embeds", tCached),
        };

        using var outputs = _session.Run(inputs);
        var outT = outputs.First(o => o.Name == "chunk_logits").AsTensor<float>();
        return outT.ToArray();
    }

    private static List<RawSegment> ExtractSegments(float[] logitsFlat, int frames, int numSpeakers, float threshold)
    {
        var segs = new List<RawSegment>();
        for (int s = 0; s < numSpeakers; s++)
        {
            bool inRun = false;
            int runStart = 0;
            for (int t = 0; t < frames; t++)
            {
                float v = logitsFlat[t * numSpeakers + s];
                bool active = 1.0 / (1.0 + Math.Exp(-v)) > threshold;
                if (active && !inRun)
                {
                    inRun = true;
                    runStart = t;
                }
                else if (!active && inRun)
                {
                    inRun = false;
                    segs.Add(new RawSegment(Math.Round(runStart * 0.01, 2), Math.Round(t * 0.01, 2), s));
                }
            }
            if (inRun)
            {
                segs.Add(new RawSegment(Math.Round(runStart * 0.01, 2), Math.Round(frames * 0.01, 2), s));
            }
        }

        segs.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Speaker.CompareTo(b.Speaker));
        return segs;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _session.Dispose();
        _disposed = true;
    }
}
