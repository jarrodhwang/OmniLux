using System.Windows.Input;
using OmniLux.Core;

namespace OmniLux.ViewModels;

/// <summary>Slider values are previews. Only CommitAsync sends a request to the hardware worker.</summary>
public sealed class MonitorSettingViewModel : ObservableObject
{
    private readonly Func<MonitorSettingViewModel, int, Task> request;
    private readonly Action? previewChanged;
    private int value;
    private int? actual;
    private bool available, dirty, applying, mixed;
    private long lastReadbackVersion;
    private string status;
    public string MonitorId { get; }
    public string MonitorName { get; }
    public DisplayControl Control { get; }
    public string Label => Control.ToString();
    public string AccessibleName => $"{MonitorName} {Label.ToLowerInvariant()}";
    public bool IsAggregate { get; }
    public bool IsAvailable { get => available; private set => Set(ref available, value); }
    public bool IsUnavailable => !IsAvailable;
    public bool HasPendingChange => dirty;
    public bool IsApplying { get => applying; set { if (Set(ref applying, value)) Notify(nameof(ValueText)); } }
    public string Status { get => status; set { if (Set(ref status, value)) Notify(nameof(HasStatus)); } }
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public MonitorControlResult? LastResult { get; internal set; }
    public int? Actual => actual;
    public string ValueText => !IsAvailable ? "Unavailable" : !dirty && !IsApplying && mixed ? "Mixed" : $"{Value}%";
    public ICommand CommitCommand { get; }
    public long RequestVersion { get; set; }
    public CancellationTokenSource? Pending { get; set; }
    public int Value
    {
        get => value;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (this.value == value) return;
            InvalidatePending();
            this.value = value; dirty = true;
            Status = "Release or leave the slider to apply";
            Notify(); Notify(nameof(ValueText)); Notify(nameof(HasPendingChange));
            previewChanged?.Invoke();
        }
    }

    public MonitorSettingViewModel(string id, string name, DisplayControl control, ControlSnapshot snapshot,
        Func<MonitorSettingViewModel, int, Task> request, bool aggregate = false, Action? previewChanged = null)
    {
        MonitorId = id; MonitorName = name; Control = control; this.request = request;
        this.previewChanged = previewChanged; IsAggregate = aggregate;
        actual = snapshot.Value; value = actual ?? 50; available = snapshot.CanControl; status = snapshot.Status;
        CommitCommand = new CommitAction(() => _ = CommitAsync());
    }

    public Task CommitAsync(bool force = false)
    {
        if (!IsAvailable || (!dirty && !force)) return Task.CompletedTask;
        dirty = false; Notify(nameof(HasPendingChange)); Notify(nameof(ValueText));
        return request(this, Value);
    }
    public Task SetAndCommitAsync(int percent, bool force = true) { Value = percent; return CommitAsync(force); }

    public void InvalidatePending()
    {
        RequestVersion++; Pending?.Cancel(); IsApplying = false;
    }

    public void CancelPreview()
    {
        if (!dirty) return;
        dirty = false;
        if (actual.HasValue) value = actual.Value;
        Status = "Preview cancelled";
        Notify(nameof(Value)); Notify(nameof(ValueText)); Notify(nameof(HasPendingChange));
    }

    public void Readback(MonitorControlResult result, long version)
    {
        if (version < lastReadbackVersion) return;
        lastReadbackVersion = version;
        if (result.Actual.HasValue) actual = result.Actual;
        bool current = version == RequestVersion && !dirty;
        if (current)
        {
            LastResult = result;
            int? confirmed = result.Actual ?? (!result.Success ? actual : null);
            if (confirmed.HasValue) { value = confirmed.Value; Notify(nameof(Value)); }
            Status = result.Message;
        }
        Notify(nameof(Actual)); Notify(nameof(ValueText));
    }

    public void UpdateAggregate(IEnumerable<MonitorSettingViewModel> controls)
    {
        var supported = controls.Where(x => x.IsAvailable).ToList();
        IsAvailable = supported.Count > 0; Notify(nameof(IsUnavailable));
        var values = supported.Select(x => x.Actual).Distinct().ToList();
        mixed = values.Count > 1;
        actual = values.FirstOrDefault();
        if (!dirty && !IsApplying && actual.HasValue) { value = actual.Value; Notify(nameof(Value)); }
        Notify(nameof(Actual)); Notify(nameof(ValueText));
    }

    private sealed class CommitAction(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
