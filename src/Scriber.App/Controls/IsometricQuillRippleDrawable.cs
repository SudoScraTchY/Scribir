using Microsoft.Maui.Graphics;

namespace Scriber.App.Controls;

/// <summary>
/// High-performance MAUI IDrawable rendering the Minimalist Isometric Ancient Persian Quill
/// with continuous, language-agnostic concentric diamond acoustic ripple waves.
/// Designed with zero embedded text and zero loading bars for pure geometric elegance.
/// </summary>
public sealed class IsometricQuillRippleDrawable : IDrawable
{
    private float _animationPhase;

    /// <summary>
    /// Advances the ripple expansion animation cycle [0.0 - 1.0].
    /// </summary>
    public void AdvanceAnimation(float delta = 0.02f)
    {
        _animationPhase = (_animationPhase + delta) % 1.0f;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float width = dirtyRect.Width;
        float height = dirtyRect.Height;
        float centerX = width / 2f;
        float centerY = height / 2f;

        canvas.SaveState();
        canvas.Antialias = true;

        // Draw 3 Concentric Animated Isometric Diamond Ripples
        DrawAnimatedRipples(canvas, centerX, centerY);

        // Draw Central Isometric Ancient Persian Quill Pen
        DrawIsometricQuill(canvas, centerX, centerY);

        canvas.RestoreState();
    }

    private void DrawAnimatedRipples(ICanvas canvas, float cx, float cy)
    {
        int rippleCount = 3;
        float maxRadius = 140f;

        for (int i = 0; i < rippleCount; i++)
        {
            // Stagger phase for each concentric ring
            float localPhase = (_animationPhase + (float)i / rippleCount) % 1.0f;
            float currentRadius = 30f + (localPhase * maxRadius);
            float opacity = (1.0f - localPhase) * 0.7f;

            if (opacity <= 0.02f) continue;

            // Isometric diamond geometry (horizontal radius is wider than vertical by 1.73 ratio for 30 deg isometric plane)
            float rx = currentRadius * 1.4f;
            float ry = currentRadius * 0.8f;

            // Alternate colors between Susa Turquoise and Achaemenid Gold
            Color rippleColor = (i % 2 == 0)
                ? Color.FromRgba(0x4E / 255f, 0xEC / 255f, 0xD6 / 255f, opacity)
                : Color.FromRgba(0xF3 / 255f, 0xC6 / 255f, 0x44 / 255f, opacity);

            canvas.StrokeColor = rippleColor;
            canvas.StrokeSize = 2f + (1.0f - localPhase) * 1.5f;

            using var path = new PathF();
            path.MoveTo(cx, cy - ry);
            path.LineTo(cx + rx, cy);
            path.LineTo(cx, cy + ry);
            path.LineTo(cx - rx, cy);
            path.Close();

            canvas.DrawPath(path);

            // Small acoustic spark node along the diamond horizon
            canvas.FillColor = rippleColor;
            canvas.FillCircle(cx + rx, cy, 3f);
            canvas.FillCircle(cx - rx, cy, 3f);
        }
    }

    private static void DrawIsometricQuill(ICanvas canvas, float cx, float cy)
    {
        // Stylus / Quill anchor point is poised at (cx, cy)
        float qx = cx;
        float qy = cy - 25f;

        // Colors
        var topFacet = Color.FromRgb(0xFF, 0xF6, 0xD1);      // Electrum Light Gold
        var leftFacet = Color.FromRgb(0xF3, 0xC6, 0x44);     // Achaemenid Warm Gold
        var leftBlade = Color.FromRgb(0xD4, 0xAF, 0x37);     // Imperial Gold Mid
        var rightBlade = Color.FromRgb(0x9A, 0x7B, 0x1C);    // Bronze Shadow Gold
        var slitColor = Color.FromRgb(0x0D, 0x1B, 0x36);     // Negative Space Navy

        // 1. Upper Shaft Top Face
        using (var path = new PathF())
        {
            path.MoveTo(qx, qy - 65f);
            path.LineTo(qx + 26f, qy - 81f);
            path.LineTo(qx + 44f, qy - 50f);
            path.LineTo(qx + 18f, qy - 34f);
            path.Close();
            canvas.FillColor = topFacet;
            canvas.FillPath(path);
        }

        // 2. Upper Shaft Left Face
        using (var path = new PathF())
        {
            path.MoveTo(qx, qy - 65f);
            path.LineTo(qx + 18f, qy - 34f);
            path.LineTo(qx - 18f, qy - 13f);
            path.LineTo(qx - 36f, qy - 44f);
            path.Close();
            canvas.FillColor = leftFacet;
            canvas.FillPath(path);
        }

        // 3. Beveled Chisel Nib (Left Blade)
        using (var path = new PathF())
        {
            path.MoveTo(qx - 18f, qy - 13f);
            path.LineTo(qx + 18f, qy - 34f);
            path.LineTo(qx, qy + 36f);
            path.Close();
            canvas.FillColor = leftBlade;
            canvas.FillPath(path);
        }

        // 4. Beveled Chisel Nib (Right Blade)
        using (var path = new PathF())
        {
            path.MoveTo(qx + 18f, qy - 34f);
            path.LineTo(qx + 44f, qy - 50f);
            path.LineTo(qx, qy + 36f);
            path.Close();
            canvas.FillColor = rightBlade;
            canvas.FillPath(path);
        }

        // 5. Isometric Nib Slit (Negative Space Cutout)
        using (var path = new PathF())
        {
            path.MoveTo(qx, qy - 10f);
            path.LineTo(qx + 6f, qy - 18f);
            path.LineTo(qx, qy + 33f);
            path.LineTo(qx - 6f, qy + 18f);
            path.Close();
            canvas.FillColor = slitColor;
            canvas.FillPath(path);
        }

        // 6. Nib Writing Contact Spark
        canvas.FillColor = Colors.White;
        canvas.FillCircle(qx, qy + 36f, 3.5f);
    }
}
