using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EdgeGlow;

// Numeric values are kept stable so older settings.json files still load.
public enum GlowMode
{
    MusicSync = 0,
    Static = 1,
    Snake = 2,
    Aurora = 3,
    Chase = 4,
    Sparkle = 6
}

public enum GradientMode
{
    OneColor = 0,
    TwoColors = 1
}

public sealed class Settings
{
    public bool Enabled { get; set; } = true;
    public GlowMode Mode { get; set; } = GlowMode.Snake;
    public GradientMode Gradient { get; set; } = GradientMode.TwoColors;

    /// <summary>true = use colors from album art; false = use the custom colors below.</summary>
    public bool UseAlbumColors { get; set; } = true;

    /// <summary>Hue/saturation of each custom color, stored as a full-brightness hex.</summary>
    public string PrimaryColor { get; set; } = "#7C5CFF";
    public string SecondaryColor { get; set; } = "#00D4FF";
    public double PrimaryBrightness { get; set; } = 1.0;
    public double SecondaryBrightness { get; set; } = 1.0;

    /// <summary>Core line thickness in DIPs.</summary>
    public double Thickness { get; set; } = 3;

    /// <summary>How far the glow spills inward, in DIPs.</summary>
    public double Glow { get; set; } = 50;

    /// <summary>Overall brightness 0..1.</summary>
    public double Brightness { get; set; } = 1.0;

    /// <summary>Beat sensitivity 0..1.</summary>
    public double Sensitivity { get; set; } = 0.5;

    /// <summary>Snake length as a fraction of the screen perimeter.</summary>
    public double SnakeLength { get; set; } = 0.30;

    /// <summary>Monitors (Screen.DeviceName) where the glow is turned off.</summary>
    public List<string> DisabledDisplays { get; set; } = new();

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EdgeGlow", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    s.DisabledDisplays ??= new List<string>();
                    return s;
                }
            }
        }
        catch { /* fall through to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }
}
