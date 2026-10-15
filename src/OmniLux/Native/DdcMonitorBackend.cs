using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using OmniLux.Core;

namespace OmniLux.Native;

/// <summary>Only brightness and contrast are writable; no arbitrary VCP, power, or reset commands.</summary>
public sealed class DdcMonitorBackend : IMonitorBackend
{
    public sealed record HardwareCheck(string Name, DisplayControl Control, uint Original, uint Requested,
        uint? ChangedReadback, bool ChangeVerified, uint? RestoredReadback, bool RestoreVerified, string? Error);
    private sealed record Channel(MonitorRange Range, bool LowLevel);
    private sealed record Endpoint(SafePhysicalMonitor Handle, Dictionary<DisplayControl, Channel> Controls);
    private readonly Dictionary<string, Endpoint> endpoints = new(StringComparer.Ordinal);
    private bool disposed;

    public IReadOnlyList<MonitorSnapshot> Discover()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ClearHandles();
        var logical = new List<nint>();
        MonitorNative.MonitorCallback callback = (monitor, _, _, _) => { logical.Add(monitor); return true; };
        if (!MonitorNative.EnumDisplayMonitors(0, 0, callback, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var result = new List<MonitorSnapshot>();
        try { foreach (var monitor in logical) DiscoverLogical(monitor, result); }
        catch { ClearHandles(); throw; }
        return result;
    }

    private static ControlSnapshot Unavailable(string status) => new(null, false, status, "Unavailable");

    private void DiscoverLogical(nint logical, List<MonitorSnapshot> result)
    {
        var info = new MonitorNative.MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorNative.MonitorInfo>() };
        if (!MonitorNative.GetMonitorInfo(logical, ref info)) return;
        var children = new List<MonitorNative.DisplayDevice>();
        for (uint index = 0; index < 64; index++)
        {
            var child = new MonitorNative.DisplayDevice { Size = (uint)Marshal.SizeOf<MonitorNative.DisplayDevice>() };
            if (!MonitorNative.EnumDisplayDevices(info.Device, index, ref child, 1)) break;
            if ((child.Flags & 1) != 0) children.Add(child);
        }
        string connection = info.Device.Replace(@"\\.\", "", StringComparison.Ordinal);
        bool primary = (info.Flags & 1) != 0;
        if (!MonitorNative.GetNumberOfPhysicalMonitorsFromHMONITOR(logical, out uint count) || count == 0 || count > 64)
        {
            var child = children.FirstOrDefault();
            var unavailable = Unavailable("DDC/CI unavailable. Enable DDC/CI in the monitor menu, then refresh.");
            result.Add(new(Identity(child), FriendlyName(child.Description, connection), connection, primary, unavailable, unavailable));
            return;
        }
        var physical = new MonitorNative.PhysicalMonitor[count];
        if (!MonitorNative.GetPhysicalMonitorsFromHMONITOR(logical, count, physical))
        {
            foreach (var item in physical) if (item.Handle != 0) MonitorNative.DestroyPhysicalMonitor(item.Handle);
            var child = children.FirstOrDefault();
            var unavailable = Unavailable("Windows could not open this monitor. Check the cable or dock and refresh.");
            result.Add(new(Identity(child), FriendlyName(child.Description, connection), connection, primary, unavailable, unavailable));
            return;
        }
        for (int index = 0; index < physical.Length; index++)
        {
            var handle = new SafePhysicalMonitor(physical[index].Handle);
            bool retained = false;
            try
            {
                var child = children.Count > index ? children[index] : children.FirstOrDefault();
                string id = physical.Length > 1 && physical.Length > children.Count
                    ? "volatile:" + Guid.NewGuid().ToString("N") : Identity(child);
                if (endpoints.ContainsKey(id)) id = "volatile:" + Guid.NewGuid().ToString("N");
                string name = FriendlyName(physical[index].Description, FriendlyName(ReadEdidName(child.Id), FriendlyName(child.Description, connection)));
                var endpoint = new Endpoint(handle, []);
                ControlSnapshot Probe(DisplayControl control)
                {
                    bool low = false;
                    bool readable = TryRead(handle, control, false, out var range);
                    if (!readable) { low = true; readable = TryRead(handle, control, true, out range); }
                    if (!readable) return Unavailable($"{control} unavailable. Check DDC/CI, HDR, or the monitor picture mode.");
                    endpoint.Controls[control] = new(range, low);
                    return new(range.Percent, true, "Ready", low ? $"DDC/CI · VCP 0x{VcpCode(control):X2}" : "DDC/CI");
                }
                var brightness = Probe(DisplayControl.Brightness);
                var contrast = Probe(DisplayControl.Contrast);
                if (endpoint.Controls.Count > 0) { endpoints.Add(id, endpoint); retained = true; }
                result.Add(new(id, name, connection, primary, brightness, contrast));
            }
            catch
            {
                for (int remaining = index + 1; remaining < physical.Length; remaining++)
                    if (physical[remaining].Handle != 0) MonitorNative.DestroyPhysicalMonitor(physical[remaining].Handle);
                throw;
            }
            finally { if (!retained) handle.Dispose(); }
        }
    }

    private static string Identity(MonitorNative.DisplayDevice child)
    {
        string key = !string.IsNullOrWhiteSpace(child.Id) ? child.Id : child.Key;
        return string.IsNullOrWhiteSpace(key) ? "volatile:" + Guid.NewGuid().ToString("N") : key.ToUpperInvariant();
    }
    private static string FriendlyName(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("Generic", StringComparison.OrdinalIgnoreCase) ? fallback : value.Trim();
    private static string? ReadEdidName(string? deviceId)
    {
        var pieces = deviceId?.Split('#');
        if (pieces is not { Length: >= 3 } || !pieces[0].EndsWith("DISPLAY", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{pieces[1]}\{pieces[2]}\Device Parameters");
            if (key?.GetValue("EDID") is not byte[] { Length: >= 128 } edid) return null;
            for (int offset = 54; offset <= 108; offset += 18)
                if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 3] == 0xFC)
                    return Encoding.ASCII.GetString(edid, offset + 5, 13).Trim('\0', '\n', '\r', ' ');
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        return null;
    }
    private static byte VcpCode(DisplayControl control) => control switch
    { DisplayControl.Brightness => 0x10, DisplayControl.Contrast => 0x12, _ => throw new ArgumentOutOfRangeException(nameof(control)) };

    private static bool TryRead(SafePhysicalMonitor handle, DisplayControl control, bool lowLevel, out MonitorRange range)
    {
        bool ok;
        if (lowLevel)
        {
            ok = MonitorNative.GetVCPFeatureAndVCPFeatureReply(handle, VcpCode(control), out uint type, out uint current, out uint maximum);
            range = new(0, current, maximum); ok &= type == 0;
        }
        else
        {
            uint minimum, current, maximum;
            ok = control == DisplayControl.Brightness
                ? MonitorNative.GetMonitorBrightness(handle, out minimum, out current, out maximum)
                : MonitorNative.GetMonitorContrast(handle, out minimum, out current, out maximum);
            range = new(minimum, current, maximum);
        }
        return ok && range.IsValid;
    }
    public MonitorControlResult SetControl(string monitorId, DisplayControl control, int percent)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Enum.IsDefined(control)) throw new ArgumentOutOfRangeException(nameof(control));
        if (!endpoints.TryGetValue(monitorId, out var endpoint) || !endpoint.Controls.TryGetValue(control, out var channel))
            return new(monitorId, control, percent, false, false, null, $"{control} unavailable or monitor disconnected. Refresh to reconnect.");
        uint raw = channel.Range.FromPercent(percent);
        if (!SetRaw(endpoint.Handle, control, channel.LowLevel, raw))
            return new(monitorId, control, percent, false, false, null,
                $"Monitor rejected {control.ToString().ToLowerInvariant()} (Windows error {Marshal.GetLastWin32Error()}). Refresh and check the monitor mode.");
        if (ReadBack(endpoint.Handle, control, channel.LowLevel, raw, out var readback))
        {
            endpoint.Controls[control] = channel with { Range = readback };
            bool matches = readback.Current == raw;
            return new(monitorId, control, percent, matches, true, readback.Percent,
                matches ? $"{control} verified" : $"Monitor reports {readback.Percent}% after requesting {percent}% {control.ToString().ToLowerInvariant()}.");
        }
        return new(monitorId, control, percent, true, false, null, "Command sent; monitor readback unavailable. Refresh to check.");
    }
    private static bool SetRaw(SafePhysicalMonitor handle, DisplayControl control, bool lowLevel, uint value) => lowLevel
        ? MonitorNative.SetVCPFeature(handle, VcpCode(control), value)
        : control == DisplayControl.Brightness ? MonitorNative.SetMonitorBrightness(handle, value) : MonitorNative.SetMonitorContrast(handle, value);
    private static bool ReadBack(SafePhysicalMonitor handle, DisplayControl control, bool low, uint expected, out MonitorRange range)
    {
        range = default; bool valid = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (TryRead(handle, control, low, out var readback))
            { range = readback; valid = true; if (readback.Current == expected) return true; }
            if (attempt < 2) Thread.Sleep(80);
        }
        return valid;
    }
    /// <summary>Test supported controls, restoring the exact original raw value after each test.</summary>
    public IReadOnlyList<HardwareCheck> VerifyHardware(IReadOnlyList<MonitorSnapshot> monitors)
    {
        var checks = new List<HardwareCheck>();
        foreach (var monitor in monitors)
        {
            if (!endpoints.TryGetValue(monitor.Id, out var endpoint)) continue;
            foreach (var (control, channel) in endpoint.Controls.ToList())
            {
                if (!TryRead(endpoint.Handle, control, channel.LowLevel, out var original))
                { checks.Add(new(monitor.Name, control, 0, 0, null, false, null, false, "Could not read original value; no write attempted.")); continue; }
                uint requested = original.Current < original.Maximum ? original.Current + 1 : original.Current - 1;
                uint? changed = null, restored;
                bool changeVerified = false, restoreVerified;
                string? error = null;
                try
                {
                    if (!SetRaw(endpoint.Handle, control, channel.LowLevel, requested)) error = "Diagnostic change was rejected.";
                    else if (ReadBack(endpoint.Handle, control, channel.LowLevel, requested, out var change))
                    { changed = change.Current; changeVerified = change.Current == requested; }
                }
                finally
                {
                    bool sent = SetRaw(endpoint.Handle, control, channel.LowLevel, original.Current);
                    bool read = ReadBack(endpoint.Handle, control, channel.LowLevel, original.Current, out var restoration);
                    restored = read ? restoration.Current : null;
                    restoreVerified = sent && read && restoration.Current == original.Current;
                    if (!restoreVerified) error = "Original value could not be verified after restoration. Check the monitor.";
                    else endpoint.Controls[control] = channel with { Range = restoration };
                }
                checks.Add(new(monitor.Name, control, original.Current, requested, changed, changeVerified, restored, restoreVerified, error));
                if (!restoreVerified) return checks;
            }
        }
        return checks;
    }
    private void ClearHandles()
    { foreach (var endpoint in endpoints.Values) endpoint.Handle.Dispose(); endpoints.Clear(); }
    public void Dispose() { if (!disposed) { disposed = true; ClearHandles(); } }
}
