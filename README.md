# MouseBat

**A tiny Windows taskbar battery indicator made for the ASUS ROG Gladius III Wireless.**

MouseBat reads the mouse's battery directly from its ASUS USB HID interface and puts a mouse glyph and percentage in the Windows notification area. At **20% or below**, the percentage turns red. Charging is marked with a blue dot.

It runs quietly in the background, checks once a minute, and can start when you sign in. It does not need Armoury Crate, a driver, an account, or an internet connection while running. Right-click the taskbar icon to refresh, toggle startup, or exit.

## Install

Download **MouseBat-Setup** from [GitHub Releases](https://github.com/jake-378/MouseBat/releases/latest), run it, and leave “Start MouseBat when I sign in” selected if you want it to launch at startup. The installer is per-user and does not need administrator rights. Windows may show a SmartScreen warning because the setup is not code-signed.

## Device and battery behavior

- Built specifically for **ROG Gladius III Wireless** (USB VID `0B05`, PID `197F`).
- Reads the battery from the mouse over its 2.4 GHz USB receiver or USB cable.
- Polls every 60 seconds; use **Refresh now** in the tray menu for an immediate reading.
- A sleeping mouse may not answer. MouseBat keeps the last reading and marks the icon as unavailable until the mouse wakes; it does not mistake the mouse's sleep response for 0% battery.
- Battery percentages come from the mouse firmware and can update in steps.

The ASUS HID request was implemented independently from published protocol descriptions and G-Helper's public battery query. MouseBat contains no code copied from Wireless Device Battery Monitor or other battery-monitor applications.

## Build from source

Requires the .NET 10 SDK to build. MouseBat targets .NET Framework 4.8, included with current Windows 10 and Windows 11 installations; the setup does not bundle a runtime. From a PowerShell or Developer PowerShell prompt:

```powershell
dotnet build .\MouseBat.csproj -c Release
New-Item -ItemType Directory -Force .\publish | Out-Null
Copy-Item .\bin\Release\net48\* .\publish -Recurse -Force
```

To build the installer, install Inno Setup 6 and run:

```powershell
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" .\installer\MouseBat.iss
```

Pushing a `v*` tag builds and publishes a GitHub Release with the setup executable.

## License

MouseBat is released under the MIT License. See [LICENSE](LICENSE).
