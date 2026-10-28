namespace Scriber.App.Controls;

using System.Windows.Input;
using Scriber.Core.Domain;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

/// <summary>
/// Interactive multi-track diarization timeline control rendered with SkiaSharp (PH2-UI-009, ADR-0002, ADR-0004).
/// Renders parallel horizontal speaker lanes, overlapping speech turns, pan/zoom interaction, and real-time playback cursor.
/// </summary>
public sealed class MultiTrackTimelineView : SKCanvasView, IDisposable
{
    private const float TrackHeight = 44f;
    private const float TrackHeaderWidth = 110f;
    private const float TrackMarginY = 6f;
    private const float HeaderPaddingX = 8f;

    private readonly SKPaint _bgPaint;
    private readonly SKPaint _trackBgPaint;
    private readonly SKPaint _trackDividerPaint;
    private readonly SKPaint _segmentPaint;
    private readonly SKPaint _segmentBorderPaint;
    private readonly SKPaint _textPaint;
    private readonly SKPaint _segmentTextPaint;
    private readonly SKFont _headerFont;
    private readonly SKFont _segmentFont;
    private readonly SKPaint _playheadPaint;
    private readonly SKPaint _playheadGlowPaint;

    private double _panOffset = 0.0;
    private double _panInitialOffset = 0.0;
    private bool _disposed;

    public static readonly BindableProperty TranscriptProperty =
        BindableProperty.Create(
            nameof(Transcript),
            typeof(Transcript),
            typeof(MultiTrackTimelineView),
            null,
            propertyChanged: (b, _, _) => ((MultiTrackTimelineView)b).InvalidateSurface());

    public static readonly BindableProperty CurrentPositionProperty =
        BindableProperty.Create(
            nameof(CurrentPosition),
            typeof(TimeSpan),
            typeof(MultiTrackTimelineView),
            TimeSpan.Zero,
            BindingMode.TwoWay,
            propertyChanged: (b, _, _) => ((MultiTrackTimelineView)b).InvalidateSurface());

    public static readonly BindableProperty DurationProperty =
        BindableProperty.Create(
            nameof(Duration),
            typeof(TimeSpan),
            typeof(MultiTrackTimelineView),
            TimeSpan.Zero,
            propertyChanged: (b, _, _) => ((MultiTrackTimelineView)b).InvalidateSurface());

    public static readonly BindableProperty ZoomLevelProperty =
        BindableProperty.Create(
            nameof(ZoomLevel),
            typeof(double),
            typeof(MultiTrackTimelineView),
            1.0,
            BindingMode.TwoWay,
            propertyChanged: (b, _, _) => ((MultiTrackTimelineView)b).InvalidateSurface());

    public static readonly BindableProperty SeekCommandProperty =
        BindableProperty.Create(
            nameof(SeekCommand),
            typeof(ICommand),
            typeof(MultiTrackTimelineView),
            null);

    public Transcript? Transcript
    {
        get => (Transcript?)GetValue(TranscriptProperty);
        set => SetValue(TranscriptProperty, value);
    }

