# Validation scope

Version **0.2.0**, local validation on **2026-10-15 (UTC)**, Windows 11 Pro x64 (build 26200), .NET SDK 10.0.401.
Keep synthetic, native UI, registry, and physical monitor evidence separate.

| Layer | Checks and evidence |
| --- | --- |
| Build | Release builds with warnings treated as errors. No external NuGet dependencies. |
| Core regression | 35 tests cover control ranges/bounds, brightness/contrast routing, a shared serialization gate, canceled queued requests, native failures/disposal, schedules/DST, settings corruption/backup/schema, and unchanged version 0.1 settings. |
| Demo UI | 34 checks exercise real WPF controls with a fake backend: zero writes while holding/dragging, no commit when leaving during a held drag, one final write per target, duplicate-release suppression, keyboard/mouse-leave apply, contrast support/failure rollback, timer completion/editor validation, tray sliders without the main window, Escape cancellation, window bounds, themes, and minimum size. Demo mode never calls DDC/CI. |
| Native UI | 18 checks use actual display discovery, real main and quick-control windows, monitor rows, work-area bounds, editor/navigation, and close/reopen behavior. |
| Physical DDC/CI | Six supported-control checks passed. DELL S2725DS DisplayPort and HDMI: brightness 75 → 76 → 75, contrast 80 → 81 → 80. Alienware AW2521HFL: brightness 75 → 76 → 75, contrast 93 → 94 → 93. Native readback confirmed each change and exact restoration. |
| Startup | Enable, disable, quoted executable command, and restoration of the original HKCU startup value. This verifies registration, not an actual reboot or sign-in cycle. |
| Upgrade | All 10 checks passed using the published, SHA-256-verified 0.1.0 setup as a real baseline: baseline version, upgrade version/path, unchanged settings bytes, all payload hashes, enabled/disabled startup retention across repeated upgrades, and explicit task override. The original startup value was restored. |
| Fresh installer | `Test-Installer.ps1` covers 13 installation/update/uninstall checks on a clean user profile. Version 0.1 passed locally; the 0.2 CI workflow runs it when its runner has an interactive desktop. This is separate from the local upgrade check. |

Commands:

```powershell
.\tools\Build.ps1 -Test
.\tools\Package.ps1 -SkipTests
.\tools\Build-Installer.ps1
.\tools\Test-App.ps1 -Hardware -Startup
.\tools\Test-Upgrade.ps1
.\tools\Test-Installer.ps1
```

The test scripts write JSON receipts and screenshots under the ignored `build/validation/` directory.
They use isolated smoke-test profiles and never publish local activity logs or monitor identifiers.
Fresh installer tests refuse to replace an existing registered OmniLux installation; upgrade tests
deliberately update that registered application. Hardware diagnostics restore each exact original
brightness and contrast value, not a rounded percentage. Documentation screenshots use
demo monitors.

Readback verifies the monitor-reported setting; it does not measure physical panel luminance.
Tray registration and close/reopen behavior are checked; the shell icon's physical placement is not
automatically asserted. Native sleep/wake and cable/dock hot-plug, HDR and disabled-DDC behavior,
Explorer restart, reboot/sign-in, Windows 10, ARM64, publisher signing, and SmartScreen reputation
are not qualified by these tests. Monitor firmware and cable paths vary, so other devices need
their own hardware check.
