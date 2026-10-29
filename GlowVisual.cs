using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EdgeGlow;

/// <summary>
/// Soft edge light rendered as a light field: for every sample near the screen edge we
/// compute brightness from its distance to the edge and colour from its position around
/// the screen perimeter. The field is low-resolution and upscaled smoothly by WPF.
/// Nothing is drawn unless a song is playing.
/// </summary>
public sealed class GlowVisual : FrameworkElement
{
    const int ProfileSize = 1024;   // brightness vs. distance-from-edge lookup
    const int PathSize = 2048;      // colour / snake lookup around the perimeter

    readonly Settings _s;
    readonly AudioAnalyzer _audio;
    readonly MediaColorProvider _media;
    readonly DrawingVisual _dv = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly List<double> _ripples = new();
    readonly float[] _spark = new float[PathSize];
    readonly Random _rng = new();
    double _scan;

    // animation state
    double _lastDraw, _master, _phase, _snakePos, _speed, _surge, _prevBeat, _lvl, _pulse;
    double[] _p = { 124, 92, 255 };
    double[] _q = { 0, 212, 255 };
    string _pKey = "", _qKey = "";
    Color _pCache = Colors.MediumPurple, _qCache = Colors.DeepSkyBlue;

    // low-resolution light field (rebuilt only when the screen size changes)
    double _cw, _ch;
    int _gw, _gh;
    float[] _dist = Array.Empty<float>();
    ushort[] _pathIdx = Array.Empty<ushort>();
    float[] _noise = Array.Empty<float>();
    byte[] _buf = Array.Empty<byte>();
    WriteableBitmap? _bmp;

    // per-frame lookup tables
    readonly float[] _profile = new float[ProfileSize];
    readonly float[] _snakeA = new float[PathSize];
    readonly byte[] _colR = new byte[PathSize];
    readonly byte[] _colG = new byte[PathSize];
    readonly byte[] _colB = new byte[PathSize];