    public TimeSpan CurrentPosition
    {
        get => (TimeSpan)GetValue(CurrentPositionProperty);
        set => SetValue(CurrentPositionProperty, value);
    }

    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public double ZoomLevel
    {
        get => (double)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, Math.Clamp(value, 0.5, 10.0));
    }

    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    public MultiTrackTimelineView()
    {
        _bgPaint = new SKPaint
        {
            Color = new SKColor(15, 23, 42), // Slate 900
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        _trackBgPaint = new SKPaint
        {
            Color = new SKColor(30, 41, 59, 180), // Slate 800
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        _trackDividerPaint = new SKPaint
        {
            Color = new SKColor(51, 65, 85), // Slate 700
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        _segmentPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        _segmentBorderPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 80),
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        _textPaint = new SKPaint
        {
            Color = new SKColor(226, 232, 240), // Slate 200
            IsAntialias = true
        };

        _segmentTextPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        _headerFont = new SKFont(SKTypeface.FromFamilyName("OpenSansRegular", SKFontStyle.Bold), 12f);
        _segmentFont = new SKFont(SKTypeface.FromFamilyName("OpenSansRegular", SKFontStyle.Normal), 10f);

        _playheadPaint = new SKPaint
        {
            Color = new SKColor(239, 68, 68), // Red 500
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        _playheadGlowPaint = new SKPaint
        {
            Color = new SKColor(239, 68, 68, 100),
            StrokeWidth = 6f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        // Gestures
        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += OnTapped;
        GestureRecognizers.Add(tapGesture);

        var panGesture = new PanGestureRecognizer();
        panGesture.PanUpdated += OnPanUpdated;
        GestureRecognizers.Add(panGesture);

        var pinchGesture = new PinchGestureRecognizer();
        pinchGesture.PinchUpdated += OnPinchUpdated;
        GestureRecognizers.Add(pinchGesture);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (pos == null || Width <= 0) return;

        float timelineStartX = TrackHeaderWidth;
        float availableWidth = (float)Width - timelineStartX;
        if (availableWidth <= 0) return;

        float tapX = (float)pos.Value.X - timelineStartX;
        if (tapX < 0) return;

        double totalDurationSec = Duration.TotalSeconds > 0
            ? Duration.TotalSeconds
            : (Transcript?.Audio.Duration.TotalSeconds ?? 0);

        if (totalDurationSec <= 0) return;

        double zoomedTimelineWidth = availableWidth * ZoomLevel;
        double clampedOffset = Math.Clamp(_panOffset, 0, Math.Max(0, zoomedTimelineWidth - availableWidth));
        double effectiveX = tapX + clampedOffset;

        double fraction = Math.Clamp(effectiveX / zoomedTimelineWidth, 0.0, 1.0);
        var targetTime = TimeSpan.FromSeconds(fraction * totalDurationSec);

        CurrentPosition = targetTime;
        SeekCommand?.Execute(targetTime);
        InvalidateSurface();
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panInitialOffset = _panOffset;
                break;
            case GestureStatus.Running:
                double timelineWidth = (Width - TrackHeaderWidth) * ZoomLevel;
                double maxOffset = Math.Max(0, timelineWidth - (Width - TrackHeaderWidth));
                _panOffset = Math.Clamp(_panInitialOffset - e.TotalX, 0, maxOffset);
                InvalidateSurface();
                break;
        }
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Running)
        {
            ZoomLevel = Math.Clamp(ZoomLevel * e.Scale, 0.5, 10.0);
            InvalidateSurface();
        }
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        var info = e.Info;

        canvas.Clear(SKColors.Transparent);

        var transcript = Transcript;
        var speakers = transcript?.Speakers ?? [];
        if (speakers.Count == 0 && transcript?.Segments.Count > 0)
        {
            var detectedIds = transcript.Segments.Select(s => s.SpeakerId).Distinct();
            speakers = detectedIds.Select((id, idx) => new Speaker(id, $"Speaker {idx + 1}", "#4F46E5")).ToList();
        }

        int trackCount = Math.Max(1, speakers.Count);
        float timelineStartX = TrackHeaderWidth;
        float availableWidth = Math.Max(10f, info.Width - timelineStartX);
        double totalDurationSec = Duration.TotalSeconds > 0
            ? Duration.TotalSeconds
            : (transcript?.Audio.Duration.TotalSeconds ?? 60.0);
        if (totalDurationSec <= 0) totalDurationSec = 60.0;

        float timelineWidth = availableWidth * (float)ZoomLevel;
        double maxOffset = Math.Max(0, timelineWidth - availableWidth);
        _panOffset = Math.Clamp(_panOffset, 0, maxOffset);
        float visibleOffset = (float)_panOffset;

        // Background
        var bounds = new SKRect(0, 0, info.Width, info.Height);
        canvas.DrawRoundRect(bounds, 8f, 8f, _bgPaint);

        // Render tracks
        for (int i = 0; i < trackCount; i++)
        {
            float trackY = i * (TrackHeight + TrackMarginY) + TrackMarginY;
            var speaker = i < speakers.Count ? speakers[i] : null;

            // Track lane background
            var trackRect = new SKRect(timelineStartX, trackY, info.Width - 4f, trackY + TrackHeight);
            canvas.DrawRoundRect(trackRect, 6f, 6f, _trackBgPaint);

            // Track header / label
            var headerRect = new SKRect(4f, trackY, timelineStartX - 4f, trackY + TrackHeight);
            canvas.DrawRoundRect(headerRect, 6f, 6f, _trackBgPaint);

            string spkName = speaker?.DisplayName ?? $"Speaker {i + 1}";
            string spkColor = speaker?.ColorHex ?? "#4F46E5";

            // Speaker badge pill
            if (SKColor.TryParse(spkColor, out var pillColor))
            {
                using var pillPaint = new SKPaint { Color = pillColor, Style = SKPaintStyle.Fill, IsAntialias = true };
                var pillRect = new SKRect(HeaderPaddingX, trackY + 8f, HeaderPaddingX + 6f, trackY + TrackHeight - 8f);
                canvas.DrawRoundRect(pillRect, 3f, 3f, pillPaint);
            }

            canvas.DrawText(spkName, HeaderPaddingX + 12f, trackY + (TrackHeight / 2f) + 4f, SKTextAlign.Left, _headerFont, _textPaint);

            // Draw segments attributed to this speaker
            if (transcript != null)
            {
                var spkSegments = transcript.Segments.Where(s => s.SpeakerId == (speaker?.Id ?? $"speaker-{i}")).ToList();

                canvas.Save();
                canvas.ClipRect(trackRect);

                foreach (var seg in spkSegments)
                {
                    double startSec = seg.Start.TotalSeconds;
                    double endSec = seg.End.TotalSeconds;
                    if (endSec <= startSec) endSec = startSec + 0.5;

                    float segStartX = timelineStartX + (float)((startSec / totalDurationSec) * timelineWidth) - visibleOffset;
                    float segEndX = timelineStartX + (float)((endSec / totalDurationSec) * timelineWidth) - visibleOffset;
                    float segWidth = Math.Max(4f, segEndX - segStartX);

                    // Skip if out of visible bounds
                    if (segStartX + segWidth < timelineStartX || segStartX > info.Width) continue;

                    var segRect = new SKRect(segStartX, trackY + 4f, segStartX + segWidth, trackY + TrackHeight - 4f);

                    SKColor blockColor = SKColor.TryParse(spkColor, out var c) ? c : new SKColor(79, 70, 229);
                    _segmentPaint.Color = blockColor.WithAlpha(210);
                    canvas.DrawRoundRect(segRect, 4f, 4f, _segmentPaint);
                    canvas.DrawRoundRect(segRect, 4f, 4f, _segmentBorderPaint);

                    if (segWidth > 40f && !string.IsNullOrWhiteSpace(seg.Text))
                    {
                        string displayText = seg.Text.Length > 20 ? seg.Text.Substring(0, 18) + ".." : seg.Text;
                        canvas.Save();
                        canvas.ClipRect(segRect);
                        canvas.DrawText(displayText, segStartX + 4f, trackY + (TrackHeight / 2f) + 3f, SKTextAlign.Left, _segmentFont, _segmentTextPaint);
                        canvas.Restore();
                    }
                }

                canvas.Restore();
            }
        }

        // Draw Playhead across all tracks
        double playheadSec = CurrentPosition.TotalSeconds;
        float playheadX = timelineStartX + (float)((playheadSec / totalDurationSec) * timelineWidth) - visibleOffset;

        if (playheadX >= timelineStartX && playheadX <= info.Width)
        {
            float totalTrackBottom = trackCount * (TrackHeight + TrackMarginY) + TrackMarginY;
            canvas.DrawLine(playheadX, 0, playheadX, totalTrackBottom, _playheadGlowPaint);
            canvas.DrawLine(playheadX, 0, playheadX, totalTrackBottom, _playheadPaint);

            // Playhead scrubber handle triangle
            var pathBuilder = new SKPathBuilder();
            pathBuilder.MoveTo(playheadX - 6f, 0);
            pathBuilder.LineTo(playheadX + 6f, 0);
            pathBuilder.LineTo(playheadX, 8f);
            pathBuilder.Close();
            using var path = pathBuilder.Detach();
            using var handlePaint = new SKPaint { Color = new SKColor(239, 68, 68), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawPath(path, handlePaint);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _bgPaint.Dispose();
            _trackBgPaint.Dispose();
            _trackDividerPaint.Dispose();
            _segmentPaint.Dispose();
            _segmentBorderPaint.Dispose();
            _textPaint.Dispose();
            _segmentTextPaint.Dispose();
            _headerFont.Dispose();
            _segmentFont.Dispose();
            _playheadPaint.Dispose();
            _playheadGlowPaint.Dispose();
        }
    }
}
