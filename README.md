# OmniLux 0.2

[Download Windows setup](https://github.com/jarrodhwang/OmniLux/releases/latest/download/OmniLux-Setup.exe) · [Portable builds and previous versions](https://github.com/jarrodhwang/OmniLux/releases) · [What's new](CHANGELOG.md)

[English](README.md) · [한국어](README.ko.md)

A compact Windows tray app for **real monitor brightness and contrast through DDC/CI**.
The device cards, sidebar, and Windows theme follow [OmniSound](https://github.com/jarrodhwang/OmniSound).

- Left-click the tray icon for per-monitor brightness and contrast sliders, without opening the main window.
- Adjust each monitor independently or all supported displays together.
- Drag smoothly: values preview locally; release or leave the slider to apply the final value.
- Set a brightness countdown or daily schedule; pause timers from the tray.
- Start quietly in the tray at Windows sign-in. System, light, and dark appearance.

![OmniLux tray controls — demo displays](docs/images/tray-dark.png)

Documentation screenshots use explicit demo mode. Actual displays are discovered through Windows.

## Install and use

1. Run **OmniLux-Setup.exe**, or extract the entire portable ZIP and run **OmniLux.exe**.
2. Enable **DDC/CI** in your monitor's on-screen menu.
3. Left-click the tray icon. Drag a monitor's brightness or contrast slider and release it.
4. Use the panel's open-window button, or right-click the tray icon → **Open OmniLux**, for timers and settings.
5. Enable **Settings → Start OmniLux when I sign in to Windows** if wanted.

While holding a slider, OmniLux sends no intermediate DDC/CI writes. Releasing the mouse commits
the final value; leaving the slider also commits a pending preview when you are no longer holding
the mouse. Keyboard arrow/page/home/end adjustments apply on key release. The small check button
also applies a preview, including changes made by accessibility tools. **Escape** in the tray panel
cancels uncommitted previews and dismisses the panel. Clicking elsewhere dismisses it and commits
remaining previews. Preset buttons apply immediately.

**Brightness verified** or **Contrast verified** means the monitor reported the requested hardware
value. Unsupported controls show **Unavailable** independently; a monitor can support brightness
without contrast. **Mixed** means supported monitors currently report different values. Percentages
map to each monitor's native range and do not guarantee equal perceived luminance across panels.

Closing or minimizing the main window keeps OmniLux and its timers running. Use **Exit OmniLux**
to stop it. Windows may place the icon in its hidden-icons area. Startup is per user and needs no
administrator privileges. Upgrades keep the install path, timer settings, and current startup
preference, including changes made in the app since the previous installation.

![OmniLux main window — demo displays](docs/images/monitors-dark.png)

## Brightness timers

- Countdowns accept 1–10080 minutes and run once. Enable a completed countdown to start it again.
- Daily timers use Windows local time in 24-hour format and are checked every five seconds.
- After sleep, restart, or a pause, only the latest missed setting applies to each available target.
- **All adjustable displays** includes brightness-capable monitors connected when the timer runs.
  A timer for a specific disconnected display stays pending until that display is available.
- Editing a timer enables it and restarts its countdown. Removing it cancels future runs.
- Timers run while OmniLux is running in your user session. They do not wake the PC or run before sign-in.
- Occurrences are saved before commands to prevent repeated writes after a crash. A partially completed
  run says **Check last run**. Failed and unverified runs are explicit and are not retried continuously.

Timers continue to schedule brightness; contrast is controlled manually in this version.

## Compatibility and distribution

Windows 11 / Windows 10 22H2 x64 and a monitor plus connection supporting DDC/CI.
The self-contained ZIP and per-user installer include .NET 10. No runtime download, driver,
privileged service, or internet connection is required to run the app. Windows 11 x64 has been
tested; Windows 10 and ARM64 have not.

Some laptop panels, HDR modes, picture presets, adapters, docks, and remote sessions do not expose
one or both controls. Try a direct HDMI/DisplayPort connection, enable DDC/CI, and refresh.
OmniLux does not substitute a gamma overlay. Hardware readback verifies the reported setting,
not measured panel luminance.

Current downloadable binaries are **unsigned**; no publisher certificate is bundled or claimed.
Each release includes SHA-256 checksums for its setup and portable ZIP. See the
[release procedure](docs/RELEASE.md) for validation and signing preparation.

## Build and validation

Install the .NET 10 SDK on Windows:

```powershell
.\tools\Build.ps1 -Test
.\tools\Package.ps1 -SkipTests
.\tools\Restore-InstallerTools.ps1
.\tools\Build-Installer.ps1
```

The build runs **35 core regressions** and **34 WPF demo UI checks**, including zero writes during
a held drag, one final write per target, contrast support/failure handling, and tray-panel controls
without the main window. Demo mode never calls a monitor API.

```powershell
# Native UI and a one-unit change/readback/exact restoration for each supported control:
.\tools\Test-App.ps1 -Hardware -Startup
# Upgrade an existing installation; preserve settings and restore the original startup state:
.\tools\Test-Upgrade.ps1
# Fresh install/update/uninstall on a clean Windows user profile:
.\tools\Test-Installer.ps1
```

`-Hardware` tests brightness and contrast independently and restores each original raw value in a
finally block. `-Startup` enables/disables the startup registry entry and restores its previous
state. JSON receipts and screenshots stay under ignored `build/validation/`. CI builds, runs core
regressions, packages the app, and runs installer/UI checks when the runner has an interactive desktop.

Arguments:

```text
--tray                        Start without opening the main window
--quick-controls              Show the tray panel, including for an existing instance
--exit                        Ask the existing instance to exit
--demo                        Isolated sample displays; no native calls
--probe <output.json>         Read-only brightness/contrast inventory
--smoke <output-directory>    Isolated native UI checks and screenshots, then exit
--hardware-test <output.json> Explicit supported-control change/readback/restore diagnostic
--startup-test <output.json>  Explicit registry round-trip diagnostic
```

Settings and bounded local activity logs are stored at `%LocalAppData%\OmniLux\`. Settings retain
the previous file as `settings.json.bak`; unreadable or newer-schema files remain intact until an
explicit reset. Version 0.1 settings remain compatible. Uninstall removes the app, shortcuts, and
startup entry and preserves personal settings. There is no telemetry or automatic update service.

See [architecture and quality trade-offs](docs/ARCHITECTURE.md) and [validation scope](docs/VALIDATION.md).
Original code and artwork are [MIT licensed](LICENSE); runtime components retain their
[upstream terms](THIRD_PARTY_NOTICES.md).
