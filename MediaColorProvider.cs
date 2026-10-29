using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace EdgeGlow;

/// <summary>
/// Watches the Windows "now playing" session (Spotify, Apple Music, browsers, etc.)
/// and extracts two vivid colors from the current album art.
/// </summary>
public sealed class MediaColorProvider : IDisposable
{
    readonly CancellationTokenSource _cts = new();
    string _lastKey = "";
    int _attempts;
    bool _isPlaying;

    /// <summary>True while any media session reports Playing.</summary>
    public bool IsPlaying => Volatile.Read(ref _isPlaying);

    /// <summary>Latest album colors, or null if nothing is playing / no art.</summary>
    public ColorPair? Current { get; private set; }

    public void Start() => _ = Task.Run(LoopAsync);

    async Task LoopAsync()
    {
        GlobalSystemMediaTransportControlsSessionManager mgr;
        try { mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
        catch { return; }

        while (!_cts.IsCancellationRequested)
        {
            try { await PollAsync(mgr); } catch { /* keep going */ }
            try { await Task.Delay(600, _cts.Token); } catch { break; }
        }
    }

    async Task PollAsync(GlobalSystemMediaTransportControlsSessionManager mgr)
    {
        GlobalSystemMediaTransportControlsSession? session = null;
        bool playing = false;
        foreach (var candidate in mgr.GetSessions())
        {
            var status = candidate.GetPlaybackInfo()?.PlaybackStatus;
            if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                session = candidate;
                playing = true;
                break;
            }
        }
        session ??= mgr.GetCurrentSession();
        Volatile.Write(ref _isPlaying, playing);

        if (session == null)
        {
            Current = null;
            _lastKey = "";
            _attempts = 0;
            return;
        }

        var props = await session.TryGetMediaPropertiesAsync();
        string key = $"{session.SourceAppUserModelId}|{props.Title}|{props.Artist}|{props.AlbumTitle}";

        if (key == _lastKey && (Current != null || _attempts >= 4)) return;
        if (key != _lastKey)
        {
            _lastKey = key;
            _attempts = 0;
        }
        _attempts++;

        var pair = await LoadColorsAsync(props);
        // Keep the previous colors while a new track's art is still loading.
        if (pair != null || _attempts >= 4) Current = pair;
    }

    static async Task<ColorPair?> LoadColorsAsync(GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        try
        {
            var thumb = props.Thumbnail;
            if (thumb == null) return null;

            using var ras = await thumb.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 64;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);

            return Extract(px, w, h);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Buckets pixels by hue, weighted by saturation * brightness (ignores dull/dark pixels),
    /// then picks the strongest bucket and the strongest clearly different one.
    /// </summary>
    static ColorPair Extract(byte[] px, int width, int height)
    {
        const int B = 12;
        var ws = new double[B];
        var rs = new double[B];
        var gs = new double[B];
        var bs = new double[B];
        double total = 0;

        for (int i = 0; i < width * height; i++)
        {
            int o = i * 4;
            if (px[o + 3] < 128) continue;
            double b = px[o] / 255.0, g = px[o + 1] / 255.0, r = px[o + 2] / 255.0;
            ColorUtil.RgbToHsv(r, g, b, out double hue, out double sat, out double val);
            if (sat < 0.25 || val < 0.25) continue;

            // the middle of the cover (the subject) counts more than the edges
            double cx = (i % width) / (double)width - 0.5;
            double cy = (i / width) / (double)height - 0.5;
            double centre = Math.Clamp(1.3 - Math.Sqrt(cx * cx + cy * cy) * 1.6, 0.5, 1.3);

            double wt = sat * val * centre;
            int k = (int)(hue / 360.0 * B) % B;
            ws[k] += wt; rs[k] += r * wt; gs[k] += g * wt; bs[k] += b * wt;
            total += wt;
        }

        if (total < width * height * 0.02)
            return new ColorPair(Color.FromRgb(235, 235, 255), Color.FromRgb(140, 160, 220),
                                 Color.FromRgb(255, 255, 255));

        int p = 0;
        for (int k = 1; k < B; k++) if (ws[k] > ws[p]) p = k;

        int q = -1;
        double bestQ = 0;
        for (int k = 0; k < B; k++)
        {
            if (k == p || HueDist(k, p, B) < 2) continue;
            if (ws[k] > bestQ) { bestQ = ws[k]; q = k; }
        }

        // accent = strongest bucket that differs from both primary and secondary
        int ai = -1;
        double bestAi = 0;
        for (int k = 0; k < B; k++)
        {
            if (k == p || k == q) continue;
            if (HueDist(k, p, B) < 2) continue;
            if (q >= 0 && HueDist(k, q, B) < 2) continue;
            if (ws[k] > bestAi) { bestAi = ws[k]; ai = k; }
        }

        Color primary = Vivid(rs[p] / ws[p], gs[p] / ws[p], bs[p] / ws[p]);

        Color secondary;
        if (q >= 0 && bestQ > ws[p] * 0.15)
        {
            secondary = Vivid(rs[q] / ws[q], gs[q] / ws[q], bs[q] / ws[q]);
        }
        else
        {
            ColorUtil.RgbToHsv(primary.R / 255.0, primary.G / 255.0, primary.B / 255.0,
                out double ph, out double ps, out double pv);
            secondary = ColorUtil.HsvToRgb(ph + 40, ps, pv);
        }

        Color accent;
        if (ai >= 0 && bestAi > ws[p] * 0.10)
        {
            accent = Vivid(rs[ai] / ws[ai], gs[ai] / ws[ai], bs[ai] / ws[ai]);
        }
        else
        {
            ColorUtil.RgbToHsv(primary.R / 255.0, primary.G / 255.0, primary.B / 255.0,
                out double ph, out double ps, out _);
            accent = ColorUtil.HsvToRgb(ph + 150, Math.Max(ps, 0.8), 1.0);
        }

        return new ColorPair(primary, secondary, accent);
    }

    static int HueDist(int a, int b, int n)
    {
        int d = Math.Abs(a - b);
        return Math.Min(d, n - d);
    }
    
    static Color Vivid(double r, double g, double b)
    {
        ColorUtil.RgbToHsv(r, g, b, out double h, out double s, out double v);
        return ColorUtil.HsvToRgb(h, Math.Max(s, 0.65), Math.Max(v, 0.9));
    }

    public void Dispose() => _cts.Cancel();
}