    public GlowVisual(Settings s, AudioAnalyzer audio, MediaColorProvider media)
    {
        _s = s;
        _audio = audio;
        _media = media;
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(_dv, BitmapScalingMode.Linear);
        AddVisualChild(_dv);
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _dv;

    void OnRendering(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = now - _lastDraw;
        if (dt < 1.0 / 60 - 0.002) return;   // cap at ~60 fps
        _lastDraw = now;
        Draw(now, Math.Min(dt, 0.1));
    }

    // =====================================================================
    //  Per-frame logic
    // =====================================================================

    void Draw(double t, double dt)
    {
        using var dc = _dv.RenderOpen();
        double w = ActualWidth, h = ActualHeight;

        // Fade in/out depending on whether a song is playing.
        bool active = _s.Enabled && Activity.IsPlaying(_media, _audio);
        _master += ((active ? 1.0 : 0.0) - _master) * (1 - Math.Exp(-(active ? 4.0 : 2.5) * dt));
        if (_master < 0.01 || w < 40 || h < 40) return;

        // --- Music values, smoothed so nothing jumps ----------------------
        var (level, beat) = _audio.Snapshot();
        _lvl += (level - _lvl) * (1 - Math.Exp(-(level > _lvl ? 12.0 : 3.0) * dt));
        _pulse += (beat - _pulse) * (1 - Math.Exp(-(beat > _pulse ? 40.0 : 7.0) * dt));

        double intensity, pulse;
        switch (_s.Mode)
        {
            case GlowMode.Static:
                // three waves at different speeds, so the shimmer never repeats exactly
                intensity = 0.60 + 0.12 * Math.Sin(t * 0.9) + 0.08 * Math.Sin(t * 2.3 + 1.3)
                                 + 0.05 * Math.Sin(t * 5.1);
                pulse = 0;
                break;
            case GlowMode.Snake:
                intensity = 0.55 + 0.45 * _lvl;
                pulse = _pulse * 0.8;
                break;
                        case GlowMode.Aurora:
                intensity = 0.55 + 0.45 * _lvl;
                pulse = _pulse * 0.6;
                break;
            case GlowMode.Chase:
                intensity = 0.55 + 0.45 * _lvl;
                pulse = _pulse * 0.8;
                break;
            case GlowMode.Sparkle:
                intensity = 0.55 + 0.45 * _lvl;
                pulse = _pulse * 0.5;
                break;
            default:
                intensity = 0.25 + 0.75 * _lvl;
                pulse = _pulse;
                break;
        }

        // --- Colours --------------------------------------------------------
        Color tp, tq;
        var album = _media.Current;
        if (_s.UseAlbumColors && album != null)
        {
            tp = album.Primary;
            tq = album.Secondary;
        }
        else
        {
            tp = Custom(_s.PrimaryColor, _s.PrimaryBrightness, ref _pKey, ref _pCache, Colors.MediumPurple);
            tq = Custom(_s.SecondaryColor, _s.SecondaryBrightness, ref _qKey, ref _qCache, Colors.DeepSkyBlue);
        }
        if (_s.Gradient == GradientMode.OneColor) tq = tp;

        Ease(_p, tp);
        Ease(_q, tq);
        var cp = Color.FromRgb((byte)_p[0], (byte)_p[1], (byte)_p[2]);
        var cq = Color.FromRgb((byte)_q[0], (byte)_q[1], (byte)_q[2]);

        // colours now follow the music
        var (bass, mid, treble) = _audio.Bands();
        Color accent = (_s.UseAlbumColors && album?.Accent is Color ac) ? ac : Shift(cp, 150);

        double drift = _s.Mode == GlowMode.Static ? 25 : 10;
        cp = Shift(cp, drift * Math.Sin(t * 0.5) + 35 * (mid - 0.4));            // mids sway the primary hue
        cq = Shift(cq, -drift * Math.Sin(t * 0.5 + 1.0) + 35 * (treble - 0.4));  // treble sways the secondary
        cp = Mix(cp, accent, 0.65 * pulse);                                       // beats flash the accent colour
        cq = Mix(cq, accent, 0.45 * pulse);
        cp = Flash(cp, 0.15 * pulse);
        cq = Flash(cq, 0.10 * pulse);

        // --- Ripples (Music Sync): a ring of light travels inward on every beat --
        if (_s.Mode == GlowMode.MusicSync)
        {
            if (beat > 0.95 && _prevBeat <= 0.95) _ripples.Add(0);
        }
        else
        {
            _ripples.Clear();
        }
        _prevBeat = beat;
        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            _ripples[i] += dt;
            if (_ripples[i] > 0.9) _ripples.RemoveAt(i);
        }

        // --- Shape of the light ------------------------------------------------
        bool snake = _s.Mode == GlowMode.Snake;
        bool pathMode = _s.Mode is GlowMode.Snake or GlowMode.Aurora or GlowMode.Chase or GlowMode.Sparkle;
        double maxInset = Math.Min(w, h) / 2.0;
        double thick = Math.Clamp(_s.Thickness * (1 + 0.4 * pulse + 0.6 * bass), 1, maxInset / 3);
        double glow = Math.Clamp(_s.Glow * (0.4 + 0.6 * intensity) * (1 + 0.35 * pulse + 1.0 * bass), 1, maxInset - thick);
        double depth = thick + glow;
        double alpha = Math.Clamp(_s.Brightness * (0.45 + 0.55 * intensity) + 0.2 * pulse + 0.25 * bass, 0, 1) * _master;

        if (_bmp == null || Math.Abs(w - _cw) > 0.5 || Math.Abs(h - _ch) > 0.5)
            BuildGeometry(w, h);

        BuildProfile(thick, glow, depth, pathMode ? 0.85 : 0.60);

        double perimeter = 2 * (w + h);
        if (snake)
        {
            // Momentum: speed eases toward its target instead of jumping.
            _surge = Math.Max(_surge * Math.Exp(-3 * dt), _pulse);
            double target = perimeter / (10.0 - 5.0 * _lvl) * (1 + 1.2 * _surge);
            if (_speed <= 0) _speed = perimeter / 10.0;
            _speed += (target - _speed) * (1 - Math.Exp(-2.5 * dt));
            _snakePos = (_snakePos + _speed * dt) % perimeter;

            double length = perimeter * Math.Clamp(_s.SnakeLength, 0.05, 0.8) * (0.85 + 0.3 * _lvl);
            length = Math.Min(length, perimeter * 0.45);
            BuildSnakeTables(perimeter, length, _snakePos, cp, cq);
        }
          else if (pathMode)
        {
            _phase = (_phase + (0.04 + 0.10 * _lvl) * dt) % 1.0;
            switch (_s.Mode)
            {
                case GlowMode.Aurora:    BuildAuroraTables(cp, cq, mid, treble); break;
                case GlowMode.Chase:     BuildChaseTables(perimeter, cp, cq, dt); break;
                default:                 BuildSparkleTables(cp, cq, dt, treble); break;
            }
        }
        else
        {
            _phase = (_phase + (0.03 + 0.12 * _lvl) * dt) % 1.0;
            BuildBorderTables(cp, cq);
        }

        Fill(depth, alpha, pathMode);

        var bmp = _bmp!;
        bmp.WritePixels(new Int32Rect(0, 0, _gw, _gh), _buf, _gw * 4, 0);
        dc.DrawImage(bmp, new Rect(0, 0, w, h));
    }

