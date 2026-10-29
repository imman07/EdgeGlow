using System;
using System.Collections.Generic;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace EdgeGlow;

/// <summary>Owns one overlay window per monitor and rebuilds them when displays change.</summary>
public sealed class OverlayManager : IDisposable
{
    readonly Settings _settings;
    readonly AudioAnalyzer _audio;
    readonly MediaColorProvider _media;
    readonly List<OverlayWindow> _windows = new();

    public OverlayManager(Settings settings, AudioAnalyzer audio, MediaColorProvider media)
    {
        _settings = settings;
        _audio = audio;
        _media = media;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    public void Rebuild()
    {
        foreach (var w in _windows) w.Close();
        _windows.Clear();

        foreach (var screen in Forms.Screen.AllScreens)
        {
            if (_settings.DisabledDisplays.Contains(screen.DeviceName)) continue;
            var win = new OverlayWindow(screen, _settings, _audio, _media);
            _windows.Add(win);
            win.Show();
        }
    }

    void OnDisplayChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(Rebuild));
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        foreach (var w in _windows) w.Close();
        _windows.Clear();
    }
}
