using OmniLux.Core;

namespace OmniLux.Native;

/// <summary>Explicit --demo mode for UI review. It never calls a monitor API.</summary>
internal sealed class DemoMonitorBackend : IMonitorBackend
{
    private readonly Dictionary<string, int> brightness = new() { ["demo:studio"] = 72, ["demo:desk"] = 45 };
    public IReadOnlyList<MonitorSnapshot> Discover() => [
        new("demo:studio", "Studio display", "DISPLAY1", true, brightness["demo:studio"], true, "Ready · demo", "DDC/CI · demo"),
        new("demo:desk", "Desk monitor", "DISPLAY2", false, brightness["demo:desk"], true, "Ready · demo", "DDC/CI · demo"),
        new("demo:panel", "Laptop panel", "DISPLAY3", false, null, false, "Brightness unavailable. Enable DDC/CI in the monitor menu, then refresh.", "Unavailable · demo")
    ];
    public BrightnessResult SetBrightness(string monitorId, int percent)
    {
        if (!brightness.ContainsKey(monitorId)) return new(monitorId, percent, false, false, null, "Unavailable · demo");
        brightness[monitorId] = percent;
        return new(monitorId, percent, true, true, percent, "Brightness verified · demo");
    }
    public void Dispose() { }
}