    // =====================================================================
    //  Light field
    // =====================================================================

    /// <summary>Precomputes, for every sample: distance from the screen edge (with softly
    /// rounded corners) and its position around the perimeter. Runs only on size change.</summary>
    void BuildGeometry(double w, double h)
    {
        _cw = w;
        _ch = h;
        double scale = Math.Max(2.0, Math.Sqrt(w * h / 450000.0));
        _gw = Math.Max(8, (int)Math.Ceiling(w / scale));
        _gh = Math.Max(8, (int)Math.Ceiling(h / scale));
        int n = _gw * _gh;

        _dist = new float[n];
        _pathIdx = new ushort[n];
        _noise = new float[n];
        _buf = new byte[n * 4];
        _bmp = new WriteableBitmap(_gw, _gh, 96, 96, PixelFormats.Pbgra32, null);

        double sx = w / _gw, sy = h / _gh;
        double P = 2 * (w + h);
        var rnd = new Random(1234);

        for (int gy = 0; gy < _gh; gy++)
        {
            double y = (gy + 0.5) * sy;
            for (int gx = 0; gx < _gw; gx++)
            {
                double x = (gx + 0.5) * sx;
                int i = gy * _gw + gx;

                double a = Math.Min(x, w - x);       // distance to nearest left/right edge
                double b = Math.Min(y, h - y);       // distance to nearest top/bottom edge
                double m = Math.Min(a, b);
                double d = Math.Max(0, m - 0.5 * Math.Min(sx, sy));

                // position along the clockwise perimeter, blended smoothly around corners
                double sH = y < h / 2 ? x : w + h + (w - x);
                double sV = x < w / 2 ? 2 * w + h + (h - y) : w + y;
                double diff = sV - sH;
                if (diff > P / 2) sV -= P; else if (diff < -P / 2) sV += P;
                double wh = 0.5 + 0.5 * Math.Clamp((a - b) / 3.0, -1, 1);
                wh = wh * wh * (3 - 2 * wh);
                double sp = sV + (sH - sV) * wh;
                sp -= Math.Floor(sp / P) * P;

                _dist[i] = (float)d;
                _pathIdx[i] = (ushort)Math.Min(PathSize - 1, (int)(sp / P * PathSize));
                _noise[i] = (float)(rnd.NextDouble() - 0.5);
            }
        }
    }

    /// <summary>Brightness as a function of distance from the screen edge:
    /// a bright core line, then a smooth soft falloff, plus any beat ripples.</summary>
    void BuildProfile(double thick, double glow, double depth, double peak)
    {
        for (int i = 0; i < ProfileSize; i++)
        {
            double d = depth * i / (ProfileSize - 1);
            double v;
            if (d <= thick)
            {
                v = 1.0;
            }
            else
            {
                double s = (d - thick) / glow;
                double g = peak * Math.Pow(Math.Max(0, 1 - s), 2.2);
                double blend = Math.Min(1.0, (d - thick) / 1.5);
                v = 1.0 + (g - 1.0) * blend;
            }
            _profile[i] = (float)v;
        }

        foreach (double age in _ripples)
        {
            double k = age / 0.9;
            double rd = thick + glow * 1.1 * k;
            double wd = 10 + 20 * k;
            double amp = 0.6 * (1 - k) * (1 - k);
            for (int i = 0; i < ProfileSize; i++)
            {
                double d = depth * i / (ProfileSize - 1);
                double z = (d - rd) / wd;
                _profile[i] = Math.Min(1f, _profile[i] + (float)(amp * Math.Exp(-z * z)));
            }
        }
    }

