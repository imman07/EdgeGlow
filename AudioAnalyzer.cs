using System;
using System.Collections.Generic;
using System.Diagnostics;
using NAudio.Wave;

namespace EdgeGlow;

/// <summary>
/// Captures whatever the PC is playing (WASAPI loopback, never the microphone),
/// computes a smoothed loudness level and a beat pulse from the bass band.
/// </summary>
public sealed class AudioAnalyzer : IDisposable
{
    const int FftSize = 1024;

    readonly Settings _settings;
    readonly float[] _window = new float[FftSize];
    readonly float[] _re = new float[FftSize];
    readonly float[] _im = new float[FftSize];
    readonly float[] _acc = new float[FftSize];
    readonly Queue<double> _bassHistory = new();
    readonly object _lock = new();

    WasapiLoopbackCapture? _capture;
    int _accCount;
    int _sampleRate = 48000;

    double _bassPeak = 1e-9;
    double _rmsPeak = 0.01;
    double _bassSum;

    double _level;
    double _midPeak = 1e-9, _treblePeak = 1e-9;
    double _bandBass, _bandMid, _bandTreble;
    double _bass;
    double _beat;
    long _lastFrameTicks;
    long _lastBeatTicks;
    long _activeSinceTicks;

    public AudioAnalyzer(Settings settings)
    {
        _settings = settings;
        for (int i = 0; i < FftSize; i++)
            _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
    }

    public void Start()
    {
        _capture = new WasapiLoopbackCapture();
        _sampleRate = _capture.WaveFormat.SampleRate;
        _capture.DataAvailable += OnData;
        _capture.StartRecording();
    }

    /// <summary>
    /// Current smoothed values. Loopback capture delivers no data during silence,
    /// so values are decayed here based on time since the last audio frame.
    /// </summary>
    /// 
    public double Bass()
    {
    lock (_lock)
    {
        if (_lastFrameTicks == 0) return 0;
        double dt = (Stopwatch.GetTimestamp() - _lastFrameTicks) / (double)Stopwatch.Frequency;
        double idle = Math.Max(0, dt - 0.05);
        return _bass * Math.Exp(-idle / 0.2);
    }
    }

    public (double bass, double mid, double treble) Bands()
    {
        lock (_lock)
        {
            if (_lastFrameTicks == 0) return (0, 0, 0);
            double dt = (Stopwatch.GetTimestamp() - _lastFrameTicks) / (double)Stopwatch.Frequency;
            double f = Math.Exp(-Math.Max(0, dt - 0.05) / 0.2);
            return (_bandBass * f, _bandMid * f, _bandTreble * f);
        }
    }

    public (double level, double beat) Snapshot()
    {
        lock (_lock)
        {
            if (_lastFrameTicks == 0) return (0, 0);
            double dt = (Stopwatch.GetTimestamp() - _lastFrameTicks) / (double)Stopwatch.Frequency;
            double idle = Math.Max(0, dt - 0.05);
            return (_level * Math.Exp(-idle / 0.25), _beat * Math.Exp(-idle / 0.12));
        }
    }

