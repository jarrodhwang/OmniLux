# Changelog

## 0.2.0 — 2026-10-15

- Left-clicking the tray icon opens a compact panel with each monitor's brightness and contrast sliders.
  The main window opens through an explicit panel/menu action.
- Added independent hardware contrast detection, control, and readback, including VCP 0x12 fallback.
  Unsupported contrast does not disable brightness.
- Sliders preview locally during a held drag. Mouse release, an idle mouse leave, keyboard release,
  or the apply button commits the final value once per supported monitor. Presets remain immediate.
- Both windows share preview and verified values. Failed commands restore the last confirmed value;
  Escape in the tray panel cancels uncommitted previews.
- The tray panel follows the clicked display's DPI and stays within its working area.
- Existing startup preferences now survive upgrades, even after changes through the app settings.
  Explicit installer task overrides are respected. Version 0.1 timer/settings files remain compatible.
- Added control routing and compatibility regressions, held-drag/contrast/tray UI checks, and an
  existing-installation upgrade harness. CI includes installer/UI checks on an interactive desktop.
- Clean package staging, version consistency checks, current-release-only checksums, bundled notices,
  updated screenshots, and a documented release/signing procedure.

## 0.1.0 — 2026-10-14

- Initial Windows brightness controls, countdown/daily timers, tray presets, startup registration,
  self-contained packaging, and per-user installation.