    /// <summary>Colour flowing around the whole screen edge (~60% primary / 40% secondary).</summary>
    void BuildBorderTables(Color cp, Color cq)
    {
        for (int i = 0; i < PathSize; i++)
        {
            double u = (double)i / PathSize + _phase;
            double wave = 0.5 - 0.5 * Math.Cos(2 * Math.PI * u);
            double b = SmoothStep(0.404, 0.904, wave);
            _colR[i] = (byte)(cp.R + (cq.R - cp.R) * b);
            _colG[i] = (byte)(cp.G + (cq.G - cp.G) * b);
            _colB[i] = (byte)(cp.B + (cq.B - cp.B) * b);
            _snakeA[i] = 1f;
        }
    }

    /// <summary>Snake: bright head, fading tail, only along a stretch of the perimeter.</summary>
    void BuildSnakeTables(double perimeter, double length, double head, Color cp, Color cq)
    {
        for (int i = 0; i < PathSize; i++)
        {
            double s = (i + 0.5) / PathSize * perimeter;
            double behind = head - s;
            behind -= Math.Floor(behind / perimeter) * perimeter;   // distance behind the head

            if (behind < length)
            {
                double u = 1 - behind / length;                      // 0 = tail tip, 1 = head
                double a = Math.Pow(u, 1.6) * Math.Min(1.0, (1.0 - u) / 0.05);
                double b = SmoothStep(0.25, 0.55, u);                // head = primary, tail = secondary
                _snakeA[i] = (float)a;
                _colR[i] = (byte)(cq.R + (cp.R - cq.R) * b);
                _colG[i] = (byte)(cq.G + (cp.G - cq.G) * b);
                _colB[i] = (byte)(cq.B + (cp.B - cq.B) * b);
            }
            else
            {
                _snakeA[i] = 0f;
                _colR[i] = cp.R; _colG[i] = cp.G; _colB[i] = cp.B;
            }
        }
    }

        // ----- Aurora: flowing ribbons of light and colour -----------------------
    void BuildAuroraTables(Color cp, Color cq, double mid, double treble)
    {
        double contrast = 1.0 + 1.5 * mid;
        for (int i = 0; i < PathSize; i++)
        {
            double u = (double)i / PathSize;
            double w1 = Math.Sin(2 * Math.PI * (2 * u - _phase * 3.0));
            double w2 = Math.Sin(2 * Math.PI * (3 * u + _phase * 2.0) + 1.7);
            double w3 = Math.Sin(2 * Math.PI * (5 * u - _phase * 5.0) + 0.6);
            double v = 0.5 + 0.5 * (0.55 * w1 + 0.30 * w2 + 0.15 * w3 * (0.5 + treble));
            v = Math.Clamp(v, 0, 1);
            _snakeA[i] = (float)(0.10 + 0.90 * Math.Pow(v, contrast));

            double b = 0.5 + 0.5 * Math.Sin(2 * Math.PI * (u + _phase * 1.5));
            _colR[i] = (byte)(cp.R + (cq.R - cp.R) * b);
            _colG[i] = (byte)(cp.G + (cq.G - cp.G) * b);
            _colB[i] = (byte)(cp.B + (cq.B - cp.B) * b);
        }
    }

    // ----- Chase: four comets circling together --------------------------------
    void BuildChaseTables(double perimeter, Color cp, Color cq, double dt)
    {
        _surge = Math.Max(_surge * Math.Exp(-3 * dt), _pulse);
        _scan = (_scan + perimeter / (12.0 - 6.0 * _lvl) * (1 + 1.0 * _surge) * dt) % perimeter;

        const int N = 2;
        double length = perimeter / N * 0.45;
        for (int i = 0; i < PathSize; i++)
        {
            double s = (i + 0.5) / PathSize * perimeter;
            float a = 0f;
            double ub = 0;
            for (int n = 0; n < N; n++)
            {
                double head = _scan + n * perimeter / N;
                double behind = head - s;
                behind -= Math.Floor(behind / perimeter) * perimeter;
                if (behind < length)
                {
                    double u = 1 - behind / length;
                    float v = (float)(Math.Pow(u, 1.6) * Math.Min(1.0, (1.0 - u) / 0.05));
                    if (v > a) { a = v; ub = u; }
                }
            }
            double b = SmoothStep(0.25, 0.55, ub);
            _snakeA[i] = a;
            _colR[i] = (byte)(cq.R + (cp.R - cq.R) * b);
            _colG[i] = (byte)(cq.G + (cp.G - cq.G) * b);
            _colB[i] = (byte)(cq.B + (cp.B - cq.B) * b);
        }
    }
    