    /// <summary>True when audible sound has been playing continuously for a moment.</summary>
    public bool IsAudioActive()
    {
        lock (_lock)
        {
            if (_lastFrameTicks == 0 || _activeSinceTicks == 0) return false;
            long now = Stopwatch.GetTimestamp();
            double sinceFrame = (now - _lastFrameTicks) / (double)Stopwatch.Frequency;
            double activeFor = (now - _activeSinceTicks) / (double)Stopwatch.Frequency;
            return sinceFrame < 0.3 && activeFor > 0.8;
        }
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        if (_capture == null) return;
        var fmt = _capture.WaveFormat;
        int ch = Math.Max(1, fmt.Channels);
        int bits = fmt.BitsPerSample;
        int bytesPer = bits / 8;
        if (bytesPer == 0) return;
        int frames = e.BytesRecorded / (bytesPer * ch);

        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < ch; c++)
            {
                int idx = (f * ch + c) * bytesPer;
                float v = bits == 32 ? BitConverter.ToSingle(e.Buffer, idx)
                        : bits == 16 ? BitConverter.ToInt16(e.Buffer, idx) / 32768f
                        : 0f;
                sum += v;
            }
            _acc[_accCount++] = sum / ch;
            if (_accCount == FftSize)
            {
                ProcessFrame();
                _accCount = 0;
            }
        }
    }

    void ProcessFrame()
    {
        double sumSq = 0;
        for (int i = 0; i < FftSize; i++)
        {
            float s = _acc[i];
            sumSq += s * s;
            _re[i] = s * _window[i];
            _im[i] = 0;
        }
        double rms = Math.Sqrt(sumSq / FftSize);

        Fft(_re, _im);

        double binHz = (double)_sampleRate / FftSize;
        int lo = Math.Max(1, (int)Math.Floor(30 / binHz));
        int hi = Math.Max(lo, (int)Math.Ceiling(150 / binHz));
        double bass = 0;
        for (int k = lo; k <= hi; k++)
            bass += _re[k] * _re[k] + _im[k] * _im[k];

        // Auto-gain: slowly decaying peak trackers.
        _rmsPeak = Math.Max(rms, _rmsPeak * 0.9995);
        _bassPeak = Math.Max(bass, _bassPeak * 0.999);
        int midLo = hi + 1;
        int midHi = Math.Min(FftSize / 2 - 1, (int)(2000 / binHz));
        int trHi = Math.Min(FftSize / 2 - 1, (int)(8000 / binHz));
        double mid = 0, treble = 0;
        for (int k = midLo; k <= midHi; k++) mid += _re[k] * _re[k] + _im[k] * _im[k];
        for (int k = midHi + 1; k <= trHi; k++) treble += _re[k] * _re[k] + _im[k] * _im[k];
        _midPeak = Math.Max(mid, _midPeak * 0.998);
        _treblePeak = Math.Max(treble, _treblePeak * 0.998);
        double bN = Math.Pow(Math.Min(1.0, bass / Math.Max(_bassPeak, 1e-9)), 0.4);
        double mN = Math.Sqrt(Math.Min(1.0, mid / Math.Max(_midPeak, 1e-9)));
        double tN = Math.Sqrt(Math.Min(1.0, treble / Math.Max(_treblePeak, 1e-9)));

        double rawLevel = Math.Min(1.0, rms / Math.Max(_rmsPeak, 0.01));
        rawLevel = Math.Pow(rawLevel, 1.4);
        double bassNorm = Math.Sqrt(Math.Min(1.0, bass / Math.Max(_bassPeak, 1e-9)));

        // Beat detection: bass energy well above its ~1 second running average.
        double avg = _bassHistory.Count > 0 ? _bassSum / _bassHistory.Count : 0;
        double threshold = 1.15 + (1.0 - Math.Clamp(_settings.Sensitivity, 0, 1)) * 0.4;
        long now = Stopwatch.GetTimestamp();
        double sinceBeat = (now - _lastBeatTicks) / (double)Stopwatch.Frequency;
        bool isBeat = _bassHistory.Count >= 10
                      && bass > avg * threshold
                      && bass > _bassPeak * 0.10
                      && rms > 0.003
                      && sinceBeat > 0.14;

        _bassHistory.Enqueue(bass);
        _bassSum += bass;
        if (_bassHistory.Count > 43) _bassSum -= _bassHistory.Dequeue();

        lock (_lock)
        {
            _bandBass = bN > _bandBass ? _bandBass + (bN - _bandBass) * 0.9 : _bandBass * 0.78;
            _bandMid = mN > _bandMid ? _bandMid + (mN - _bandMid) * 0.7 : _bandMid * 0.85;
            _bandTreble = tN > _bandTreble ? _bandTreble + (tN - _bandTreble) * 0.7 : _bandTreble * 0.85;
            _level = rawLevel > _level ? _level + (rawLevel - _level) * 0.7 : _level * 0.82;
            _bass = bassNorm > _bass ? _bass + (bassNorm - _bass) * 0.8 : _bass * 0.80;
            _beat = isBeat ? 1.0 : _beat * 0.86;
            if (isBeat) _lastBeatTicks = now;
            _lastFrameTicks = now;
            if (rms > 0.002)
            {
                if (_activeSinceTicks == 0) _activeSinceTicks = now;
            }
            else
            {
                _activeSinceTicks = 0;
            }
        }
    }

    // In-place iterative radix-2 FFT.
    static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            float wlr = (float)Math.Cos(ang), wli = (float)Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float wr = 1, wi = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int u = i + k, v = i + k + len / 2;
                    float xr = re[v] * wr - im[v] * wi;
                    float xi = re[v] * wi + im[v] * wr;
                    re[v] = re[u] - xr; im[v] = im[u] - xi;
                    re[u] += xr; im[u] += xi;
                    float nwr = wr * wlr - wi * wli;
                    wi = wr * wli + wi * wlr;
                    wr = nwr;
                }
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnData;
                _capture.StopRecording();
                _capture.Dispose();
            }
        }
        catch { /* ignore */ }
        _capture = null;
    }
}
