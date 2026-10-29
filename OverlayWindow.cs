using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace EdgeGlow;

/// <summary>
/// A transparent, borderless, always-on-top, click-through window covering one monitor.
/// </summary>
public sealed class OverlayWindow : Window
{
    readonly Forms.Screen _screen;

    public OverlayWindow(Forms.Screen screen, Settings settings, AudioAnalyzer audio, MediaColorProvider media)
    {
        _screen = screen;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var b = screen.Bounds;
        Left = b.X; Top = b.Y; Width = b.Width; Height = b.Height;

        Content = new GlowVisual(settings, audio, media);

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex);
            ApplyBounds();
        };
        ContentRendered += (_, _) => ApplyBounds();
    }

    // Place the window using exact physical pixels so mixed-DPI monitors line up.
    void ApplyBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var b = _screen.Bounds;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, Native.SWP_NOACTIVATE);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(new Action(ApplyBounds));
    }
}
