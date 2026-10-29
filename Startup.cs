using System;
using Microsoft.Win32;

namespace EdgeGlow;

/// <summary>Start-with-Windows via the HKCU Run key.</summary>
public static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "EdgeGlow";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) != null;
    }

    public static void Set(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (enable && Environment.ProcessPath != null)
                key.SetValue(ValueName, "\"" + Environment.ProcessPath + "\"");
            else
                key.DeleteValue(ValueName, false);
        }
        catch { /* non-fatal */ }
    }
}
