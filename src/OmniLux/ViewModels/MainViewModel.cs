using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using OmniLux.Core;
using OmniLux.Services;

namespace OmniLux.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly MonitorAccess access;
    private readonly SettingsStore store;
    private readonly StartupService startup;
    private readonly AppLog log;
    private readonly AppSettings settings;
    private readonly DispatcherTimer timer;
    private readonly Action<string> setTheme;
    private bool discovering, timerRunning, closing, refreshAgain;
    private bool startupEnabled;
    private bool settingsWritable;
    private int allBrightness = 50;
    private CancellationTokenSource? allPending;
    private string status = "Connecting to monitors…";
    public ObservableCollection<MonitorViewModel> Monitors { get; } = [];
    public ObservableCollection<TimerViewModel> Timers { get; } = [];
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsDiscovering { get => discovering; private set { Set(ref discovering, value); Notify(nameof(CanRefresh)); } }
    public bool CanRefresh => !IsDiscovering;
    public bool HasMonitors => Monitors.Count > 0;
    public bool HasNoMonitors => !HasMonitors && !IsDiscovering;
    public bool HasAdjustable => Monitors.Any(x => x.CanControl);
    public bool HasNoTimers => Timers.Count == 0;
    public string MonitorCount => $"{Monitors.Count} connected · {Monitors.Count(x => x.CanControl)} adjustable";
    public string TimerSummary => TimersPaused ? "Timers paused" : $"{Timers.Count(x => x.Enabled)} active timers";
    public string StoragePath => Path.GetDirectoryName(store.FilePath)!;
    public bool SettingsWritable => settingsWritable;
    public event Action? Changed;

    public int AllBrightness
    {
        get => allBrightness;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (Set(ref allBrightness, value)) { Notify(nameof(AllBrightnessText)); RequestAll(value); }
        }
    }
    public string AllBrightnessText
    {
        get
        {
            if (allPending is not null) return $"{AllBrightness}%";
            var values = Monitors.Where(x => x.CanControl).Select(x => x.Actual).Distinct().ToList();
            return values.Count > 1 ? "Mixed" : values.FirstOrDefault() is { } value ? $"{value}%" : "—";
        }
    }

    public string Theme
    {
        get => settings.Theme;
        set
        {
            if (value == settings.Theme || value is not ("System" or "Light" or "Dark")) return;
            string previous = settings.Theme;
            settings.Theme = value;
            if (!SaveSettings()) settings.Theme = previous;
            setTheme(settings.Theme); Notify(); Changed?.Invoke();
        }
    }

    public bool StartupEnabled
    {
        get => startupEnabled;
        set
        {
            if (value == startupEnabled) return;
            try
            {
                startup.SetEnabled(value);
                startupEnabled = value;
                Status = value ? "Startup enabled · OmniLux will open in the tray at sign-in" : "Startup disabled";
                log.Write("startup", new { enabled = value });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { Status = "Startup setting could not be saved: " + ex.Message; }
            Notify(); Changed?.Invoke();
        }
    }

    public bool TimersPaused
    {
        get => settings.TimersPaused;
        set
        {
            if (settings.TimersPaused == value) return;
            bool previous = settings.TimersPaused;
            settings.TimersPaused = value;
            if (!SaveSettings()) settings.TimersPaused = previous;
            Notify(); Notify(nameof(TimerSummary)); Changed?.Invoke();
            Status = settings.TimersPaused ? "Brightness timers paused" : "Brightness timers resumed";
        }
    }

    public MainViewModel(MonitorAccess access, SettingsStore store, StartupService startup, AppLog log,
        SettingsLoadResult loaded, Action<string> setTheme)
    {
        this.access = access; this.store = store; this.startup = startup; this.log = log;
        settings = loaded.Settings; settingsWritable = loaded.Warning is null;
        this.setTheme = setTheme;
        try { startupEnabled = startup.IsEnabled; } catch (Exception ex) when (ex is IOException or System.Security.SecurityException) { log.Write("startup-read-failed", ex.Message); }
        foreach (var rule in settings.Schedules) Timers.Add(CreateTimer(rule));
        timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += TimerTick;
        if (loaded.Warning is not null) { Status = loaded.Warning; settings.TimersPaused = true; }
        timer.Start();
    }

    public async Task RefreshAsync()
    {
        if (closing) return;
        if (IsDiscovering) { refreshAgain = true; return; }
        bool refreshed = false;
        IsDiscovering = true; Status = "Reading monitor brightness…";
        allPending?.Cancel();
        foreach (var monitor in Monitors) monitor.Pending?.Cancel();
        try
        {
            var snapshots = await access.DiscoverAsync();
            if (closing) return;
            Monitors.Clear();
            foreach (var snapshot in snapshots) Monitors.Add(new(snapshot, RequestMonitor));
            var first = Monitors.FirstOrDefault(x => x.CanControl);
            if (first is not null) { allBrightness = first.Brightness; Notify(nameof(AllBrightness)); Notify(nameof(AllBrightnessText)); }
            Status = settingsWritable ? (HasAdjustable ? "Monitors ready" : "No adjustable DDC/CI monitors found")
                : "Saved settings need recovery. Open Settings to reset them; brightness controls are available.";
            foreach (var property in new[] { nameof(HasMonitors), nameof(HasNoMonitors), nameof(HasAdjustable), nameof(MonitorCount) }) Notify(property);
            foreach (var entry in Timers) entry.Refresh();
            log.Write("monitors-discovered", snapshots.Select(x => new { x.Name, x.CanControl, x.Brightness, x.Protocol }));
            Changed?.Invoke();
            refreshed = true;
        }
        catch (Exception ex) { Status = "Could not refresh monitors: " + ex.Message; log.Write("discovery-failed", ex.Message); }
        finally { IsDiscovering = false; Notify(nameof(HasNoMonitors)); }
        if (refreshAgain && !closing) { refreshAgain = false; await RefreshAsync(); }
        else if (refreshed) await RunTimersAsync();
    }

    private async void RequestMonitor(MonitorViewModel monitor, int value)
    {
        if (!monitor.CanControl || closing || IsDiscovering) return;
        monitor.Pending?.Cancel();
        var pending = monitor.Pending = new CancellationTokenSource();
        long version = ++monitor.RequestVersion;
        monitor.Status = "Applying…"; monitor.IsApplying = true;
        try
        {
            await Task.Delay(180, pending.Token);
            var result = await access.SetAsync(monitor.Id, value, pending.Token);
            log.Write("brightness-manual", new { monitor.Name, result.Requested, result.Actual, result.Success, result.Verified });
            if (version != monitor.RequestVersion || closing || !Monitors.Contains(monitor)) return;
            monitor.Readback(result, !result.Success || result.Verified);
            Status = result.Success && result.Verified ? $"{monitor.Name} · {result.Actual}%" : result.Message;
            Notify(nameof(AllBrightnessText));
            Changed?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (version == monitor.RequestVersion) { monitor.Status = ex.Message; Status = "Brightness change failed: " + ex.Message; } log.Write("brightness-error", ex.Message); }
        finally { if (version == monitor.RequestVersion) monitor.IsApplying = false; if (ReferenceEquals(monitor.Pending, pending)) monitor.Pending = null; pending.Dispose(); }
    }

    private async void RequestAll(int value)
    {
        allPending?.Cancel();
        var pending = allPending = new CancellationTokenSource();
        try
        {
            await Task.Delay(180, pending.Token);
            foreach (var monitor in Monitors.Where(x => x.CanControl).ToList())
            {
                if (pending.IsCancellationRequested || closing) break;
                // Individual requests share the same coalescing and serialized native path.
                if (monitor.Brightness == value) RequestMonitor(monitor, value);
                else monitor.Brightness = value;
            }
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(allPending, pending)) allPending = null; Notify(nameof(AllBrightnessText)); pending.Dispose(); }
    }

    public void SetAll(int value)
    {
        if (AllBrightness == value) RequestAll(value); else AllBrightness = value;
    }

    public void AddOrUpdateTimer(BrightnessSchedule rule)
    {
        var existing = settings.Schedules.FirstOrDefault(x => x.Id == rule.Id);
        if (existing is not null) settings.Schedules.Remove(existing);
        settings.Schedules.Add(rule);
        if (!SaveSettings()) { settings.Schedules.Remove(rule); if (existing is not null) settings.Schedules.Add(existing); return; }
        RebuildTimers(); Status = "Brightness timer saved"; log.Write("timer-saved", new { rule.Name, rule.Kind, rule.Brightness });
    }

    public void RemoveTimer(TimerViewModel entry)
    {
        int index = settings.Schedules.IndexOf(entry.Rule);
        if (index < 0) return;
        settings.Schedules.RemoveAt(index);
        if (!SaveSettings()) { settings.Schedules.Insert(index, entry.Rule); return; }
        RebuildTimers(); Status = "Brightness timer removed";
    }

    private void ToggleTimer(TimerViewModel entry, bool enabled)
    {
        var previous = entry.Rule with { };
        entry.Rule.Enabled = enabled;
        if (enabled)
        {
            entry.Rule.NotBefore = DateTimeOffset.Now;
            entry.Rule.LastOccurrence = null;
            entry.Rule.LastOutcome = null;
            if (entry.Rule.Kind == ScheduleKind.Countdown) entry.Rule.DueAt = DateTimeOffset.Now.AddMinutes(entry.Rule.DelayMinutes);
        }
        if (!SaveSettings())
        {
            entry.Rule.Enabled = previous.Enabled; entry.Rule.NotBefore = previous.NotBefore;
            entry.Rule.LastOccurrence = previous.LastOccurrence; entry.Rule.DueAt = previous.DueAt;
            entry.Rule.LastOutcome = previous.LastOutcome;
        }
        entry.Refresh(); Notify(nameof(TimerSummary)); Changed?.Invoke();
    }

    private TimerViewModel CreateTimer(BrightnessSchedule rule) => new(rule, id => id is null ? "All adjustable displays"
        : Monitors.FirstOrDefault(x => x.Id == id)?.Name ?? "Saved display · disconnected", ToggleTimer);

    private void RebuildTimers()
    {
        Timers.Clear(); foreach (var rule in settings.Schedules) Timers.Add(CreateTimer(rule));
        Notify(nameof(HasNoTimers)); Notify(nameof(TimerSummary)); Changed?.Invoke();
    }

    private async void TimerTick(object? sender, EventArgs args) => await RunTimersAsync();

    private async Task RunTimersAsync()
    {
        if (timerRunning || closing || IsDiscovering || TimersPaused || !settingsWritable) return;
        timerRunning = true;
        try
        {
            var batch = SchedulePlanner.Plan(settings.Schedules, DateTimeOffset.Now, TimeZoneInfo.Local,
                Monitors.Where(x => x.CanControl).Select(x => x.Id));
            if (batch.Writes.Count == 0) return;
            var before = settings.Schedules.Select(x => x with { }).ToList();
            var activeRuleIds = batch.Writes.Select(x => x.ScheduleId).ToHashSet();
            var outcomes = batch.Consumed.ToDictionary(x => x.ScheduleId, x => activeRuleIds.Contains(x.ScheduleId) ? "Pending" : "Superseded");
            foreach (var consumed in batch.Consumed)
            {
                var rule = settings.Schedules.First(x => x.Id == consumed.ScheduleId);
                rule.LastOccurrence = consumed.Occurrence;
                rule.LastOutcome = "Pending";
                if (rule.Kind == ScheduleKind.Countdown) rule.Enabled = false;
            }
            // Persist consumption before writing, avoiding repeated actions after crashes/restarts.
            if (!SaveSettings()) { settings.Schedules = before; RebuildTimers(); return; }
            foreach (var write in batch.Writes)
            {
                if (closing) break;
                var monitor = Monitors.FirstOrDefault(x => x.Id == write.MonitorId);
                if (monitor is null) continue;
                monitor.Pending?.Cancel();
                long version = ++monitor.RequestVersion;
                monitor.IsApplying = true;
                try
                {
                    var result = await access.SetAsync(write.MonitorId, write.Brightness);
                    string outcome = !result.Success ? "Failed" : result.Verified ? "Applied" : "Unverified";
                    if (outcomes[write.ScheduleId] != "Failed" && !(outcomes[write.ScheduleId] == "Unverified" && outcome == "Applied"))
                        outcomes[write.ScheduleId] = outcome;
                    if (monitor.RequestVersion == version)
                    {
                        monitor.Readback(result, true);
                        Status = result.Success && result.Verified ? $"Timer applied · {monitor.Name} · {result.Actual}%" : "Timer: " + result.Message;
                    }
                    log.Write("brightness-timer", new { write.ScheduleId, monitor.Name, result.Requested, result.Actual, result.Success, result.Verified });
                    Notify(nameof(AllBrightnessText));
                }
                catch (Exception ex) { outcomes[write.ScheduleId] = "Failed"; Status = "Timer failed: " + ex.Message; log.Write("timer-failed", ex.Message); }
                finally { if (monitor.RequestVersion == version) monitor.IsApplying = false; }
            }
            foreach (var outcome in outcomes)
            {
                var rule = settings.Schedules.FirstOrDefault(x => x.Id == outcome.Key);
                if (rule is not null) rule.LastOutcome = outcome.Value;
            }
            SaveSettings();
            foreach (var entry in Timers) entry.Refresh(); Notify(nameof(TimerSummary)); Changed?.Invoke();
        }
        finally { timerRunning = false; }
    }

    private bool SaveSettings()
    {
        if (!settingsWritable) { Status = "Saved settings need recovery. Open Settings to reset them first."; return false; }
        try { store.Save(settings); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status = "Settings could not be saved: " + ex.Message; log.Write("settings-save-failed", ex.Message); return false; }
    }

    public void ResetSettings()
    {
        try
        {
            if (File.Exists(store.FilePath)) File.Copy(store.FilePath, store.FilePath + ".recovered-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), true);
            var reset = new AppSettings { Theme = settings.Theme, TimersPaused = true };
            store.Save(reset);
            settings.Schedules.Clear(); settings.TimersPaused = true; settingsWritable = true;
            Notify(nameof(SettingsWritable)); Notify(nameof(TimersPaused)); RebuildTimers(); Status = "Settings reset. Previous file preserved beside settings.json.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "Could not reset settings: " + ex.Message; }
    }

    public void Stop()
    {
        closing = true; timer.Stop(); allPending?.Cancel();
        foreach (var monitor in Monitors) monitor.Pending?.Cancel();
    }
}
