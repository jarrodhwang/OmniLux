# OmniLux

[Download Windows setup](https://github.com/jarrodhwang/OmniLux/releases/latest/download/OmniLux-Setup.exe) · [Portable builds and previous versions](https://github.com/jarrodhwang/OmniLux/releases)

[English](README.md) · [한국어](README.ko.md)

A compact Windows system-tray app for **real monitor brightness through DDC/CI**.
The device cards, sidebar, and Windows theme follow the design of
[OmniSound](https://github.com/jarrodhwang/OmniSound).

- Adjust each monitor independently, or all connected adjustable monitors together.
- Set a brightness countdown or a recurring daily time.
- Change brightness, pause timers, and refresh monitors from the tray menu.
- Start quietly in the tray when you sign in to Windows.
- Light, dark, and system appearance. Keyboard controls and accessible slider names.

![OmniLux dark appearance — demo displays](docs/images/monitors-dark.png)

The screenshot uses explicit demo mode. Actual connected monitors are discovered through Windows.

## Install and use

1. Download and run **OmniLux-Setup.exe**, or extract the entire portable ZIP and run **OmniLux.exe**.
2. Enable **DDC/CI** in your monitor's on-screen menu.
3. Move a monitor slider. **Brightness verified** means the monitor reported the requested value.
4. Open **Timers → Add timer**. Choose a display, brightness, and either a countdown or a daily local time.
5. In **Settings**, enable **Start OmniLux when I sign in to Windows** if wanted. It starts hidden in the tray.

Closing or minimizing the window leaves OmniLux and its timers running. Left-click the tray icon
to reopen the window; right-click for brightness presets and **Exit OmniLux**.
Startup is per user and requires no administrator privileges. Windows may place the tray icon in
its hidden-icons area.

The all-display control shows **Mixed** when monitors report different brightness values.
Percentages are normalized to each monitor's hardware range; matching percentages can still look
different on different panels. Settings are saved automatically.

## Timer behavior

- Countdowns accept 1–10080 minutes and run once. Enable a completed countdown to start it again.
- Daily timers use your Windows local time in 24-hour format. Timers are checked every five seconds.
- After sleep, restart, or a pause, the latest missed setting applies to each available target.
  Older missed settings are skipped so displays do not cycle through them.
- **All adjustable displays** includes the displays connected when the timer runs. A timer for a
  specific disconnected display stays pending until that display is available again.
- Editing a timer enables it and starts its countdown again. Removing a timer cancels future runs.
- Timers run while OmniLux is running in your user session. They do not wake the computer or run
  before Windows sign-in.
- Occurrences are saved before sending commands, to prevent repeated writes after a crash.
  A crash during a run can leave that run incomplete; its state says **Check last run**.
  Failed or unverified runs are shown explicitly and are not retried continuously. Re-enable or edit
  a countdown to retry, or use a slider.

## Compatibility

Windows 11 / Windows 10 22H2 x64, and a monitor plus connection that supports DDC/CI brightness.
The self-contained package includes .NET 10; no runtime download, driver, service, or web connection
is required to run OmniLux. Windows 11 x64 has been tested; Windows 10 and ARM64 have not.

Some laptop panels, HDR modes, adapters, docks, and remote sessions do not expose DDC/CI.
Unsupported displays stay visible with their status. OmniLux does not substitute a gamma overlay
for hardware brightness. Try a direct HDMI/DisplayPort connection, enable DDC/CI, and refresh.
Readback proves the monitor's reported brightness setting; panel luminance is not measured.

This initial release is **unsigned**. The app and installer do not have a publisher certificate.
Published assets include SHA-256 checksums.

## Build and validation

Install the .NET 10 SDK on Windows, then run:

```powershell
.\tools\Build.ps1 -Test
.\tools\Package.ps1
.\tools\Restore-InstallerTools.ps1
.\tools\Build-Installer.ps1
```

`Build.ps1 -Test` runs 30 core regression tests and native WPF UI checks in demo mode.
Demo mode never sends commands to hardware. To validate your own connected monitors:

```powershell
.\tools\Test-App.ps1 -Hardware -Startup
```

`-Hardware` changes each adjustable monitor by one native brightness unit, reads it back,
then restores and verifies the exact original native value. `-Startup` enables/disables the
HKCU startup value and restores its previous state. Receipts and UI screenshots go under
the ignored `build/validation/` directory. Hardware tests are separate from synthetic tests.

Useful app arguments:

```text
--tray                       Start without opening a window
--exit                       Ask the existing instance to exit
--demo                       Isolated sample displays; no monitor API calls
--probe <output.json>        Read-only native monitor inventory
--smoke <output-directory>   Isolated UI checks and screenshots, then exit
--hardware-test <output.json>  Explicit change/readback/restore diagnostic
--startup-test <output.json>   Explicit registry round-trip diagnostic
```

Data is stored locally at `%LocalAppData%\OmniLux\settings.json`. The previous settings file is
kept as `settings.json.bak`; corrupt or newer-version files are preserved until you explicitly
reset them. Activity is recorded locally in a size-limited `activity.jsonl` file. Uninstalling
removes the app, shortcuts, and startup entry and keeps personal settings. There is no telemetry
or automatic update service.

See [architecture and quality trade-offs](docs/ARCHITECTURE.md) and
[validation scope](docs/VALIDATION.md). Original code and artwork are [MIT licensed](LICENSE);
bundled runtime components retain their [upstream terms](THIRD_PARTY_NOTICES.md).
