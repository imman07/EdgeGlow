using System;
using System.Threading;
using System.Windows;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace EdgeGlow;

public partial class App : System.Windows.Application
{
    Mutex? _mutex;
    Settings _settings = null!;
    AudioAnalyzer _audio = null!;
    MediaColorProvider _media = null!;
    OverlayManager _overlays = null!;
    Forms.NotifyIcon? _tray;
    Forms.ToolStripMenuItem? _enabledItem;
    SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "EdgeGlow.SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        _settings = Settings.Load();
        _audio = new AudioAnalyzer(_settings);
        _media = new MediaColorProvider();

        try
        {
            _audio.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not start audio capture:\n" + ex.Message +
                            "\n\nThe glow will still run, but it can't react to music.",
                            "EdgeGlow", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _media.Start();

        _overlays = new OverlayManager(_settings, _audio, _media);
        _overlays.Rebuild();

        BuildTray();
    }

    void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();

        _enabledItem = new Forms.ToolStripMenuItem("Enabled") { Checked = _settings.Enabled, CheckOnClick = true };
        _enabledItem.CheckedChanged += (_, _) =>
        {
            _settings.Enabled = _enabledItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(_enabledItem);

        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new Forms.NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "EdgeGlow",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowSettings();
    }

    void ShowSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings,
            () => Activity.IsPlaying(_media, _audio),
            () => _overlays.Rebuild());
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (_enabledItem != null) _enabledItem.Checked = _settings.Enabled;
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    void ExitApp()
    {
        if (_tray != null) _tray.Visible = false;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settings?.Save();
        _tray?.Dispose();
        _overlays?.Dispose();
        _media?.Dispose();
        _audio?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    static Drawing.Icon MakeIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var outer = new Drawing.Pen(Drawing.Color.FromArgb(124, 92, 255), 4);
            using var inner = new Drawing.Pen(Drawing.Color.FromArgb(0, 212, 255), 2);
            g.DrawEllipse(outer, 4, 4, 24, 24);
            g.DrawEllipse(inner, 10, 10, 12, 12);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }
}
