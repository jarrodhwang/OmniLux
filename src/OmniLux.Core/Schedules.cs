namespace OmniLux.Core;

public enum ScheduleKind { Countdown, Daily }

public sealed record BrightnessSchedule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Brightness timer";
    public bool Enabled { get; set; } = true;
    public ScheduleKind Kind { get; init; }
    public int Brightness { get; init; } = 40;
    // Null means every currently connected adjustable display. IDs never depend on list order.
    public string? MonitorId { get; init; }
    public int DelayMinutes { get; init; } = 30;
    public TimeOnly DailyTime { get; init; } = new(20, 0);
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset NotBefore { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastOccurrence { get; set; }
    public string? LastOutcome { get; set; }
}

public sealed record ScheduledWrite(string MonitorId, int Brightness, Guid ScheduleId, DateTimeOffset Occurrence);
public sealed record ConsumedOccurrence(Guid ScheduleId, DateTimeOffset Occurrence);
public sealed record ScheduleBatch(IReadOnlyList<ScheduledWrite> Writes, IReadOnlyList<ConsumedOccurrence> Consumed);

public static class SchedulePlanner
{
    public static ScheduleBatch Plan(IEnumerable<BrightnessSchedule> schedules, DateTimeOffset now,
        TimeZoneInfo zone, IEnumerable<string> availableMonitorIds)
    {
        var available = availableMonitorIds.ToHashSet(StringComparer.Ordinal);
        var due = schedules.Where(x => x.Enabled && IsValid(x))
            .Select(x => (Rule: x, Occurrence: LatestOccurrence(x, now, zone)))
            .Where(x => x.Occurrence is { } occurrence && occurrence >= x.Rule.NotBefore &&
                occurrence <= now && (x.Rule.LastOccurrence is null || occurrence > x.Rule.LastOccurrence))
            .OrderBy(x => x.Occurrence).ThenBy(x => x.Rule.NotBefore).ThenBy(x => x.Rule.Id);
        var writes = new Dictionary<string, ScheduledWrite>(StringComparer.Ordinal);
        var consumed = new List<ConsumedOccurrence>();
        foreach (var (rule, occurrence) in due)
        {
            var targets = rule.MonitorId is { } id ? available.Where(x => x == id) : available;
            var targetList = targets.ToList();
            // Leave unavailable targets pending so reconnecting can still complete a countdown.
            if (targetList.Count == 0) continue;
            foreach (var target in targetList)
                writes[target] = new(target, rule.Brightness, rule.Id, occurrence!.Value);
            consumed.Add(new(rule.Id, occurrence!.Value));
        }
        // On resume, only the latest applicable setting reaches each monitor.
        return new(writes.Values.ToList(), consumed);
    }

    public static bool IsValid(BrightnessSchedule rule) => rule.Id != Guid.Empty &&
        rule.Brightness is >= 0 and <= 100 && rule.DelayMinutes is >= 1 and <= 10080 &&
        Enum.IsDefined(rule.Kind) && (rule.Kind != ScheduleKind.Countdown || rule.DueAt.HasValue);

    public static DateTimeOffset? NextOccurrence(BrightnessSchedule rule, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!rule.Enabled || !IsValid(rule)) return null;
        if (rule.Kind == ScheduleKind.Countdown) return rule.DueAt;
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var next = ResolveLocal(day, rule.DailyTime, zone);
        if (next < rule.NotBefore || next <= (rule.LastOccurrence ?? DateTimeOffset.MinValue))
            return ResolveLocal(day.AddDays(1), rule.DailyTime, zone);
        // A missed, unconsumed occurrence remains due rather than silently disappearing.
        return next;
    }

    private static DateTimeOffset? LatestOccurrence(BrightnessSchedule rule, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (rule.Kind == ScheduleKind.Countdown) return rule.DueAt;
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var slot = ResolveLocal(day, rule.DailyTime, zone);
        return slot <= now ? slot : ResolveLocal(day.AddDays(-1), rule.DailyTime, zone);
    }

    public static DateTimeOffset ResolveLocal(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        // Spring-forward: run at the first valid minute after the missing wall-clock time.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        // Fall-back: select the later occurrence so this timer runs only once that day.
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Min() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
