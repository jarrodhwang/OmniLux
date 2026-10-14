using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using OmniLux.Core;

namespace OmniLux.Native;

/// <summary>Only brightness is exposed. No contrast, power, factory reset, or arbitrary VCP writes.</summary>
public sealed class DdcMonitorBackend : IMonitorBackend
{
    public sealed record HardwareCheck(string Name, uint Original, uint Requested,
        uint? ChangedReadback, bool ChangeVerified, uint? RestoredReadback, bool RestoreVerified, string? Error);
    private sealed record Endpoint(SafePhysicalMonitor Handle, BrightnessRange Range, bool LowLevel);
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

    private void DiscoverLogical(nint logical, List<MonitorSnapshot> result)
    {
        var info = new MonitorNative.MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorNative.MonitorInfo>() };
        if (!MonitorNative.GetMonitorInfo(logical, ref info)) return;
        var children = new List<MonitorNative.DisplayDevice>();
        for (uint index = 0; index < 64; index++)
        {
            var child = new MonitorNative.DisplayDevice { Size = (uint)Marshal.SizeOf<MonitorNative.DisplayDevice>() };
            // EDD_GET_DEVICE_INTERFACE_NAME supplies a persistent PnP interface, not DISPLAY1 ordering.
            if (!MonitorNative.EnumDisplayDevices(info.Device, index, ref child, 1)) break;
            if ((child.Flags & 1) != 0) children.Add(child);
        }
        var connection = info.Device.Replace(@"\\.\", "", StringComparison.Ordinal);
        bool primary = (info.Flags & 1) != 0;
        if (!MonitorNative.GetNumberOfPhysicalMonitorsFromHMONITOR(logical, out uint count) || count == 0 || count > 64)
        {
            var child = children.FirstOrDefault();
            result.Add(new(Identity(child, null, 0), FriendlyName(child.Description, connection), connection, primary,
                null, false, "DDC/CI unavailable. Enable DDC/CI in the monitor menu, then refresh.", "Unavailable"));
            return;
        }
        var physical = new MonitorNative.PhysicalMonitor[count];
        if (!MonitorNative.GetPhysicalMonitorsFromHMONITOR(logical, count, physical))
        {
            // Defensive cleanup if a driver returned any partially populated handles.
            foreach (var item in physical) if (item.Handle != 0) MonitorNative.DestroyPhysicalMonitor(item.Handle);
            var child = children.FirstOrDefault();
            result.Add(new(Identity(child, null, 0), FriendlyName(child.Description, connection), connection, primary,
                null, false, "Windows could not open this monitor. Check the cable or dock and refresh.", "Unavailable"));
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
                    ? "volatile:" + Guid.NewGuid().ToString("N") : Identity(child, null, index);
                string name = FriendlyName(physical[index].Description, FriendlyName(ReadEdidName(child.Id), FriendlyName(child.Description, connection)));
                bool lowLevel = false;
                bool readable = TryRead(handle, false, out var range);
                if (!readable) { lowLevel = true; readable = TryRead(handle, true, out range); }
                if (readable)
                {
                    if (endpoints.ContainsKey(id)) id = "volatile:" + Guid.NewGuid().ToString("N");
                    endpoints.Add(id, new(handle, range, lowLevel));
                    retained = true;
                    result.Add(new(id, name, connection, primary, range.Percent, true, "Ready",
                        lowLevel ? "DDC/CI · VCP 0x10" : "DDC/CI"));
                }
                else
                {
                    result.Add(new(id, name, connection, primary, null, false,
                        "Brightness unavailable. Enable DDC/CI in the monitor menu, then refresh.", "Unavailable"));
                }
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

    private static string Identity(MonitorNative.DisplayDevice child, string? suffix, int index)
    {
        string key = !string.IsNullOrWhiteSpace(child.Id) ? child.Id : child.Key;
        if (string.IsNullOrWhiteSpace(key)) return "volatile:" + Guid.NewGuid().ToString("N");
        return key.ToUpperInvariant() + (suffix is null ? "" : $"|{suffix}|{index}");
    }

    private static string FriendlyName(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("Generic", StringComparison.OrdinalIgnoreCase)
            ? fallback : value.Trim();

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

    private static bool TryRead(SafePhysicalMonitor handle, bool lowLevel, out BrightnessRange range)
    {
        bool ok;
        if (lowLevel)
        {
            ok = MonitorNative.GetVCPFeatureAndVCPFeatureReply(handle, 0x10, out uint type, out uint current, out uint maximum);
            range = new(0, current, maximum);
            ok &= type == 0; // MC_SET_PARAMETER: brightness must be a continuous setting.
        }
        else
        {
            ok = MonitorNative.GetMonitorBrightness(handle, out uint minimum, out uint current, out uint maximum);
            range = new(minimum, current, maximum);
        }
        return ok && range.IsValid;
    }

    public BrightnessResult SetBrightness(string monitorId, int percent)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!endpoints.TryGetValue(monitorId, out var endpoint))
            return new(monitorId, percent, false, false, null, "Monitor disconnected or unavailable. Refresh to reconnect.");
        uint raw = endpoint.Range.FromPercent(percent);
        bool ok = SetRaw(endpoint, raw);
        if (!ok)
        {
            int error = Marshal.GetLastWin32Error();
            return new(monitorId, percent, false, false, null, $"Monitor rejected the brightness change (Windows error {error}). Refresh and check DDC/CI.");
        }
        // Read the monitor itself. A successful setter alone does not prove the visible value changed.
        if (ReadBack(endpoint, raw, out var readback))
        {
            endpoints[monitorId] = endpoint with { Range = readback };
            bool matches = readback.Current == raw;
            return new(monitorId, percent, matches, true, readback.Percent,
                matches ? "Brightness verified" : $"Monitor reports {readback.Percent}% after requesting {percent}%.");
        }
        return new(monitorId, percent, true, false, null, "Command sent; monitor readback unavailable. Refresh to check.");
    }

    private static bool SetRaw(Endpoint endpoint, uint value) => endpoint.LowLevel
        ? MonitorNative.SetVCPFeature(endpoint.Handle, 0x10, value)
        : MonitorNative.SetMonitorBrightness(endpoint.Handle, value);

    private static bool ReadBack(Endpoint endpoint, uint expected, out BrightnessRange range)
    {
        range = default;
        bool valid = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (TryRead(endpoint.Handle, endpoint.LowLevel, out var readback))
            { range = readback; valid = true; if (readback.Current == expected) return true; }
            if (attempt < 2) Thread.Sleep(80);
        }
        return valid;
    }

