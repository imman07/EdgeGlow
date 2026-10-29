@echo off
setlocal
cd /d "%~dp0"
title EdgeGlow installer

set "OUT=%LocalAppData%\Programs\EdgeGlow"

echo.
echo  Building EdgeGlow (first time takes about a minute)...
echo.

taskkill /f /im EdgeGlow.exe >nul 2>&1

dotnet publish EdgeGlow.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "%OUT%"
if errorlevel 1 (
    echo.
    echo  Build failed. Make sure the .NET 8 SDK is installed:
    echo  https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

echo.
echo  Creating shortcuts...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=New-Object -ComObject WScript.Shell; foreach($p in @([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('Programs'))){ $l=$s.CreateShortcut((Join-Path $p 'EdgeGlow.lnk')); $l.TargetPath='%OUT%\EdgeGlow.exe'; $l.WorkingDirectory='%OUT%'; $l.Save() }"

start "" "%OUT%\EdgeGlow.exe"

echo.
echo  Done! EdgeGlow is running (look for its tray icon).
echo  A shortcut was added to your Desktop and Start menu.
echo  Turn on "Start with Windows" in Settings to launch it automatically.
echo.
pause
