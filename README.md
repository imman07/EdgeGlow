# EdgeGlow

Music-reactive ambient edge lighting for Windows 10/11.
Captures system audio (WASAPI loopback — never the microphone), detects beats,
reads album-art colors from the current media session, and draws a glowing border
on every monitor via transparent, click-through overlay windows.

## Requirements
- Windows 10 (build 19041+) or Windows 11
- .NET 8 SDK  (https://dotnet.microsoft.com/download)
- Visual Studio 2022 (optional) with the ".NET desktop development" workload

## Install (no terminal needed)
Double-click **Install.bat**. It builds the app, installs it to
`%LocalAppData%\Programs\EdgeGlow`, adds Desktop and Start-menu shortcuts and launches it.
Run it again any time after changing the code to update the installed app.

After that, just open EdgeGlow from the shortcut. Turn on "Start with Windows" in Settings
and it launches automatically at login.

(Developers can still use `dotnet run -c Release` or open `EdgeGlow.csproj` in Visual Studio.)

## Use
Play music (Spotify, YouTube in a browser, Apple Music, etc.). Use the tray icon
(bottom-right, may be under the ^ arrow) to open Settings or exit.

## How it works
| File | Job |
|---|---|
| `AudioAnalyzer.cs` | Loopback capture → FFT → loudness level + bass-band beat detection |
| `MediaColorProvider.cs` | Windows media session → album art → two dominant vivid colors |
| `GlowVisual.cs` | Snake / border glow rendering, play-only fade, color weighting |
| `ColorWheel.cs` | HSV color wheel control used in Settings |
| `Activity.cs` | Defines "a song is playing" (media session or sustained audio) |
| `OverlayWindow.cs` | Transparent, topmost, click-through window per monitor |
| `OverlayManager.cs` | Creates/rebuilds overlays when monitors change |
| `SettingsWindow.*` | Live settings UI; saved to `%AppData%\EdgeGlow\settings.json` |

## Known limits
- Exclusive-fullscreen games hide the overlay (borderless windowed is fine).
- Windows doesn't allow third-party apps on the lock screen.
- The loopback follows the default output device at launch; restart after switching.
- Software-rendered transparent windows: if CPU use is high at 4K, lower Glow size.

## Ideas for next steps
- Per-monitor selection, rounded corners, more animation styles
- Move rendering to Direct2D/Win2D for lower CPU use
- Re-open loopback when the default audio device changes
