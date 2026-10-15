namespace OmniLux.Core;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DisplayControl>))]
public enum DisplayControl { Brightness, Contrast }

public readonly record struct MonitorRange(uint Minimum, uint Current, uint Maximum)
{
    public bool IsValid => Maximum > Minimum && Current >= Minimum && Current <= Maximum;

    public int Percent => IsValid
        ? (int)Math.Round((Current - Minimum) * 100d / (Maximum - Minimum), MidpointRounding.AwayFromZero)
        : throw new InvalidOperationException("The monitor returned an invalid control range.");

    public uint FromPercent(int percent)
    {
        if (!IsValid) throw new InvalidOperationException("The monitor returned an invalid control range.");
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        return Minimum + (uint)Math.Round((Maximum - Minimum) * (percent / 100d), MidpointRounding.AwayFromZero);
    }
}

public sealed record ControlSnapshot(int? Value, bool CanControl, string Status, string Protocol = "DDC/CI");

public sealed record MonitorSnapshot(string Id, string Name, string Connection, bool IsPrimary,
    ControlSnapshot Brightness, ControlSnapshot Contrast);

public sealed record MonitorControlResult(string MonitorId, DisplayControl Control, int Requested,
    bool Success, bool Verified, int? Actual, string Message);

public interface IMonitorBackend : IDisposable
{
    IReadOnlyList<MonitorSnapshot> Discover();
    MonitorControlResult SetControl(string monitorId, DisplayControl control, int percent);
}

/// <summary>Serializes DDC/CI operations and handle replacement, without blocking the UI thread.</summary>
public sealed class MonitorAccess(IMonitorBackend backend) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public Task<IReadOnlyList<MonitorSnapshot>> DiscoverAsync(CancellationToken cancellation = default)
        => RunAsync(backend.Discover, cancellation);

    public Task<MonitorControlResult> SetAsync(string id, int percent, CancellationToken cancellation = default)
        => SetAsync(id, DisplayControl.Brightness, percent, cancellation);

    public Task<MonitorControlResult> SetAsync(string id, DisplayControl control, int percent, CancellationToken cancellation = default)
    {
        if (!Enum.IsDefined(control)) throw new ArgumentOutOfRangeException(nameof(control));
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        return RunAsync(() => backend.SetControl(id, control, percent), cancellation);
    }

    private async Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            // Once a native call starts, keep the gate until it finishes. Cancellation must
            // never free a physical handle that a driver call is still using.
            return await Task.Run(work, CancellationToken.None).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { if (!disposed) { disposed = true; backend.Dispose(); } }
        finally { gate.Release(); }
    }
}
