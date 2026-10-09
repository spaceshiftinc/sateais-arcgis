using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SateAIs.AddIn.Jobs;

/// <summary>The empty state's backdrop: a slow twinkle over a soft glow. Only animates while visible.</summary>
internal sealed class Starfield : FrameworkElement
{
    private const int StarCount = 44;
    private const uint Seed = 7;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(66); // ~15 fps: calm, and easy on the battery
    private static readonly Color Glow = Color.FromRgb(0x13, 0x1A, 0x33);
    private static readonly Color Deep = Color.FromRgb(0x05, 0x08, 0x0F);

    private readonly (double X, double Y, double R, double Base, double Amp, double Phase, double Speed)[] _stars = MakeStars();
    private readonly DispatcherTimer _timer;
    private double _t;

    public Starfield()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = Tick };
        _timer.Tick += (_, _) =>
        {
            _t += Tick.TotalSeconds;
            InvalidateVisual();
        };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = RenderSize.Width;
        var height = RenderSize.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }
        // A circular glow, 0.85 of the longer side; the brush's radii are relative to each side.
        var radius = 0.85 * Math.Max(width, height);
        var glow = new RadialGradientBrush(Glow, Deep)
        {
            Center = new Point(0.5, 0.30),
            GradientOrigin = new Point(0.5, 0.30),
            RadiusX = radius / width,
            RadiusY = radius / height,
        };
        dc.DrawRectangle(glow, null, new Rect(0, 0, width, height));
        foreach (var star in _stars)
        {
            var alpha = Math.Clamp(star.Base + star.Amp * Math.Sin(_t * star.Speed + star.Phase), 0, 1);
            var brush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), 200, 220, 255));
            dc.DrawEllipse(brush, null, new Point(star.X * width, star.Y * height), star.R, star.R);
        }
    }

    /// <summary>A fixed layout from a tiny generator, so the sky is the same every time.</summary>
    private static (double, double, double, double, double, double, double)[] MakeStars()
    {
        var state = Seed;
        double Next()
        {
            state = unchecked(state * 1664525u + 1013904223u);
            return state / 4294967296.0;
        }
        double Between(double low, double high) => low + (high - low) * Next();

        var stars = new (double, double, double, double, double, double, double)[StarCount];
        for (var i = 0; i < StarCount; i++)
        {
            stars[i] = (Next(), Next(), Between(0.6, 1.7), Between(0.12, 0.55), Between(0.10, 0.40), Between(0, Math.Tau), Between(0.4, 1.2));
        }
        return stars;
    }
}