    // ----- Sparkle: twinkling lights triggered by treble and beats -----------------
    void BuildSparkleTables(Color cp, Color cq, double dt, double treble)
    {
        float decay = (float)Math.Exp(-5.0 * dt);
        for (int i = 0; i < PathSize; i++) _spark[i] *= decay;

        double expected = (15 + 250 * treble + 300 * _pulse) * dt;
        int births = (int)expected;
        if (_rng.NextDouble() < expected - births) births++;

        for (int n = 0; n < births; n++)
        {
            int c = _rng.Next(PathSize);
            float v = (float)(0.6 + 0.4 * _rng.NextDouble());
            for (int d = -6; d <= 6; d++)
            {
                int j = (c + d + PathSize) % PathSize;
                float g = v * (float)Math.Exp(-(d * d) / 18.0);
                if (g > _spark[j]) _spark[j] = g;
            }
        }

        for (int i = 0; i < PathSize; i++)
        {
            _snakeA[i] = 0.12f + 0.88f * Math.Min(1f, _spark[i]);
            double b = 0.5 + 0.5 * Math.Sin(2 * Math.PI * ((double)i / PathSize * 2 + _phase));
            _colR[i] = (byte)(cp.R + (cq.R - cp.R) * b);
            _colG[i] = (byte)(cp.G + (cq.G - cp.G) * b);
            _colB[i] = (byte)(cp.B + (cq.B - cp.B) * b);
        }
    }

    /// <summary>Writes the light field into the bitmap (premultiplied BGRA).</summary>
    void Fill(double depth, double alpha, bool snake)
    {
        int n = _gw * _gh;
        float fDepth = (float)depth;
        float invDepth = (float)((ProfileSize - 1) / depth);
        float gain = (float)(alpha * 255.0);

        var buf = _buf; var dist = _dist; var idx = _pathIdx; var noise = _noise;
        var prof = _profile; var sa = _snakeA;
        var cr = _colR; var cg = _colG; var cb = _colB;

        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            float d = dist[i];
            if (d >= fDepth)
            {
                buf[o] = 0; buf[o + 1] = 0; buf[o + 2] = 0; buf[o + 3] = 0;
                continue;
            }

            int ci = idx[i];
            float a = prof[(int)(d * invDepth)] * gain;
            if (snake) a *= sa[ci];
            a += noise[i];                       // tiny dither hides colour banding

            if (a < 0.5f)
            {
                buf[o] = 0; buf[o + 1] = 0; buf[o + 2] = 0; buf[o + 3] = 0;
                continue;
            }
            if (a > 255f) a = 255f;

            float f = a / 255f;
            buf[o] = (byte)(cb[ci] * f + 0.5f);
            buf[o + 1] = (byte)(cg[ci] * f + 0.5f);
            buf[o + 2] = (byte)(cr[ci] * f + 0.5f);
            buf[o + 3] = (byte)(a + 0.5f);
        }
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    static double SmoothStep(double e0, double e1, double x)
    {
        double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    static Color Custom(string hex, double brightness, ref string key, ref Color cache, Color fallback)
    {
        string k = hex + "|" + brightness.ToString("F3");
        if (k != key)
        {
            key = k;
            var c = ColorUtil.Parse(hex, fallback);
            ColorUtil.RgbToHsv(c.R / 255.0, c.G / 255.0, c.B / 255.0, out double hue, out double sat, out _);
            cache = ColorUtil.HsvToRgb(hue, sat, Math.Clamp(brightness, 0.05, 1));
        }
        return cache;
    }

    static void Ease(double[] cur, Color target)
    {
        cur[0] += (target.R - cur[0]) * 0.08;
        cur[1] += (target.G - cur[1]) * 0.08;
        cur[2] += (target.B - cur[2]) * 0.08;
    }

    static Color Shift(Color c, double deg)
    {
        ColorUtil.RgbToHsv(c.R / 255.0, c.G / 255.0, c.B / 255.0, out double h, out double s, out double v);
        return ColorUtil.HsvToRgb(h + deg, s, v);
    }

    static Color Flash(Color c, double k) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * k),
        (byte)(c.G + (255 - c.G) * k),
        (byte)(c.B + (255 - c.B) * k));

    static Color Mix(Color a, Color b, double k) => Color.FromRgb(
    (byte)(a.R + (b.R - a.R) * k),
    (byte)(a.G + (b.G - a.G) * k),
    (byte)(a.B + (b.B - a.B) * k));
}