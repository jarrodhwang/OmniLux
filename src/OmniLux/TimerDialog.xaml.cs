using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OmniLux.Core;
using OmniLux.Services;
using OmniLux.ViewModels;

namespace OmniLux;

public partial class TimerDialog : Window
{
    public sealed record TargetOption(string? Id, string Name)
    {
        public override string ToString() => Name;
    }
    private readonly BrightnessSchedule? previous;
    public BrightnessSchedule? Result { get; private set; }

    public TimerDialog(IEnumerable<MonitorViewModel> monitors, BrightnessSchedule? previous = null)
    {
        InitializeComponent(); this.previous = previous;
        var targets = new List<TargetOption> { new(null, "All adjustable displays") };
        targets.AddRange(monitors.Where(x => x.CanControl && !x.Id.StartsWith("volatile:", StringComparison.Ordinal)).Select(x => new TargetOption(x.Id, x.Name)));
        if (previous?.MonitorId is { } id && targets.All(x => x.Id != id)) targets.Add(new(id, "Saved display · disconnected"));
        TargetPicker.ItemsSource = targets;
        TargetPicker.SelectedItem = targets.FirstOrDefault(x => x.Id == previous?.MonitorId) ?? targets[0];
        if (previous is not null)
        {
            HeadingText.Text = "Edit brightness timer"; NameBox.Text = previous.Name;
            MinutesBox.Text = previous.DelayMinutes.ToString(CultureInfo.InvariantCulture);
            TimeBox.Text = previous.DailyTime.ToString("HH:mm"); BrightnessSlider.Value = previous.Brightness;
        }
        KindPicker.SelectedIndex = previous?.Kind == ScheduleKind.Daily ? 1 : 0;
        SourceInitialized += (_, _) => ThemeManager.ApplyWindow(this);
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Kind_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (CountdownFields is null || DailyFields is null) return;
        CountdownFields.Visibility = KindPicker.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        DailyFields.Visibility = KindPicker.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Brightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    { if (PercentText is not null) PercentText.Text = $"{(int)Math.Round(args.NewValue)}%"; }
    private void Field_TextChanged(object sender, TextChangedEventArgs args)
    { if (ErrorText is not null) ErrorText.Text = ""; }

    private void Save_Click(object sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ErrorText.Text = "Enter a timer name."; NameBox.Focus(); return; }
        int minutes = 30;
        TimeOnly time = new(20, 0);
        if (KindPicker.SelectedIndex == 0 && (!int.TryParse(MinutesBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out minutes) || minutes is < 1 or > 10080))
        { ErrorText.Text = "Enter 1–10080 minutes (up to 7 days)."; MinutesBox.Focus(); return; }
        if (KindPicker.SelectedIndex == 1 && !TimeOnly.TryParseExact(TimeBox.Text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        { ErrorText.Text = "Enter a 24-hour time, such as 20:00."; TimeBox.Focus(); return; }
        DateTimeOffset now = DateTimeOffset.Now;
        Result = new()
        {
            Id = previous?.Id ?? Guid.NewGuid(), Name = NameBox.Text.Trim(), Enabled = true,
            Kind = KindPicker.SelectedIndex == 0 ? ScheduleKind.Countdown : ScheduleKind.Daily,
            Brightness = (int)Math.Round(BrightnessSlider.Value), MonitorId = (TargetPicker.SelectedItem as TargetOption)?.Id,
            DelayMinutes = minutes, DailyTime = time, NotBefore = now,
            DueAt = KindPicker.SelectedIndex == 0 ? now.AddMinutes(minutes) : null
        };
        DialogResult = true;
    }
}
