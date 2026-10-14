using OmniLux.Core;

namespace OmniLux.ViewModels;

public sealed class TimerViewModel(BrightnessSchedule rule, Func<string?, string> targetName,
    Action<TimerViewModel, bool> enabledChanged) : ObservableObject
{
    public BrightnessSchedule Rule { get; } = rule;
    public string Name => Rule.Name;
    public string Target => targetName(Rule.MonitorId);
    public string Brightness => $"{Rule.Brightness}%";
    public string Kind => Rule.Kind == ScheduleKind.Daily ? "Every day" : "Countdown";
    public string TimeText => Rule.Kind == ScheduleKind.Daily ? Rule.DailyTime.ToString("HH:mm")
        : Rule.DueAt?.LocalDateTime.ToString("MMM d · HH:mm") ?? "—";
    public bool Enabled
    {
        get => Rule.Enabled;
        set { if (Rule.Enabled != value) enabledChanged(this, value); }
    }
    public string State => !Rule.Enabled
        ? (Rule.Kind == ScheduleKind.Countdown && Rule.LastOccurrence.HasValue ? LastRun : "Disabled")
        : Rule.Kind == ScheduleKind.Countdown && Rule.DueAt <= DateTimeOffset.Now ? "Waiting for monitor"
        : Rule.LastOutcome is "Failed" or "Pending" or "Unverified" ? LastRun : "Scheduled";
    private string LastRun => Rule.LastOutcome switch
    {
        "Applied" => "Completed", "Failed" => "Last run failed", "Unverified" => "Readback unavailable",
        "Superseded" => "Superseded", _ => "Check last run"
    };
    public void Refresh() { Notify(nameof(Enabled)); Notify(nameof(State)); Notify(nameof(TimeText)); Notify(nameof(Target)); }
}
