using System.Security.Principal;

namespace OmniLux.Services;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle show, exit, quick;
    private RegisteredWaitHandle? showWait, exitWait, quickWait;
    public bool IsOwner { get; }

    public SingleInstance(string? isolation = null)
    {
        string identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        string name = @"Local\OmniLux." + identity + (isolation is null ? "" : "." + isolation);
        mutex = new Mutex(true, name + ".mutex", out bool created);
        IsOwner = created;
        show = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".show");
        exit = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".exit");
        quick = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".quick");
    }

    public void SignalShow() => show.Set();
    public void SignalExit() => exit.Set();
    public void SignalQuickControls() => quick.Set();
    public void Listen(Action onShow, Action onExit, Action onQuick)
    {
        showWait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => onShow(), null, Timeout.Infinite, false);
        exitWait = ThreadPool.RegisterWaitForSingleObject(exit, (_, _) => onExit(), null, Timeout.Infinite, false);
        quickWait = ThreadPool.RegisterWaitForSingleObject(quick, (_, _) => onQuick(), null, Timeout.Infinite, false);
    }
    public void Dispose()
    {
        showWait?.Unregister(null); exitWait?.Unregister(null); quickWait?.Unregister(null);
        if (IsOwner) mutex.ReleaseMutex();
        mutex.Dispose(); show.Dispose(); exit.Dispose(); quick.Dispose();
    }
}
