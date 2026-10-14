# Validation scope

Local validation on **2026-10-14 (UTC)**, Windows 11 Pro x64 (build 26200), .NET SDK 10.0.401.
Keep synthetic, native UI, registry, and physical monitor evidence separate.

| Layer | Checks and evidence |
| --- | --- |
| Build | Release builds with warnings treated as errors. No external NuGet dependencies. |
| Core regression | 30 tests cover native ranges, input bounds, countdown/daily planning, missed occurrences, targeting, disconnected displays, DST gaps/repeats, settings corruption/schema/backup, serialization, canceled queued requests, driver failures, and disposal. |
| Demo UI | 19 checks exercise real WPF windows and controls, simulated slider write/readback, all-display control, countdown engine completion, timer editor validation, navigation, theme screenshots, persistence, close/minimize-to-tray, restore, and minimum window size. Demo mode never calls DDC/CI. |
| Native UI | 15 checks use actual display discovery. Windows computer use also exercised a visible slider, observed verified brightness, and restored the original value. |
| Physical DDC/CI | DELL S2725DS over DisplayPort: 75 → 76 → 75; Alienware AW2521HFL: 60 → 61 → 60; DELL S2725DS over HDMI: 75 → 76 → 75. Changes and exact restoration were confirmed by native monitor readback. |
| Startup | Enable, disable, quoted executable command, and restoration of the original HKCU startup value. This verifies registration, not an actual reboot or sign-in cycle. |
| Installer | All 13 checks passed: installation, all payload hashes, uninstaller registration, startup, installed UI smoke, tray-only launch, second-instance reopen, cooperative exit, update path, and uninstall cleanup. |

Commands:

```powershell
.\tools\Build.ps1 -Test
.\tools\Package.ps1 -SkipTests
.\tools\Build-Installer.ps1
.\tools\Test-App.ps1 -Hardware -Startup
.\tools\Test-Installer.ps1
```

The test scripts write JSON receipts and screenshots under the ignored `build/validation/` directory.
They use isolated smoke-test profiles and never publish local activity logs or monitor identifiers.
Installer tests refuse to replace an existing registered OmniLux installation. Hardware diagnostics
restore the exact original raw brightness, not a rounded percentage. Documentation screenshots use
demo monitors.

Readback verifies the monitor-reported setting; it does not measure physical panel luminance.
Tray registration and close/reopen behavior are checked; the shell icon's physical placement is not
automatically asserted. Native sleep/wake and cable/dock hot-plug, HDR and disabled-DDC behavior,
Explorer restart, reboot/sign-in, Windows 10, ARM64, publisher signing, and SmartScreen reputation
are not qualified by these tests. Monitor firmware and cable paths vary, so other devices need
their own hardware check.
