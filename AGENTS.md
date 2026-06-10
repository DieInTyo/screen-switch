# Agent Notes

- Before running `dotnet publish .\ScreenSwitch.csproj -c Release`, check whether `ScreenSwitch.exe` is already running. If it is running from this workspace, stop that process first so the publish target can replace the root `ScreenSwitch.exe`; after a successful publish, start the fresh executable again if the app should remain running for manual testing.
