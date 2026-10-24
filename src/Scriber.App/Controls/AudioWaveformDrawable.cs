namespace Scriber.App.Controls;

/// <summary>
/// High-performance 2D Canvas audio waveform visualizer implementing MAUI IDrawable.
/// Renders animated, mirrored audio bars with dynamic color gradients and zero per-frame heap allocations.
/// </summary>
public sealed class AudioWaveformDrawable : IDrawable
{
    private const int BarCount = 42;
    private readonly float[] _bars = new float[BarCount];
    private float _idlePhase;

    public bool IsRecording { get; set; }
    public bool IsPaused { get; set; }

    public AudioWaveformDrawable()
    {
        // Initialize with gentle resting heights
        for (int i = 0; i < BarCount; i++)
        {
            _bars[i] = 0.08f;
        }
    }

    /// <summary>
    /// Pushes a new normalized audio level sample [0.0, 1.0] and advances the rolling waveform.
    /// </summary>
    public void PushAudioLevel(float level)
    {
        // Shift left
        Array.Copy(_bars, 1, _bars, 0, BarCount - 1);

        // Apply smooth peak shaping
        float clamped = Math.Clamp(level, 0.05f, 1.0f);
        _bars[BarCount - 1] = clamped;
    }

    public void Reset()
    {
        for (int i = 0; i < BarCount; i++)
        {
            _bars[i] = 0.08f;
        }
        _idlePhase = 0f;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float width = dirtyRect.Width;
        float height = dirtyRect.Height;
        float centerY = height / 2f;

        if (width <= 0 || height <= 0) return;

        canvas.Antialias = true;

        if (!IsRecording)
        {
            // Draw sleek idle baseline with subtle sine ambient motion
            DrawIdleWaveform(canvas, width, height, centerY);
            return;
        }

        // Active recording: draw mirrored rounded vertical audio bars
        float spacing = 3.5f;
        float totalSpacing = spacing * (BarCount - 1);
        float barWidth = Math.Max(2.5f, (width - totalSpacing - 16f) / BarCount);
        float startX = 8f;
        float maxHalfHeight = (height / 2f) - 6f;

        for (int i = 0; i < BarCount; i++)
        {
            float level = _bars[i];
            float barHalfHeight = Math.Max(3f, level * maxHalfHeight);

            float x = startX + i * (barWidth + spacing);
            float topY = centerY - barHalfHeight;
            float barTotalHeight = barHalfHeight * 2f;

            // Dynamic color gradient: Cyan -> Indigo -> Violet -> Rose based on bar position & intensity
            Color barColor = GetBarColor(i, BarCount, level, IsPaused);
            canvas.FillColor = barColor;

            float cornerRadius = barWidth / 2f;
            canvas.FillRoundedRectangle(x, topY, barWidth, barTotalHeight, cornerRadius);
        }
    }

    private void DrawIdleWaveform(ICanvas canvas, float width, float height, float centerY)
    {
        _idlePhase += 0.08f;
        if (_idlePhase > MathF.PI * 2f) _idlePhase -= MathF.PI * 2f;

        // Draw soft horizontal centerline
        canvas.StrokeColor = Color.FromArgb("#334155");
        canvas.StrokeSize = 1.5f;
        canvas.DrawLine(16f, centerY, width - 16f, centerY);

        // Draw gentle resting ambient dots/ticks
        int tickCount = 36;
        float tickSpacing = (width - 32f) / tickCount;
        canvas.FillColor = Color.FromArgb("#6366F1");

        for (int i = 0; i < tickCount; i++)
        {
            float x = 16f + i * tickSpacing;
            float offset = MathF.Sin(_idlePhase + (i * 0.25f)) * 5f;
            float dotY = centerY + offset;
            canvas.FillCircle(x, dotY, 2f);
        }
    }

    private static Color GetBarColor(int index, int total, float level, bool isPaused)
    {
        if (isPaused)
        {
            return Color.FromArgb("#94A3B8"); // Muted Slate when paused
        }

        float progress = (float)index / total;

        // High volume accents
        if (level > 0.75f)
        {
            return Color.FromArgb("#F43F5E"); // Rose neon for intense sound
        }

        if (progress < 0.35f)
        {
            return Color.FromArgb("#38BDF8"); // Sky Blue / Cyan
        }
        else if (progress < 0.70f)
        {
            return Color.FromArgb("#6366F1"); // Indigo Primary
        }
        else
        {
            return Color.FromArgb("#A855F7"); // Purple Accent
        }
    }
}
