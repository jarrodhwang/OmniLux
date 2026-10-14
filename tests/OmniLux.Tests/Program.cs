using OmniLux.Core;

var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> action) => tests.Add((name, action));
void Expect(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
var utc = TimeZoneInfo.Utc;
var now = new DateTimeOffset(2026, 10, 14, 21, 0, 0, TimeSpan.Zero);
BrightnessSchedule Daily(int brightness, string? id = null, int hour = 20) => new()
{ Kind = ScheduleKind.Daily, Brightness = brightness, DailyTime = new(hour, 0), MonitorId = id, NotBefore = now.AddDays(-5) };
BrightnessSchedule Once(int brightness = 40, string? id = null) => new()
{ Kind = ScheduleKind.Countdown, Brightness = brightness, MonitorId = id, DueAt = now.AddMinutes(-1), NotBefore = now.AddHours(-1) };

Test("Nonzero monitor minimum maps correctly", () =>
{ var range = new BrightnessRange(20, 60, 100); Expect(range.Percent == 50 && range.FromPercent(0) == 20 && range.FromPercent(100) == 100 && range.FromPercent(50) == 60); });
Test("Non-percent hardware ranges retain endpoints", () =>
{ var range = new BrightnessRange(0, 127, 255); Expect(range.Percent == 50 && range.FromPercent(100) == 255 && range.FromPercent(50) == 128); });
Test("Brightness rejects invalid monitor ranges", () =>
{ foreach (var range in new[] { new BrightnessRange(0, 0, 0), new BrightnessRange(10, 9, 20), new BrightnessRange(0, 21, 20) }) { Expect(!range.IsValid); Throws<InvalidOperationException>(() => range.FromPercent(50)); } });
Test("Brightness rejects out-of-range commands", () =>
{ var range = new BrightnessRange(0, 50, 100); Throws<ArgumentOutOfRangeException>(() => range.FromPercent(-1)); Throws<ArgumentOutOfRangeException>(() => range.FromPercent(101)); });
Test("Countdown only fires after deadline", () =>
{ var rule = Once(); Expect(SchedulePlanner.Plan([rule], now.AddMinutes(-2), utc, ["a"]).Writes.Count == 0); Expect(SchedulePlanner.Plan([rule], now, utc, ["a"]).Writes.Single().Brightness == 40); });
Test("Disabled timers never write", () =>
{ var rule = Once(); rule.Enabled = false; Expect(SchedulePlanner.Plan([rule], now, utc, ["a"]).Writes.Count == 0); });
Test("Daily rule does not apply an occurrence before creation", () =>
{ var rule = Daily(40) with { NotBefore = now }; Expect(SchedulePlanner.Plan([rule], now, utc, ["a"]).Writes.Count == 0); });
Test("Daily timer catches up after sleep", () =>
{ var rule = Daily(35); var plan = SchedulePlanner.Plan([rule], now, utc, ["a"]); Expect(plan.Writes.Single().Brightness == 35 && plan.Consumed.Single().Occurrence.Hour == 20); });
Test("Yesterday's missed daily slot survives an early morning resume", () =>
{ var plan = SchedulePlanner.Plan([Daily(35)], now.AddHours(10), utc, ["a"]); Expect(plan.Writes.Single().Occurrence.Date == now.Date); });
Test("Repeated ticks do not repeat consumed occurrences", () =>
{ var rule = Daily(35); rule.LastOccurrence = SchedulePlanner.Plan([rule], now, utc, ["a"]).Consumed.Single().Occurrence; Expect(SchedulePlanner.Plan([rule], now.AddSeconds(5), utc, ["a"]).Writes.Count == 0); });
Test("Daily rules fire again the following day", () =>
{ var rule = Daily(35) with { LastOccurrence = now.AddHours(-1) }; Expect(SchedulePlanner.Plan([rule], now.AddDays(1), utc, ["a"]).Writes.Count == 1); });
Test("Multi-day sleep applies one latest slot", () =>
{ var plan = SchedulePlanner.Plan([Daily(35)], now.AddDays(10), utc, ["a"]); Expect(plan.Writes.Count == 1 && plan.Writes[0].Occurrence.Date == now.AddDays(10).Date); });
Test("All-monitor timer targets every adjustable monitor", () =>
{ var plan = SchedulePlanner.Plan([Once()], now, utc, ["a", "b"]); Expect(plan.Writes.Count == 2); });
Test("Targeted timer cannot drift to another monitor", () =>
{ var plan = SchedulePlanner.Plan([Once(id: "a")], now, utc, ["b"]); Expect(plan.Writes.Count == 0 && plan.Consumed.Count == 0); });
Test("Disconnected timer stays pending until reconnect", () =>
{ var rule = Once(id: "a"); Expect(SchedulePlanner.Plan([rule], now, utc, []).Consumed.Count == 0); Expect(SchedulePlanner.Plan([rule], now.AddHours(1), utc, ["a"]).Writes.Count == 1); });
Test("Latest missed setting wins separately for each monitor", () =>
{ var plan = SchedulePlanner.Plan([Daily(70, hour: 9), Daily(30, "a", 20)], now, utc, ["a", "b"]); Expect(plan.Writes.Single(x => x.MonitorId == "a").Brightness == 30); Expect(plan.Writes.Single(x => x.MonitorId == "b").Brightness == 70); Expect(plan.Consumed.Count == 2); });
Test("Invalid persisted brightness never reaches a monitor", () =>
{ Expect(SchedulePlanner.Plan([Once(101), Once(-1)], now, utc, ["a"]).Writes.Count == 0); });
Test("Next daily occurrence moves forward after consumption", () =>
{ var rule = Daily(35) with { LastOccurrence = now.AddHours(-1) }; Expect(SchedulePlanner.NextOccurrence(rule, now, utc)?.Date == now.AddDays(1).Date); });
Test("Spring DST gap uses first valid local minute", () =>
{ var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); var slot = SchedulePlanner.ResolveLocal(new(2026, 3, 8), new(2, 30), zone); Expect(slot.Hour == 3 && slot.Minute == 0 && slot.Offset == TimeSpan.FromHours(-7)); });
Test("Fall DST repeat chooses later occurrence once", () =>
{ var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); var slot = SchedulePlanner.ResolveLocal(new(2026, 11, 1), new(1, 30), zone); Expect(slot.Offset == TimeSpan.FromHours(-8)); });
Test("Wall-clock daily schedules respect the selected time zone", () =>
{ var zone = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"); var rule = Daily(35); var plan = SchedulePlanner.Plan([rule], new(2026, 10, 14, 11, 1, 0, TimeSpan.Zero), zone, ["a"]); Expect(plan.Writes.Single().Occurrence.UtcDateTime.Hour == 11); });

string temp = Path.Combine(Path.GetTempPath(), "OmniLux-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
Test("Settings persist timers and consumed occurrences", () =>
{ var store = new SettingsStore(Path.Combine(temp, "roundtrip")); var rule = Daily(35) with { LastOccurrence = now.AddHours(-1) }; store.Save(new() { Theme = "Dark", Schedules = [rule] }); var loaded = store.Load(); Expect(loaded.Warning is null && loaded.Settings.Theme == "Dark" && loaded.Settings.Schedules.Single().LastOccurrence == rule.LastOccurrence); });
Test("Atomic save preserves previous settings as backup", () =>
{ var store = new SettingsStore(Path.Combine(temp, "backup")); store.Save(new() { Theme = "Light" }); store.Save(new() { Theme = "Dark" }); Expect(store.Load().Settings.Theme == "Dark" && File.ReadAllText(store.FilePath + ".bak").Contains("Light")); });
Test("Corrupt settings are preserved and timers cannot run", () =>
{ var dir = Path.Combine(temp, "corrupt"); Directory.CreateDirectory(dir); var store = new SettingsStore(dir); File.WriteAllText(store.FilePath, "{broken"); var loaded = store.Load(); Expect(loaded.Warning is not null && loaded.Settings.Schedules.Count == 0 && File.ReadAllText(store.FilePath) == "{broken"); });
Test("Future settings schema is preserved", () =>
{ var dir = Path.Combine(temp, "future"); Directory.CreateDirectory(dir); var store = new SettingsStore(dir); File.WriteAllText(store.FilePath, "{\"SchemaVersion\":99}"); Expect(store.Load().Warning is not null && File.ReadAllText(store.FilePath).Contains("99")); });
Test("Invalid timer entries cannot bypass settings validation", () =>
{ var store = new SettingsStore(Path.Combine(temp, "invalid")); store.Save(new() { Schedules = [Once(120)] }); var loaded = store.Load(); Expect(loaded.Warning is not null && loaded.Settings.Schedules.Count == 0); });

AsyncTest("DDC work is serialized across writes and rediscovery", async () =>
{ var backend = new FakeBackend(); await using var access = new MonitorAccess(backend); await Task.WhenAll(Enumerable.Range(0, 12).Select(i => i % 3 == 0 ? (Task)access.DiscoverAsync() : access.SetAsync("a", i))); Expect(backend.Peak == 1 && backend.Calls == 12); });
AsyncTest("Cancellation removes queued writes without interrupting in-flight handles", async () =>
{ var backend = new FakeBackend(); await using var access = new MonitorAccess(backend); var first = access.SetAsync("a", 30); await backend.Started.Task; using var cancel = new CancellationTokenSource(); var queued = access.SetAsync("a", 80, cancel.Token); cancel.Cancel(); try { await queued; throw new Exception("Queued write was not canceled"); } catch (OperationCanceledException) { } await first; Expect(backend.Calls == 1); });
AsyncTest("Backend failures release the serialization gate", async () =>
{ var backend = new FakeBackend { FailNext = true }; await using var access = new MonitorAccess(backend); try { await access.SetAsync("a", 30); } catch (IOException) { } var result = await access.SetAsync("a", 60); Expect(result.Success); });
AsyncTest("Disposal waits for in-flight native work", async () =>
{ var backend = new FakeBackend(); var access = new MonitorAccess(backend); var write = access.SetAsync("a", 30); await backend.Started.Task; var dispose = access.DisposeAsync().AsTask(); Expect(!dispose.IsCompleted); await write; await dispose; Expect(backend.Disposed && !backend.DisposedDuringWork); });

int failed = 0;
try
{
    foreach (var (name, run) in tests)
    {
        try { await run(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
}
finally { Directory.Delete(temp, true); }
Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed");
return failed == 0 ? 0 : 1;

sealed class FakeBackend : IMonitorBackend
{
    private int active;
    public int Peak, Calls;
    public bool FailNext, Disposed, DisposedDuringWork;
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private T Work<T>(Func<T> action)
    {
        int current = Interlocked.Increment(ref active); Peak = Math.Max(Peak, current); Interlocked.Increment(ref Calls);
        Started.TrySetResult();
        try { Thread.Sleep(40); if (FailNext) { FailNext = false; throw new IOException("Driver failed"); } return action(); }
        finally { Interlocked.Decrement(ref active); }
    }
    public IReadOnlyList<MonitorSnapshot> Discover() => Work<IReadOnlyList<MonitorSnapshot>>(() => []);
    public BrightnessResult SetBrightness(string id, int percent) => Work(() => new BrightnessResult(id, percent, true, true, percent, "Verified"));
    public void Dispose() { DisposedDuringWork = active > 0; Disposed = true; }
}
