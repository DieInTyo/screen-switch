# Screen Switch

Screen Switch is a small Windows tray app for moving windows between two monitors.

## Features

- Move the active window, all windows, or selected windows to the other monitor.
- Use the tray menu or configurable global hotkeys.
- Open a "Move window" picker with table and tile views, app icons, monitor indicators, window state, multi-select, and double-click moving.
- Use the compact overlay for quick window moves, drag-to-move between monitor zones, and actions for active/all/minimize.
- Minimize all visible movable windows.
- Preserve window size and position relative to the target monitor work area.
- Handle normal, maximized, minimized, and browser fullscreen windows.
- Switch between English and Russian UI.
- Switch between light and dark themes.
- Configure notifications, startup with Windows, overlay position, overlay opacity, and draggable overlay mode.
- Warn when exactly two monitors are not connected.

## Languages

English is the default interface language. Russian is also available from the tray menu.

## Ready-Made EXE

You can use the ready-made `ScreenSwitch.exe` from the release archive:

1. Download the release zip.
2. Extract it anywhere you like.
3. Run `ScreenSwitch.exe`.

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
