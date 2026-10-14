# Architecture and practical quality review

OmniLux is a per-user, offline .NET 10 WPF app. It uses the Windows monitor configuration
APIs from `Dxva2.dll`, a Windows Forms `NotifyIcon`, and an HKCU Run value. There is no
privileged process, driver, network client, scheduled Windows task, or arbitrary VCP interface.
OmniSound informed the compact Windows UI; WPF makes this smaller project straightforward to
build and deploy without adding a Windows App SDK runtime.

`OmniLux.Core` owns brightness range conversion, schedule planning, settings persistence, and
serialized monitor access. The Windows app owns native enumeration/handles, UI state, tray,
startup registration, and theme. Core tests run without displays; demo UI checks explicitly
use a fake backend. Live hardware diagnostics use the production backend.

## Product quality

| Dimension | Implementation and trade-off |
| --- | --- |
| Functional suitability | Per-monitor/all-monitor hardware brightness, countdown/daily timers, tray operation, and startup are implemented. High-level brightness is tried first; monitors supporting only continuous VCP 0x10 use the low-level path. Unsupported devices stay visible. Setter success and verified readback are different states. |
| Reliability | Safe handles are released on rediscovery/exit. A single async gate serializes enumeration, writes, readback, and disposal. Hot-plug events are debounced, with one follow-up scan retained if changes arrive during discovery; resume refreshes twice for slower docks. Corrupt/newer settings are kept intact with timers disabled until recovery. Failed runs are visible and not hammered with repeated writes. |
| Performance efficiency | The UI never performs a DDC call synchronously. Slider writes debounce for 180 ms and queued superseded requests are canceled. One timer checks every five seconds; displays are not continuously polled. Verification makes at most three bounded reads. Serializing several displays trades some throughput for predictable driver behavior. |
| Maintainability | Pure schedule/range contracts, a backend interface, small platform services, and XAML views separate responsibilities. No external NuGet dependencies are required. Deterministic builds treat warnings as errors. Core regressions cover failures, cancellation, DST, and settings recovery. |
| Compatibility | Uses documented Windows desktop APIs with pointer-sized handles and Unicode structs. Brightness is normalized to the native min/max range. PnP monitor interface identities prevent schedules from following changing DISPLAY numbers. Ambiguous/volatile identities cannot be selected for persistent individual timers. |
| Security | Normal user privileges; startup writes only the app's HKCU value. No command execution from settings, arbitrary hardware commands, remote control, telemetry, or secrets. Settings and bounded activity logs stay in the user's local profile. Packages have SHA-256 manifests. The initial binaries are unsigned. |
| Usability | Monitor cards expose actual names, percentages, primary display, and errors. Mixed brightness is explicit. Closing/minimizing keeps the app in the tray; Exit is explicit. Invalid time/countdown inputs stay in the editor with clear feedback. Controls have accessible names and support keyboard input. |
| Portability | The app deliberately targets Windows hardware APIs. Self-contained x64 ZIP and per-user installer run offline. Source can also publish `win-arm64`, but ARM64 and Windows 10 have not been validated. No cross-platform abstraction or install service is added without a requirement. |

## Quality in use

| Dimension | Assessment |
| --- | --- |
| Effectiveness | A user can identify supported displays, change brightness, and check monitor-reported readback. Timer results distinguish applied, failed, unverified, interrupted, and superseded actions. |
| Efficiency | Sliders and tray presets avoid opening monitor menus; timer settings save automatically. Discovery and hardware latency do not freeze controls. |
| Satisfaction | Compact cards, consistent navigation, native tray behavior, and system/light/dark themes follow the existing OmniSound design. |
| Freedom from risk | Only brightness is writable; no factory reset, power-off, firmware, or driver installation. Hardware diagnostics change one raw unit and restore the original in a `finally` block. Corrupt data cannot silently activate timers. |
| Context coverage | Multiple displays, non-percent brightness ranges, disconnected targets, unavailable DDC/CI, sleep/restart, time zones, and DST have defined behavior. Real docks, HDR, different Windows versions, Explorer restarts, and physical sleep cycles still require device-specific validation. |

## Boundaries that matter

Native monitor drivers can stall inside a call. Canceling a managed task cannot safely interrupt
that call, so the gate stays held until it returns. The UI remains responsive and the user can
exit; Windows releases handles on process termination. A stalled driver can delay all pending
monitor writes. Separate worker processes would improve hard fault isolation but add deployment,
IPC, and lifecycle complexity inappropriate for this first version.

There is intentionally no continuous polling. A brightness change made through a monitor's own
menu becomes visible on Refresh or the next readback. No saved slider level is replayed on launch;
the app initially reads each display. Only explicit controls and due timers change brightness.

Daily wall-clock times resolve DST gaps to the next valid minute and repeated times to the later
occurrence once per date. Occurrences are persisted before hardware writes: this provides
at-most-once attempts after crashes, rather than risking repeated commands. Hardware writes
across multiple displays are not a transaction; a crash can leave a partial run. The activity
log and timer outcome expose this, and the user can retry deliberately.

Settings save through a temporary file plus replacement, preserving the prior version as a
backup. A process/OS crash during a hardware command can leave an incomplete attempt, which is
shown as Check last run. Timer targets retain PnP identifiers; moving a display to another port
may require reselecting it. Cloned displays lacking distinct identities use session-only IDs.

## Primary API references

- [Windows monitor configuration](https://learn.microsoft.com/en-us/windows/win32/monitor/monitor-configuration)
- [GetMonitorBrightness](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-getmonitorbrightness)
- [SetMonitorBrightness](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-setmonitorbrightness)
- [GetVCPFeatureAndVCPFeatureReply](https://learn.microsoft.com/en-us/windows/win32/api/lowlevelmonitorconfigurationapi/nf-lowlevelmonitorconfigurationapi-getvcpfeatureandvcpfeaturereply)
- [DestroyPhysicalMonitors](https://learn.microsoft.com/en-us/windows/win32/api/physicalmonitorenumerationapi/nf-physicalmonitorenumerationapi-destroyphysicalmonitors)
- [.NET self-contained publishing](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
