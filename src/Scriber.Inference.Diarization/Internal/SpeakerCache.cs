namespace Scriber.Inference.Diarization.Internal;

/// <summary>
/// C# port of Nemotron3DiarizationSpeakerCache (AOSC + FIFO), using float32
/// buffers that mirror in-place reference semantics.
/// </summary>
internal sealed class SpeakerCache
{
    public const int Hidden = 512;

    private readonly int _speakerCacheLength;
    private readonly int _fifoLength;
    private readonly int _updatePeriod;
    private readonly int _numSilenceFrames;
    private readonly float _scoreThreshold;
    private readonly float _latestBoost;
    private readonly int _numSpeakers;
    private readonly int _subsampling;
    private readonly int _minPositiveScores;
    private readonly int _numStrongBoosted;
    private readonly int _numWeakBoosted;

    private static readonly float BoostStrong = (float)(-2.0 * Math.Log(0.5));
    private static readonly float BoostWeak = (float)(-Math.Log(0.5));

    private readonly float[] _embeds; // [len * 512]
    private readonly float[] _probs;  // [len * S]
    private readonly float[] _fifo;   // [fifoLen * 512]

    public int NumCacheFrames { get; private set; }
    public int NumFifoFrames { get; private set; }
    public bool IsCompressed { get; private set; }
    public int StateLength => NumCacheFrames + NumFifoFrames;

    public SpeakerCache(int speakerCacheLength = 264, int fifoLength = 264, int updatePeriod = 222,
                        int numSilenceFrames = 1, float scoreThreshold = 0.25f, float latestBoost = 0.05f,
                        int numSpeakers = 8, int subsampling = 8)
    {
        _speakerCacheLength = speakerCacheLength;
        _fifoLength = fifoLength;
        _updatePeriod = updatePeriod;
        _numSilenceFrames = numSilenceFrames;
        _scoreThreshold = scoreThreshold;
        _latestBoost = latestBoost;
        _numSpeakers = numSpeakers;
        _subsampling = subsampling;

        int budget = speakerCacheLength / numSpeakers - numSilenceFrames;
        _minPositiveScores = (int)Math.Floor(budget * 0.5);
        _numStrongBoosted = (int)Math.Floor(budget * 0.75);
        _numWeakBoosted = (int)Math.Floor(budget * 1.5);

        _embeds = new float[speakerCacheLength * Hidden];
        _probs = new float[speakerCacheLength * numSpeakers];
        _fifo = new float[fifoLength * Hidden];
    }

    /// <summary>Concatenated cache + FIFO frames (encoder-input embeddings), [StateLength x 512].</summary>
    public float[] GetEmbeds()
    {
        var outv = new float[StateLength * Hidden];
        Array.Copy(_embeds, 0, outv, 0, NumCacheFrames * Hidden);
        Array.Copy(_fifo, 0, outv, NumCacheFrames * Hidden, NumFifoFrames * Hidden);
        return outv;
    }

    /// <param name="chunkInputEmbeds">[numInputFrames x 512] = cached frames + chunk (with look-ahead).</param>
    /// <param name="chunkLogits">[numLogitFrames x S] at 10-ms frame rate (pooled by 8 inside).</param>
    /// <param name="numChunkFrames">chunk frames following cached frames (look-ahead excluded).</param>
    public void Update(float[] chunkInputEmbeds, int numInputFrames, float[] chunkLogits, int numLogitFrames,
                       float[] silenceEmbeds, int numChunkFrames)
    {
        int S = _numSpeakers;

        var probs = new float[numInputFrames * S];
        for (int f = 0; f < numInputFrames; f++)
        {
            int baseIn = f * _subsampling * S;
            int baseOut = f * S;
            for (int s = 0; s < S; s++)
            {
                double acc = 0.0;
                for (int k2 = 0; k2 < _subsampling; k2++)
                {
                    float v = chunkLogits[baseIn + k2 * S + s];
                    acc += 1.0 / (1.0 + Math.Exp(-v));
                }
                probs[baseOut + s] = (float)(acc / _subsampling);
            }
        }

        int ncf = NumCacheFrames, nff = NumFifoFrames;
        int chunkStart = ncf + nff;
        int fifoLenNew = nff + numChunkFrames;

        var fifoEmbeds = new float[fifoLenNew * Hidden];
        Array.Copy(_fifo, 0, fifoEmbeds, 0, nff * Hidden);
        Array.Copy(chunkInputEmbeds, chunkStart * Hidden, fifoEmbeds, nff * Hidden, numChunkFrames * Hidden);

        int numPopped = NumPoppedFrames(fifoLenNew);
        if (numPopped > 0)
        {
            int cacheLenNew = ncf + numPopped;
            var cacheEmbeds = new float[cacheLenNew * Hidden];
            var cacheProbs = new float[cacheLenNew * S];
            Array.Copy(_embeds, 0, cacheEmbeds, 0, ncf * Hidden);
            Array.Copy(fifoEmbeds, 0, cacheEmbeds, ncf * Hidden, numPopped * Hidden);
            if (IsCompressed) Array.Copy(_probs, 0, cacheProbs, 0, ncf * S);
            else Array.Copy(probs, 0, cacheProbs, 0, ncf * S);
            Array.Copy(probs, ncf * S, cacheProbs, ncf * S, numPopped * S);

            int fifoRem = fifoLenNew - numPopped;
            var fifoRest = new float[fifoRem * Hidden];
            Array.Copy(fifoEmbeds, numPopped * Hidden, fifoRest, 0, fifoRem * Hidden);

            if (cacheLenNew > _speakerCacheLength)
            {
                (cacheEmbeds, cacheProbs) = Compress(cacheEmbeds, cacheProbs, cacheLenNew, silenceEmbeds);
                IsCompressed = true;
                cacheLenNew = _speakerCacheLength;
            }

            NumCacheFrames = cacheLenNew;
            Array.Copy(cacheEmbeds, 0, _embeds, 0, cacheLenNew * Hidden);
            Array.Copy(cacheProbs, 0, _probs, 0, cacheLenNew * S);
            NumFifoFrames = fifoRem;
            Array.Copy(fifoRest, 0, _fifo, 0, fifoRem * Hidden);
        }
        else
        {
            NumFifoFrames = fifoLenNew;
            Array.Copy(fifoEmbeds, 0, _fifo, 0, fifoLenNew * Hidden);
        }

        if (numLogitFrames != (numInputFrames * _subsampling))
            throw new InvalidOperationException("logit frame count does not match input frames * subsampling");
    }

