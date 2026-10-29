using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace EdgeGlow;

public partial class SettingsWindow : Window
{
    readonly Settings _s;
    readonly Func<bool> _isActive;
    readonly Action _displaysChanged;
    readonly DispatcherTimer _timer;
    bool _loading = true;

    public SettingsWindow(Settings settings, Func<bool> isActive, Action displaysChanged)
    {
        _s = settings;
        _isActive = isActive;
        _displaysChanged = displaysChanged;

        InitializeComponent();
        ApplyAccent();
        MaxHeight = SystemParameters.WorkArea.Height - 20;

        OneRadio.IsChecked = _s.Gradient == GradientMode.OneColor;
        TwoRadio.IsChecked = _s.Gradient == GradientMode.TwoColors;
        MusicRadio.IsChecked = _s.Mode == GlowMode.MusicSync;
        SnakeRadio.IsChecked = _s.Mode == GlowMode.Snake;
        StaticRadio.IsChecked = _s.Mode == GlowMode.Static;
        AuroraRadio.IsChecked = _s.Mode == GlowMode.Aurora;
        ChaseRadio.IsChecked = _s.Mode == GlowMode.Chase;
        SparkleRadio.IsChecked = _s.Mode == GlowMode.Sparkle;
        OverrideBox.IsChecked = !_s.UseAlbumColors;
        ThicknessSlider.Value = _s.Thickness;
        GlowSlider.Value = _s.Glow;
        LengthSlider.Value = _s.SnakeLength;
        PrimaryBright.Value = _s.PrimaryBrightness;
        SecondaryBright.Value = _s.SecondaryBrightness;
        StartupBox.IsChecked = Startup.IsEnabled();

        LoadWheel(PrimaryWheel, _s.PrimaryColor, _s.PrimaryBrightness);
        LoadWheel(SecondaryWheel, _s.SecondaryColor, _s.SecondaryBrightness);
        PrimaryWheel.ColorChanged += (_, _) =>
            _s.PrimaryColor = WheelHex(PrimaryWheel);
        SecondaryWheel.ColorChanged += (_, _) =>
            _s.SecondaryColor = WheelHex(SecondaryWheel);

        BuildDisplays();
        UpdateUi();
        _loading = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => UpdateBadge();
        _timer.Start();
        UpdateBadge();

        Closed += (_, _) =>
        {
            _timer.Stop();
            _s.Save();
        };
    }

    // ----- Windows accent colour ---------------------------------------------

    void ApplyAccent()
    {
        // Use the user's Windows accent colour, lightened a little for dark backgrounds.
        var c = SystemParameters.WindowGlassColor;
        double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        if (lum < 0.55)
        {
            const double k = 0.35;
            c = Color.FromRgb(
                (byte)(c.R + (255 - c.R) * k),
                (byte)(c.G + (255 - c.G) * k),
                (byte)(c.B + (255 - c.B) * k));
            lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        }
        Resources["Accent"] = new SolidColorBrush(c);
        Resources["OnAccent"] = new SolidColorBrush(lum > 0.55 ? Colors.Black : Colors.White);
    }

    Brush Accent() => (Brush)FindResource("Accent");

    // ----- title bar -----------------------------------------------------------

    void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    void OnCloseClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    void OnMinClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        WindowState = WindowState.Minimized;
    }

    // ----- settings --------------------------------------------------------------

    void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _s.Gradient = OneRadio.IsChecked == true ? GradientMode.OneColor : GradientMode.TwoColors;
        _s.Mode = MusicRadio.IsChecked == true ? GlowMode.MusicSync
                : SnakeRadio.IsChecked == true ? GlowMode.Snake
                : AuroraRadio.IsChecked == true ? GlowMode.Aurora
                : ChaseRadio.IsChecked == true ? GlowMode.Chase
                : SparkleRadio.IsChecked == true ? GlowMode.Sparkle
                : GlowMode.Static;
        _s.UseAlbumColors = OverrideBox.IsChecked != true;
        _s.Thickness = ThicknessSlider.Value;
        _s.Glow = GlowSlider.Value;
        _s.SnakeLength = LengthSlider.Value;
        _s.PrimaryBrightness = PrimaryBright.Value;
        _s.SecondaryBrightness = SecondaryBright.Value;
        PrimaryWheel.Brightness = PrimaryBright.Value;
        SecondaryWheel.Brightness = SecondaryBright.Value;

        if (ReferenceEquals(sender, StartupBox))
            Startup.Set(StartupBox.IsChecked == true);

        UpdateUi();
    }

    void UpdateUi()
    {
        ThicknessValue.Text = $"{ThicknessSlider.Value:0.#}";
        GlowValue.Text = $"{GlowSlider.Value:0}";
        LengthValue.Text = $"{LengthSlider.Value:P0}";

        bool custom = OverrideBox.IsChecked == true;
        WheelsPanel.Opacity = custom ? 1.0 : 0.4;
        WheelsPanel.IsHitTestVisible = custom;
    }

    void UpdateBadge()
    {
        bool on = _isActive();
        ActiveText.Text = on ? "Active" : "Idle";
        ActiveText.Foreground = Brush(on ? "#6CCB5F" : "#9D9D9D");
        ActiveBadge.Background = Brush(on ? "#1B3A24" : "#2B2B2B");
        ActiveBadge.BorderBrush = Brush(on ? "#2E6B3E" : "#3A3A3A");
    }

    // ----- colour wheels -----------------------------------------------------------

    static void LoadWheel(ColorWheel wheel, string hex, double brightness)
    {
        var c = ColorUtil.Parse(hex, Colors.White);
        ColorUtil.RgbToHsv(c.R / 255.0, c.G / 255.0, c.B / 255.0, out double h, out double s, out _);
        wheel.SetHueSat(h, s);
        wheel.Brightness = brightness;
    }

    static string WheelHex(ColorWheel wheel)
        => ColorUtil.ToHex(ColorUtil.HsvToRgb(wheel.Hue, wheel.Saturation, 1.0));

    // ----- displays ---------------------------------------------------------------------

    void BuildDisplays()
    {
        DisplayList.Children.Clear();
        int n = 1;
        foreach (var screen in Forms.Screen.AllScreens)
        {
            string id = screen.DeviceName;
            bool on = !_s.DisabledDisplays.Contains(id);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = "\uE7F4",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Foreground = on ? Accent() : Brush("#9D9D9D"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            };

            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textPanel.Children.Add(new TextBlock
            {
                Text = $"Display {n}{(screen.Primary ? " (Primary)" : "")}",
                Foreground = Brushes.White
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = $"{screen.Bounds.Width} \u00D7 {screen.Bounds.Height}",
                Foreground = Brush("#A0A0A0"),
                FontSize = 12
            });

            var check = new TextBlock
            {
                Text = "\uE73E",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = Accent(),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = on ? Visibility.Visible : Visibility.Hidden
            };
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(textPanel, 1);
            Grid.SetColumn(check, 2);
            grid.Children.Add(icon);
            grid.Children.Add(textPanel);
            grid.Children.Add(check);

            var card = new Border
            {
                Child = grid,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = on ? Accent() : Brush("#363636"),
                Background = Brush("#2B2B2B"),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };
            card.MouseLeftButtonUp += (_, _) =>
            {
                if (_s.DisabledDisplays.Contains(id)) _s.DisabledDisplays.Remove(id);
                else _s.DisabledDisplays.Add(id);
                _displaysChanged();
                BuildDisplays();
            };
            DisplayList.Children.Add(card);
            n++;
        }
    }

    static SolidColorBrush Brush(string hex)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
}
