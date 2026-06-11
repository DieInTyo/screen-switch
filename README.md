# Screen Switch

A small Windows tray app for moving windows between two monitors.

## Features

- Left-clicking the tray icon runs the selected action: move the active window or move all windows.
- A tray menu can move a specific window, or several selected windows at once.
- Window size and position are preserved relative to the target monitor work area.
- Normal, maximized, minimized, and browser fullscreen windows are supported.
- Left-click action, notifications, hotkeys, language, and startup settings are saved between launches.
- Startup with Windows can be enabled directly from the tray menu.
- The app warns you if exactly two monitors are not connected.

## Languages

English is the default interface language. Russian is also available from the tray menu.

## Ready-Made EXE

You can use the ready-made `ScreenSwitch.exe` from the release archive. Download the zip, extract it, and run `ScreenSwitch.exe`.

After launch, the app appears in the Windows tray or hidden tray icons area.

## Build From Source

Requirements:

- Windows
- .NET SDK 8 or newer

Build a release executable:

```powershell
dotnet publish .\ScreenSwitch.csproj -c Release
```

After publishing, the ready-to-use `ScreenSwitch.exe` is copied to the project root.

For local development, run:

```powershell
dotnet run
```
