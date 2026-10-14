using OmniLux.Core;

namespace OmniLux.ViewModels;

public sealed class MonitorViewModel : ObservableObject
{
    private readonly Action<MonitorViewModel, int> request;
    private int brightness;
    private int? actual;
    private string status;
    private bool applying;
    public MonitorSnapshot Snapshot { get; }
    public string Id => Snapshot.Id;
    public string Name => Snapshot.Name;
    public bool CanControl => Snapshot.CanControl;
    public string Connection => Snapshot.Connection + (Snapshot.IsPrimary ? " · Primary" : "");
    public string Protocol => Snapshot.Protocol;
    public bool IsUnavailable => !CanControl;
    public bool IsApplying { get => applying; set => Set(ref applying, value); }
    public string Status { get => status; set => Set(ref status, value); }
    public int Brightness
    {
        get => brightness;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (Set(ref brightness, value)) { Notify(nameof(BrightnessText)); request(this, value); }
        }
    }
    public string BrightnessText => CanControl ? $"{Brightness}%" : "—";
    public int? Actual => actual;
    public long RequestVersion { get; set; }
    public CancellationTokenSource? Pending { get; set; }

    public MonitorViewModel(MonitorSnapshot snapshot, Action<MonitorViewModel, int> request)
    {
        Snapshot = snapshot;
        this.request = request;
        brightness = snapshot.Brightness ?? 50;
        actual = snapshot.Brightness;
        status = snapshot.Status;
    }

    public void Readback(BrightnessResult result, bool updateSlider)
    {
        if (result.Actual.HasValue) actual = result.Actual;
        int? sliderValue = result.Actual ?? (!result.Success ? actual : null);
        if (updateSlider && sliderValue.HasValue)
        {
            brightness = sliderValue.Value;
            Notify(nameof(Brightness)); Notify(nameof(BrightnessText));
        }
        Status = result.Message;
        Notify(nameof(Actual));
    }
}
