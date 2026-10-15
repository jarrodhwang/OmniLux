using OmniLux.Core;

namespace OmniLux.Native;

/// <summary>Explicit --demo mode: no native calls. Counters verify that previews do not write.</summary>
internal sealed class DemoMonitorBackend : IMonitorBackend
{
    private readonly Dictionary<(string, DisplayControl), int> values = new()
    {
        [("demo:studio", DisplayControl.Brightness)] = 72, [("demo:studio", DisplayControl.Contrast)] = 70,
        [("demo:desk", DisplayControl.Brightness)] = 45
    };
    public int WriteCount { get; private set; }
    public DisplayControl? RejectControl { get; set; }
    private ControlSnapshot Read(string id, DisplayControl control) => values.TryGetValue((id, control), out int value)
        ? new(value, true, "Ready · demo", "DDC/CI · demo") : new(null, false, $"{control} unavailable · demo", "Unavailable · demo");
    public IReadOnlyList<MonitorSnapshot> Discover() => [
        new("demo:studio", "Studio display", "DISPLAY1", true, Read("demo:studio", DisplayControl.Brightness), Read("demo:studio", DisplayControl.Contrast)),
        new("demo:desk", "Desk monitor", "DISPLAY2", false, Read("demo:desk", DisplayControl.Brightness), Read("demo:desk", DisplayControl.Contrast)),
        new("demo:panel", "Laptop panel", "DISPLAY3", false, Read("demo:panel", DisplayControl.Brightness), Read("demo:panel", DisplayControl.Contrast))
    ];
    public MonitorControlResult SetControl(string monitorId, DisplayControl control, int percent)
    {
        WriteCount++;
        if (control == RejectControl || !values.ContainsKey((monitorId, control)))
            return new(monitorId, control, percent, false, false, null, $"{control} rejected · demo");
        values[(monitorId, control)] = percent;
        return new(monitorId, control, percent, true, true, percent, $"{control} verified · demo");
    }
    public void Dispose() { }
}