    private int NumPoppedFrames(int numFifoFrames)
    {
        if (numFifoFrames <= _fifoLength) return 0;
        return Math.Min(Math.Max(_updatePeriod, numFifoFrames - _fifoLength), numFifoFrames);
    }

    private (float[] embeds, float[] probs) Compress(float[] embeds, float[] probs, int numFrames, float[] silenceEmbeds)
    {
        int S = _numSpeakers;
        var scores = FrameScores(probs, numFrames);

        for (int f = _speakerCacheLength; f < numFrames; f++)
            for (int s = 0; s < S; s++)
                scores[f * S + s] += _latestBoost;

        BoostScores(scores, numFrames, _numStrongBoosted, BoostStrong);
        BoostScores(scores, numFrames, _numWeakBoosted, BoostWeak);

        int scored = numFrames + _numSilenceFrames;
        var flat = new float[S * scored];
        for (int f = 0; f < numFrames; f++)
            for (int s = 0; s < S; s++)
                flat[s * scored + f] = scores[f * S + s];
        for (int s = 0; s < S; s++)
            flat[s * scored + numFrames] = float.PositiveInfinity;

        int k = _speakerCacheLength;
        int sentinel = scored * S;
        var idx = TopKIndices(flat, k);
        for (int i = 0; i < k; i++)
            if (float.IsNegativeInfinity(flat[idx[i]]))
                idx[i] = sentinel;
        Array.Sort(idx);

        var frameIdx = new int[k];
        for (int i = 0; i < k; i++)
            frameIdx[i] = idx[i] == sentinel ? numFrames : Math.Min(idx[i] % scored, numFrames);

        var outEmbeds = new float[k * Hidden];
        var outProbs = new float[k * S];
        for (int i = 0; i < k; i++)
        {
            int fi = frameIdx[i];
            if (fi == numFrames)
            {
                Array.Copy(silenceEmbeds, 0, outEmbeds, i * Hidden, Hidden);
                for (int s = 0; s < S; s++) outProbs[i * S + s] = 0f;
            }
            else
            {
                Array.Copy(embeds, fi * Hidden, outEmbeds, i * Hidden, Hidden);
                Array.Copy(probs, fi * S, outProbs, i * S, S);
            }
        }
        return (outEmbeds, outProbs);
    }

    private float[] FrameScores(float[] probs, int numFrames)
    {
        int S = _numSpeakers;
        var scores = new float[numFrames * S];
        double logHalf = Math.Log(0.5);
        Span<float> lp = stackalloc float[8];
        Span<float> lc = stackalloc float[8];

        for (int f = 0; f < numFrames; f++)
        {
            int baseRow = f * S;
            double logComplSum = 0.0;
            for (int s = 0; s < S; s++)
            {
                float p = probs[baseRow + s];
                float pc = MathF.Max(p, _scoreThreshold);
                float ncc = MathF.Max(1.0f - p, _scoreThreshold);
                lp[s] = MathF.Log(pc);
                lc[s] = MathF.Log(ncc);
                logComplSum += lc[s];
            }
            for (int s = 0; s < S; s++)
            {
                float p = probs[baseRow + s];
                double v = lp[s] - lc[s] + logComplSum - logHalf;
                bool isSpeech = p > 0.5f;
                float sc = isSpeech ? (float)v : float.NegativeInfinity;
                scores[baseRow + s] = sc;
            }

            int positiveCount = 0;
            for (int s = 0; s < S; s++)
                if (scores[baseRow + s] > 0f) positiveCount++;
            if (positiveCount >= _minPositiveScores)
            {
                for (int s = 0; s < S; s++)
                    if (scores[baseRow + s] <= 0f && probs[baseRow + s] > 0.5f)
                        scores[baseRow + s] = float.NegativeInfinity;
            }
        }
        return scores;
    }

    private void BoostScores(float[] scores, int numFrames, int numBoosted, float boost)
    {
        int S = _numSpeakers;
        if (numBoosted <= 0 || numFrames <= 0) return;
        int n = Math.Min(numBoosted, numFrames);
        var order = new int[numFrames];
        for (int s = 0; s < S; s++)
        {
            for (int f = 0; f < numFrames; f++) order[f] = f;
            Array.Sort(order, (a, b) =>
            {
                float va = scores[a * S + s], vb = scores[b * S + s];
                int c = vb.CompareTo(va);
                return c != 0 ? c : a.CompareTo(b);
            });
            for (int i = 0; i < n; i++) scores[order[i] * S + s] += boost;
        }
    }

    private static int[] TopKIndices(float[] flat, int k)
    {
        var order = new int[flat.Length];
        for (int i = 0; i < flat.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            int c = flat[b].CompareTo(flat[a]);
            return c != 0 ? c : a.CompareTo(b);
        });
        var result = new int[k];
        Array.Copy(order, result, k);
        return result;
    }
}
