using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OmniLux.ViewModels;

namespace OmniLux.Services;

internal static class SmokeRunner
{
    public static async Task RunAsync(App app, MainWindow window, MainViewModel model, TrayService tray, string directory, bool demo)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>();
        int failures = 0;
        void Check(string name, bool success)
        { checks.Add(new { name, success }); if (!success) failures++; }
        try
        {
            window.ShowFromTray(); await Task.Delay(200);
            Check("native-window-created", new WindowInteropHelper(window).Handle != 0 && window.IsVisible);
            Check("tray-registration-requested", tray.IsVisible);
            Check("monitor-controls-bound", window.MonitorItems.Items.Count == model.Monitors.Count);
            Check("refresh-completed", !model.IsDiscovering);
            Check("accessible-all-slider", System.Windows.Automation.AutomationProperties.GetName(window.AllSlider) == "All monitors brightness");
            model.Theme = "Light"; await Task.Delay(100); Capture(window, Path.Combine(directory, "monitors-light.png"));
            model.Theme = "Dark"; await Task.Delay(100); Capture(window, Path.Combine(directory, "monitors-dark.png"));
            if (demo)
            {
                var adjustable = model.Monitors.First(x => x.CanControl);
                for (int brightness = 31; brightness <= 58; brightness++) adjustable.Brightness = brightness;
                await Task.Delay(700);
                Check("rapid-slider-final-readback", adjustable.Actual == 58 && !adjustable.IsApplying);
                model.SetAll(67); await Task.Delay(700);
                Check("all-displays-readback", model.Monitors.Where(x => x.CanControl).All(x => x.Actual == 67));
            }
            model.TimersPaused = true;
            window.TimersNav.IsChecked = true;
            Check("timer-page-navigation", window.TimersPage.Visibility == Visibility.Visible && window.MonitorsPage.Visibility == Visibility.Collapsed);
            bool invalidRejected = false, timerAccepted = false;
            _ = window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                var dialog = window.OwnedWindows.OfType<TimerDialog>().FirstOrDefault();
                if (dialog is null) return;
                try
                {
                    dialog.NameBox.Text = "Evening brightness";
                    dialog.KindPicker.SelectedIndex = 1;
                    dialog.TimeBox.Text = "25:00";
                    dialog.SaveTimerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            await Task.Delay(100); Capture(window, Path.Combine(directory, "timers-dark.png"));
            if (demo)
            {
                var monitor = model.Monitors.First(x => x.CanControl);
                var countdown = new OmniLux.Core.BrightnessSchedule
                {
                    Name = "Diagnostic countdown", Kind = OmniLux.Core.ScheduleKind.Countdown,
                    MonitorId = monitor.Id, Brightness = 29, NotBefore = DateTimeOffset.Now,
                    DueAt = DateTimeOffset.Now.AddSeconds(1)
                };
                model.AddOrUpdateTimer(countdown); model.TimersPaused = false;
                DateTimeOffset deadline = DateTimeOffset.Now.AddSeconds(8);
                while (monitor.Actual != 29 && DateTimeOffset.Now < deadline) await Task.Delay(100);
                Check("countdown-engine-readback", monitor.Actual == 29);
                Check("countdown-completed-once", !countdown.Enabled && countdown.LastOccurrence.HasValue);
                model.RemoveTimer(model.Timers.Single(x => x.Rule.Id == countdown.Id)); model.TimersPaused = true;
            }
            window.SettingsNav.IsChecked = true;
            Check("settings-page-navigation", window.SettingsPage.Visibility == Visibility.Visible);
            await Task.Delay(100); Capture(window, Path.Combine(directory, "settings-dark.png"));
            window.Close(); Check("close-keeps-tray", !window.IsVisible && tray.IsVisible);
            window.ShowFromTray(); Check("tray-reopens-window", window.IsVisible);
            window.WindowState = WindowState.Minimized; Check("minimize-keeps-tray", !window.IsVisible && tray.IsVisible);
            window.ShowFromTray(); Check("restore-from-minimized", window.WindowState == WindowState.Normal && window.IsVisible);
            Check("settings-persisted", File.Exists(Path.Combine(model.StoragePath, "settings.json")));
            window.Width = 740; window.Height = 510; window.MonitorsNav.IsChecked = true;
            await Task.Delay(100); Capture(window, Path.Combine(directory, "monitors-minimum.png"));
            Check("minimum-size-layout", window.MonitorItems.ActualWidth > 450);
        }
        catch (Exception ex) { checks.Add(new { name = "smoke-exception", success = false, error = ex.ToString() }); failures++; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "smoke.json"), JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.UtcNow, demo, failures, checks,
                monitors = model.Monitors.Select(x => new { x.Name, x.CanControl, x.Actual, x.Protocol }),
                hardwareWritesTested = false,
                startupRegistryTested = false,
                trayIconVisuallyTested = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            app.Quit();
        }
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
