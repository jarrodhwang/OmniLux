using System.Security.Principal;

namespace OmniLux.Services;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle show, exit;
    private RegisteredWaitHandle? showWait, exitWait;
    public bool IsOwner { get; }

    public SingleInstance(string? isolation = null)
    {
        string identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        string name = @"Local\OmniLux." + identity + (isolation is null ? "" : "." + isolation);
        mutex = new Mutex(true, name + ".mutex", out bool created);
        IsOwner = created;
        show = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".show");
        exit = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".exit");
    }

    public void SignalShow() => show.Set();
    public void SignalExit() => exit.Set();
    public void Listen(Action onShow, Action onExit)
    {
        showWait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => onShow(), null, Timeout.Infinite, false);
        exitWait = ThreadPool.RegisterWaitForSingleObject(exit, (_, _) => onExit(), null, Timeout.Infinite, false);
    }
    public void Dispose()
    {
        showWait?.Unregister(null); exitWait?.Unregister(null);
        if (IsOwner) mutex.ReleaseMutex();
        mutex.Dispose(); show.Dispose(); exit.Dispose();
    }
}
