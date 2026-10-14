namespace OmniLux.Core;

public readonly record struct BrightnessRange(uint Minimum, uint Current, uint Maximum)
{
    public bool IsValid => Maximum > Minimum && Current >= Minimum && Current <= Maximum;

    public int Percent => IsValid
        ? (int)Math.Round((Current - Minimum) * 100d / (Maximum - Minimum), MidpointRounding.AwayFromZero)
        : throw new InvalidOperationException("The monitor returned an invalid brightness range.");

    public uint FromPercent(int percent)
    {
        if (!IsValid) throw new InvalidOperationException("The monitor returned an invalid brightness range.");
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        return Minimum + (uint)Math.Round((Maximum - Minimum) * (percent / 100d), MidpointRounding.AwayFromZero);
    }
}

public sealed record MonitorSnapshot(string Id, string Name, string Connection, bool IsPrimary,
    int? Brightness, bool CanControl, string Status, string Protocol);

public sealed record BrightnessResult(string MonitorId, int Requested, bool Success, bool Verified,
    int? Actual, string Message);

public interface IMonitorBackend : IDisposable
{
    IReadOnlyList<MonitorSnapshot> Discover();
    BrightnessResult SetBrightness(string monitorId, int percent);
}

/// <summary>Serializes DDC/CI operations and handle replacement, without blocking the UI thread.</summary>
public sealed class MonitorAccess(IMonitorBackend backend) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public Task<IReadOnlyList<MonitorSnapshot>> DiscoverAsync(CancellationToken cancellation = default)
        => RunAsync(backend.Discover, cancellation);

    public Task<BrightnessResult> SetAsync(string id, int percent, CancellationToken cancellation = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        return RunAsync(() => backend.SetBrightness(id, percent), cancellation);
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
