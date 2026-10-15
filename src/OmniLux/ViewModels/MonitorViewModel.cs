using OmniLux.Core;

namespace OmniLux.ViewModels;

public sealed class MonitorViewModel
{
    public MonitorSnapshot Snapshot { get; }
    public string Id => Snapshot.Id;
    public string Name => Snapshot.Name;
    public string Connection => Snapshot.Connection + (Snapshot.IsPrimary ? " · Primary" : "");
    public bool CanControl => Brightness.IsAvailable;
    public bool HasAnyControl => Brightness.IsAvailable || Contrast.IsAvailable;
    public MonitorSettingViewModel Brightness { get; }
    public MonitorSettingViewModel Contrast { get; }
    public MonitorSettingViewModel Setting(DisplayControl control) => control == DisplayControl.Brightness ? Brightness : Contrast;
    public MonitorViewModel(MonitorSnapshot snapshot, Func<MonitorSettingViewModel, int, Task> request)
    {
        Snapshot = snapshot;
        Brightness = new(snapshot.Id, snapshot.Name, DisplayControl.Brightness, snapshot.Brightness, request);
        Contrast = new(snapshot.Id, snapshot.Name, DisplayControl.Contrast, snapshot.Contrast, request);
    }
}
