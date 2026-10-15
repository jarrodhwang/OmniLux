using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OmniLux.Core;
using OmniLux.Native;
using OmniLux.ViewModels;

namespace OmniLux.Services;

internal static class SmokeRunner
{
    public static async Task RunAsync(App app, MainWindow window, MainViewModel model, TrayService tray,
        string directory, DemoMonitorBackend? demoBackend)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); int failures = 0;
        void Check(string name, bool success) { checks.Add(new { name, success }); if (!success) failures++; }
        try
        {
            window.ShowFromTray(); await Task.Delay(150); window.UpdateLayout();
            Check("native-window-created", new WindowInteropHelper(window).Handle != 0 && window.IsVisible);
            Check("tray-registration-requested", tray.IsVisible);
            Check("monitor-controls-bound", window.MonitorItems.Items.Count == model.Monitors.Count);
            Check("refresh-completed", !model.IsDiscovering);
            Check("accessible-brightness-slider", FindSlider(window, "All displays brightness") is not null);
            model.Theme = "Light"; await Task.Delay(70); Capture(window, Path.Combine(directory, "monitors-light.png"));
            model.Theme = "Dark"; await Task.Delay(70); Capture(window, Path.Combine(directory, "monitors-dark.png"));
            if (demoBackend is not null)
            {
                var monitor = model.Monitors.First(x => x.CanControl);
                var slider = FindSlider(window, monitor.Brightness.AccessibleName)!;
                int before = demoBackend.WriteCount;
                Pointer(slider, true);
                foreach (int value in new[] { 31, 42, 57, 58 }) { monitor.Brightness.Value = value; await Task.Delay(220); }
                Check("held-drag-does-not-write", demoBackend.WriteCount == before && monitor.Brightness.Actual == 72);
                slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                Check("leave-during-held-drag-does-not-write", demoBackend.WriteCount == before);
                Pointer(slider, false); await WaitUntil(() => !monitor.Brightness.IsApplying);
                Check("release-writes-final-value-once", monitor.Brightness.Actual == 58 && demoBackend.WriteCount == before + 1);
                Pointer(slider, false); await Task.Delay(50);
                Check("duplicate-release-is-not-a-second-write", demoBackend.WriteCount == before + 1);
                monitor.Brightness.Value = 63;
                slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(slider)!, 0, Key.Right) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
                await WaitUntil(() => !monitor.Brightness.IsApplying);
                Check("keyboard-release-applies", monitor.Brightness.Actual == 63);
                var allSlider = FindSlider(window, "All displays brightness")!;
                before = demoBackend.WriteCount; Pointer(allSlider, true);
                foreach (int value in new[] { 11, 35, 53, 67 }) { model.AllBrightness.Value = value; await Task.Delay(220); }
                Check("all-displays-drag-does-not-write", demoBackend.WriteCount == before);
                Pointer(allSlider, false); await WaitUntil(() => !model.AllBrightness.IsApplying);
                Check("all-displays-commit-writes-one-per-supported-monitor", demoBackend.WriteCount == before + 2 && model.Monitors.Where(x => x.CanControl).All(x => x.Brightness.Actual == 67));
                var contrast = monitor.Contrast;
                var contrastSlider = FindSlider(window, contrast.AccessibleName)!;
                before = demoBackend.WriteCount; contrast.Value = 66; await Task.Delay(220);
                Check("contrast-preview-does-not-write", demoBackend.WriteCount == before);
                contrastSlider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                await WaitUntil(() => !contrast.IsApplying);
                Check("contrast-mouse-leave-applies", contrast.Actual == 66 && demoBackend.WriteCount == before + 1);
                Check("independent-contrast-support", model.Monitors.Single(x => x.Id == "demo:desk").Brightness.IsAvailable && !model.Monitors.Single(x => x.Id == "demo:desk").Contrast.IsAvailable);
                before = demoBackend.WriteCount; await model.SetAllAsync(64, DisplayControl.Contrast);
                Check("all-contrast-skips-unsupported-monitors", demoBackend.WriteCount == before + 1 && contrast.Actual == 64);
                demoBackend.RejectControl = DisplayControl.Contrast;
                await contrast.SetAndCommitAsync(90);
                Check("rejected-contrast-rolls-back-preview", contrast.Value == 64 && contrast.Actual == 64 && contrast.Status.Contains("rejected", StringComparison.Ordinal));
                demoBackend.RejectControl = null;
            }

            model.TimersPaused = true; window.TimersNav.IsChecked = true;
            Check("timer-page-navigation", window.TimersPage.Visibility == Visibility.Visible && window.MonitorsPage.Visibility == Visibility.Collapsed);
            bool invalidRejected = false, timerAccepted = false;
            _ = window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                var dialog = window.OwnedWindows.OfType<TimerDialog>().FirstOrDefault();
                if (dialog is null) return;
                try
                {
                    dialog.NameBox.Text = "Evening brightness"; dialog.KindPicker.SelectedIndex = 1;
                    dialog.TimeBox.Text = "25:00"; dialog.SaveTimerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    invalidRejected = dialog.Result is null && dialog.ErrorText.Text.Length > 0;
                    dialog.TimeBox.Text = "20:00"; dialog.BrightnessSlider.Value = 35;
                    Capture(dialog, Path.Combine(directory, "timer-dialog.png"));
                    dialog.SaveTimerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    timerAccepted = dialog.Result?.Brightness == 35;
                }
                catch { dialog.DialogResult = false; }
            }));
            window.AddTimerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("timer-invalid-time-rejected", invalidRejected);
            Check("timer-dialog-saved", timerAccepted && model.Timers.Count == 1);
            Capture(window, Path.Combine(directory, "timers-dark.png"));
            if (demoBackend is not null)
            {
                model.Timers.Single().Enabled = false;
                var monitor = model.Monitors.First(x => x.CanControl);
                var countdown = new BrightnessSchedule { Name = "Diagnostic countdown", Kind = ScheduleKind.Countdown,
                    MonitorId = monitor.Id, Brightness = 29, NotBefore = DateTimeOffset.Now, DueAt = DateTimeOffset.Now.AddSeconds(1) };
                model.AddOrUpdateTimer(countdown); model.TimersPaused = false;
                await WaitUntil(() => monitor.Brightness.Actual == 29, 8000);
                Check("countdown-engine-readback", monitor.Brightness.Actual == 29);
                Check("countdown-completed-once", !countdown.Enabled && countdown.LastOccurrence.HasValue && countdown.LastOutcome == "Applied");
                model.RemoveTimer(model.Timers.Single(x => x.Rule.Id == countdown.Id)); model.TimersPaused = true;
            }
            window.SettingsNav.IsChecked = true;
            Check("settings-page-navigation", window.SettingsPage.Visibility == Visibility.Visible);
            Capture(window, Path.Combine(directory, "settings-dark.png"));
            await model.RefreshAsync();
            window.Close(); Check("close-keeps-tray", !window.IsVisible && tray.IsVisible);
            tray.ShowQuickControls(false); await Task.Delay(120);
            var quick = tray.QuickControls!;
            Check("tray-panel-without-main-window", quick.IsVisible && !window.IsVisible);
            Check("tray-panel-has-each-monitor", quick.MonitorItems.Items.Count == model.Monitors.Count);
            Check("tray-panel-fits-work-area", quick.FitsWorkArea());
            model.Theme = "Light"; Capture(quick, Path.Combine(directory, "tray-light.png"));
            model.Theme = "Dark"; Capture(quick, Path.Combine(directory, "tray-dark.png"));
            if (demoBackend is not null)
            {
                var monitor = model.Monitors.First(x => x.CanControl);
                var slider = FindSlider(quick, monitor.Brightness.AccessibleName)!;
                int before = demoBackend.WriteCount;
                monitor.Brightness.Value = 62;
                slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                await WaitUntil(() => !monitor.Brightness.IsApplying);
                Check("tray-slider-shares-verified-main-state", monitor.Brightness.Actual == 62 && demoBackend.WriteCount == before + 1 && !window.IsVisible);
                before = demoBackend.WriteCount; monitor.Brightness.Value = 12;
                quick.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(quick)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                await Task.Delay(70);
                Check("escape-cancels-preview-without-hardware-write", !quick.IsVisible && monitor.Brightness.Value == 62 && demoBackend.WriteCount == before);
            }
            quick.Dismiss(); window.ShowFromTray(); Check("explicit-open-main", window.IsVisible);
            window.WindowState = WindowState.Minimized; Check("minimize-keeps-tray", !window.IsVisible && tray.IsVisible);
            window.ShowFromTray(); Check("restore-from-minimized", window.WindowState == WindowState.Normal && window.IsVisible);
            Check("settings-persisted", File.Exists(Path.Combine(model.StoragePath, "settings.json")));
            window.Width = 740; window.Height = 510; window.MonitorsNav.IsChecked = true;
            Capture(window, Path.Combine(directory, "monitors-minimum.png"));
            Check("minimum-size-layout", window.MonitorItems.ActualWidth > 450);
        }
        catch (Exception ex) { checks.Add(new { name = "smoke-exception", success = false, error = ex.ToString() }); failures++; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "smoke.json"), JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.UtcNow, demo = demoBackend is not null, failures, checks,
                monitors = model.Monitors.Select(x => new { x.Name, brightness = x.Brightness.Actual, contrast = x.Contrast.Actual, brightnessSupported = x.Brightness.IsAvailable, contrastSupported = x.Contrast.IsAvailable }),
                hardwareWritesTested = false, startupRegistryTested = false, trayIconVisuallyTested = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            app.Quit();
        }
    }
    private static void Pointer(Slider slider, bool down) => slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
    { RoutedEvent = down ? UIElement.PreviewMouseLeftButtonDownEvent : UIElement.PreviewMouseLeftButtonUpEvent });
    private static async Task WaitUntil(Func<bool> condition, int timeout = 2500)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeout);
        while (!condition() && DateTimeOffset.UtcNow < deadline) await Task.Delay(40);
    }
    private static Slider? FindSlider(DependencyObject root, string name)
    {
        if (root is Slider slider && AutomationProperties.GetName(slider) == name) return slider;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindSlider(VisualTreeHelper.GetChild(root, i), name) is { } found) return found;
        return null;
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(window);
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        image.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
