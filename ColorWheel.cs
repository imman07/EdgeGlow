using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EdgeGlow;

/// <summary>HSV colour wheel: angle = hue, distance from centre = saturation.
/// The wheel is dimmed by Brightness (like the reference design).</summary>
public sealed class ColorWheel : FrameworkElement
{
    const int N = 256;

    WriteableBitmap? _bmp;
    double _hue, _sat = 1, _val = 1;

    public event EventHandler? ColorChanged;

    public double Hue => _hue;
    public double Saturation => _sat;

    public double Brightness
    {
        get => _val;
        set
        {
            double v = Math.Clamp(value, 0, 1);
            if (_bmp != null && Math.Abs(v - _val) < 1e-3) return;
            _val = v;
            Rebuild();
            InvalidateVisual();
        }
    }

    public ColorWheel()
    {
        Cursor = Cursors.Hand;
        Rebuild();
    }

    public void SetHueSat(double hue, double sat)
    {
        _hue = hue;
        _sat = Math.Clamp(sat, 0, 1);
        InvalidateVisual();
    }

    void Rebuild()
    {
        var px = new byte[N * N * 4];
        double c = (N - 1) / 2.0;
        double R = N / 2.0 - 1;
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                double dx = x - c, dy = y - c;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d > R + 1) continue;
                double a = d <= R ? 1.0 : Math.Max(0, R + 1 - d);
                double ang = Math.Atan2(dy, dx) * 180 / Math.PI;
                if (ang < 0) ang += 360;
                var col = ColorUtil.HsvToRgb(ang, Math.Min(1, d / R), _val);
                int o = (y * N + x) * 4;
                px[o] = (byte)(col.B * a);
                px[o + 1] = (byte)(col.G * a);
                px[o + 2] = (byte)(col.R * a);
                px[o + 3] = (byte)(255 * a);
            }
        }
        _bmp = new WriteableBitmap(N, N, 96, 96, PixelFormats.Pbgra32, null);
        _bmp.WritePixels(new Int32Rect(0, 0, N, N), px, N * 4, 0);
    }

    double Radius => ActualWidth / 2 * (N - 2.0) / N;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (_bmp != null) dc.DrawImage(_bmp, new Rect(0, 0, w, h));

        double rad = _hue * Math.PI / 180;
        var pt = new Point(w / 2 + Radius * _sat * Math.Cos(rad),
                           h / 2 + Radius * _sat * Math.Sin(rad));
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 4), pt, 9, 9);
        dc.DrawEllipse(null, new Pen(Brushes.White, 2), pt, 9, 9);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        Pick(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) Pick(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    void Pick(Point p)
    {
        double dx = p.X - ActualWidth / 2, dy = p.Y - ActualHeight / 2;
        double d = Math.Sqrt(dx * dx + dy * dy);
        _sat = Math.Min(1, d / Radius);
        double ang = Math.Atan2(dy, dx) * 180 / Math.PI;
        _hue = ang < 0 ? ang + 360 : ang;
        InvalidateVisual();
        ColorChanged?.Invoke(this, EventArgs.Empty);
    }
}
