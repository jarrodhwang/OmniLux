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
    private string status = "Connecting to monitors…";
    public ObservableCollection<MonitorViewModel> Monitors { get; } = [];
    public ObservableCollection<TimerViewModel> Timers { get; } = [];
    public MonitorSettingViewModel AllBrightness { get; }
    public MonitorSettingViewModel AllContrast { get; }
    public string VersionText => "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.2.0");
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsDiscovering { get => discovering; private set { Set(ref discovering, value); Notify(nameof(CanRefresh)); } }
    public bool CanRefresh => !IsDiscovering;
    public bool HasMonitors => Monitors.Count > 0;
    public bool HasNoMonitors => !HasMonitors && !IsDiscovering;
    public bool HasAdjustable => Monitors.Any(x => x.HasAnyControl);
    public bool HasNoTimers => Timers.Count == 0;
    public string MonitorCount => $"{Monitors.Count} connected · {Monitors.Count(x => x.HasAnyControl)} adjustable";
    public string TimerSummary => TimersPaused ? "Timers paused" : $"{Timers.Count(x => x.Enabled)} active timers";
    public string StoragePath => Path.GetDirectoryName(store.FilePath)!;
    public bool SettingsWritable => settingsWritable;
    public event Action? Changed;

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
        AllBrightness = new("all", "All displays", DisplayControl.Brightness, new(null, false, ""), RequestAllAsync, true,
            () => CancelGroupPreview(DisplayControl.Brightness));
        AllContrast = new("all", "All displays", DisplayControl.Contrast, new(null, false, ""), RequestAllAsync, true,
            () => CancelGroupPreview(DisplayControl.Contrast));
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
        IsDiscovering = true; Status = "Reading monitor controls…";
        CancelManualRequests();
        try
        {
            var snapshots = await access.DiscoverAsync();
            if (closing) return;
            Monitors.Clear();
            foreach (var snapshot in snapshots) Monitors.Add(new(snapshot, RequestMonitorAsync));
            UpdateAggregates();
            Status = settingsWritable ? (HasAdjustable ? "Monitors ready" : "No adjustable DDC/CI monitors found")
                : "Saved settings need recovery. Open Settings to reset them; brightness controls are available.";
            foreach (var property in new[] { nameof(HasMonitors), nameof(HasNoMonitors), nameof(HasAdjustable), nameof(MonitorCount) }) Notify(property);
            foreach (var entry in Timers) entry.Refresh();
            log.Write("monitors-discovered", snapshots.Select(x => new { x.Name, x.Brightness, x.Contrast }));
            Changed?.Invoke();
            refreshed = true;
        }
        catch (Exception ex) { Status = "Could not refresh monitors: " + ex.Message; log.Write("discovery-failed", ex.Message); }
        finally { IsDiscovering = false; Notify(nameof(HasNoMonitors)); }
        if (refreshAgain && !closing) { refreshAgain = false; await RefreshAsync(); }
        else if (refreshed) await RunTimersAsync();
    }

    private async Task RequestMonitorAsync(MonitorSettingViewModel setting, int value)
    {
        if (!setting.IsAvailable || closing || IsDiscovering) return;
        setting.Pending?.Cancel();
        var pending = setting.Pending = new CancellationTokenSource();
        long version = ++setting.RequestVersion;
        setting.Status = "Applying…"; setting.IsApplying = true; setting.LastResult = null;
        try
        {
            var result = await access.SetAsync(setting.MonitorId, setting.Control, value, pending.Token);
            log.Write("control-manual", new { setting.MonitorName, result.Control, result.Requested, result.Actual, result.Success, result.Verified });
            if (closing || !Monitors.Any(x => x.Id == setting.MonitorId && ReferenceEquals(x.Setting(setting.Control), setting))) return;
            setting.Readback(result, version);
            if (version == setting.RequestVersion)
                Status = result.Success && result.Verified ? $"{setting.MonitorName} · {setting.Label} {result.Actual}%" : result.Message;
            UpdateAggregates(); Changed?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (version == setting.RequestVersion)
            {
                setting.Readback(new(setting.MonitorId, setting.Control, value, false, false, null, "Change failed: " + ex.Message), version);
                Status = "Monitor change failed: " + ex.Message;
            }
            log.Write("control-error", ex.Message);
        }
        finally
        {
            if (version == setting.RequestVersion) setting.IsApplying = false;
            if (ReferenceEquals(setting.Pending, pending)) setting.Pending = null;
            pending.Dispose();
        }
    }

    private async Task RequestAllAsync(MonitorSettingViewModel aggregate, int value)
    {
        if (closing || IsDiscovering) return;
        long version = ++aggregate.RequestVersion;
        aggregate.IsApplying = true; aggregate.Status = "Applying…";
        try
        {
            var targets = Monitors.Select(x => x.Setting(aggregate.Control)).Where(x => x.IsAvailable).ToList();
            await Task.WhenAll(targets.Select(x => x.SetAndCommitAsync(value)).ToList());
            if (version == aggregate.RequestVersion)
            {
                aggregate.Status = targets.Any(x => x.HasPendingChange) ? "Newer monitor previews are pending"
                    : targets.Any(x => x.LastResult is not { Success: true } || x.LastResult.Requested != value) ? "Some displays could not be updated"
                    : targets.Any(x => x.LastResult is not { Verified: true }) ? "Commands sent; some monitor readback is unavailable"
                    : $"{aggregate.Label} verified on {targets.Count} displays";
                Status = aggregate.Status;
            }
        }
        finally
        {
            if (version == aggregate.RequestVersion) aggregate.IsApplying = false;
            UpdateAggregates();
        }
    }

    private void CancelGroupPreview(DisplayControl control)
    { foreach (var monitor in Monitors) monitor.Setting(control).InvalidatePending(); }

    private void CancelManualRequests()
    {
        AllBrightness.InvalidatePending(); AllContrast.InvalidatePending();
        foreach (var monitor in Monitors) { monitor.Brightness.InvalidatePending(); monitor.Contrast.InvalidatePending(); }
    }

    private void UpdateAggregates()
    {
        AllBrightness.UpdateAggregate(Monitors.Select(x => x.Brightness));
        AllContrast.UpdateAggregate(Monitors.Select(x => x.Contrast));
    }

    public Task SetAllAsync(int value, DisplayControl control = DisplayControl.Brightness)
        => (control == DisplayControl.Brightness ? AllBrightness : AllContrast).SetAndCommitAsync(value);

    public Task CommitPreviewsAsync() => Task.WhenAll(
        new[] { AllBrightness, AllContrast }.Concat(Monitors.SelectMany(x => new[] { x.Brightness, x.Contrast }))
            .Select(x => x.CommitAsync()));

    public void CancelPreviews()
    {
        AllBrightness.CancelPreview(); AllContrast.CancelPreview();
        foreach (var monitor in Monitors) { monitor.Brightness.CancelPreview(); monitor.Contrast.CancelPreview(); }
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
                var setting = monitor.Brightness;
                setting.InvalidatePending();
                long version = ++setting.RequestVersion;
                setting.IsApplying = true;
                try
                {
                    var result = await access.SetAsync(write.MonitorId, write.Brightness);
                    string outcome = !result.Success ? "Failed" : result.Verified ? "Applied" : "Unverified";
                    if (outcomes[write.ScheduleId] != "Failed" && !(outcomes[write.ScheduleId] == "Unverified" && outcome == "Applied"))
                        outcomes[write.ScheduleId] = outcome;
                    setting.Readback(result, version);
                    if (setting.RequestVersion == version)
                    {
                        Status = result.Success && result.Verified ? $"Timer applied · {monitor.Name} · {result.Actual}%" : "Timer: " + result.Message;
                    }
                    log.Write("brightness-timer", new { write.ScheduleId, monitor.Name, result.Requested, result.Actual, result.Success, result.Verified });
                    UpdateAggregates();
                }
                catch (Exception ex) { outcomes[write.ScheduleId] = "Failed"; Status = "Timer failed: " + ex.Message; log.Write("timer-failed", ex.Message); }
                finally { if (setting.RequestVersion == version) setting.IsApplying = false; }
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
        closing = true; timer.Stop(); CancelManualRequests();
    }
}