    /// <summary>Explicit diagnostic: changes one native brightness unit and restores the exact original raw value.</summary>
    public IReadOnlyList<HardwareCheck> VerifyHardware(IReadOnlyList<MonitorSnapshot> monitors)
    {
        var checks = new List<HardwareCheck>();
        foreach (var monitor in monitors.Where(x => x.CanControl))
        {
            if (!endpoints.TryGetValue(monitor.Id, out var endpoint)) continue;
            if (!TryRead(endpoint.Handle, endpoint.LowLevel, out var original))
            { checks.Add(new(monitor.Name, 0, 0, null, false, null, false, "Could not read original brightness; no write attempted.")); continue; }
            uint requested = original.Current < original.Maximum ? original.Current + 1 : original.Current - 1;
            uint? changed = null, restored = null;
            bool changeVerified = false, restoreVerified;
            string? error = null;
            try
            {
                if (!SetRaw(endpoint, requested)) error = "The diagnostic change was rejected.";
                else if (ReadBack(endpoint, requested, out var change))
                { changed = change.Current; changeVerified = change.Current == requested; }
            }
            finally
            {
                bool restoreSent = SetRaw(endpoint, original.Current);
                bool restoreRead = ReadBack(endpoint, original.Current, out var restoration);
                restored = restoreRead ? restoration.Current : null;
                restoreVerified = restoreSent && restoreRead && restoration.Current == original.Current;
                if (!restoreVerified) error = "Original brightness could not be verified after restoration. Check the monitor.";
                else endpoints[monitor.Id] = endpoint with { Range = restoration };
            }
            checks.Add(new(monitor.Name, original.Current, requested, changed, changeVerified, restored, restoreVerified, error));
        }
        return checks;
    }

    private void ClearHandles()
    {
        foreach (var endpoint in endpoints.Values) endpoint.Handle.Dispose();
        endpoints.Clear();
    }

    public void Dispose() { if (!disposed) { disposed = true; ClearHandles(); } }
}
