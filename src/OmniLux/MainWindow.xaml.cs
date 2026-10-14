using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using OmniLux.Services;
using OmniLux.ViewModels;

namespace OmniLux;

public partial class MainWindow : Window
{
    private bool exiting;
    private HwndSource? source;
    private CancellationTokenSource? refreshPending;
    private readonly MainViewModel model;

    public MainWindow(MainViewModel model)
    {
        InitializeComponent(); this.model = model; DataContext = model;
        ThemePicker.ItemsSource = new[] { "System", "Light", "Dark" };
        SystemEvents.PowerModeChanged += PowerModeChanged;
        SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
    }

    public void EnsureNativeWindow() => new WindowInteropHelper(this).EnsureHandle();
    private void Window_SourceInitialized(object? sender, EventArgs args)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WindowMessage);
        ThemeManager.ApplyWindow(this);
    }

    private nint WindowMessage(nint hwnd, int message, nint wparam, nint lparam, ref bool handled)
    {
        if (message is 0x007E or 0x0219) ScheduleRefresh(); // WM_DISPLAYCHANGE / WM_DEVICECHANGE
        return 0;
    }

    private void PowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(() => ScheduleRefresh(true));
    }
    private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
        => Dispatcher.BeginInvoke(() => ThemeManager.Apply(model.Theme));

    private async void ScheduleRefresh(bool resumed = false)
    {
        refreshPending?.Cancel();
        var pending = refreshPending = new CancellationTokenSource();
        try
        {
            await Task.Delay(1800, pending.Token);
            await model.RefreshAsync();
            if (resumed)
            {
                // Displays behind docks may become available several seconds after Windows resumes.
                await Task.Delay(6000, pending.Token);
                await model.RefreshAsync();
            }
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(refreshPending, pending)) refreshPending = null; pending.Dispose(); }
    }

    private void Navigation_Checked(object sender, RoutedEventArgs args)
    {
        if (MonitorsPage is null) return;
        string page = (sender as RadioButton)?.Tag as string ?? "Monitors";
        MonitorsPage.Visibility = page == "Monitors" ? Visibility.Visible : Visibility.Collapsed;
        TimersPage.Visibility = page == "Timers" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void Refresh_Click(object sender, RoutedEventArgs args) => await model.RefreshAsync();
    private void Preset_Click(object sender, RoutedEventArgs args)
    { if (int.TryParse((sender as Button)?.Tag as string, out int value)) model.SetAll(value); }
    private void AddTimer_Click(object sender, RoutedEventArgs args) => EditTimer(null);
    private void EditTimer_Click(object sender, RoutedEventArgs args) => EditTimer(((FrameworkElement)sender).DataContext as TimerViewModel);
    private void EditTimer(TimerViewModel? entry)
    {
        var dialog = new TimerDialog(model.Monitors, entry?.Rule) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } result) model.AddOrUpdateTimer(result);
    }
    private void DeleteTimer_Click(object sender, RoutedEventArgs args)
    { if (((FrameworkElement)sender).DataContext is TimerViewModel entry) model.RemoveTimer(entry); }
    private void DisplaySettings_Click(object sender, RoutedEventArgs args) => OpenShell("ms-settings:display");
    private void OpenSettings_Click(object sender, RoutedEventArgs args)
    { Directory.CreateDirectory(model.StoragePath); OpenShell(model.StoragePath); }
    private void ResetSettings_Click(object sender, RoutedEventArgs args)
    {
        if (MessageBox.Show(this, "Reset OmniLux timers and settings? A copy of the current file will be kept for recovery.",
            "Reset settings", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK) model.ResetSettings();
    }
    private static void OpenShell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Win32Exception ex) { MessageBox.Show(ex.Message, "OmniLux", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void Exit_Click(object sender, RoutedEventArgs args) => ((App)Application.Current).Quit();
    private void Minimize_Click(object sender, RoutedEventArgs args) => Hide();
    private void Maximize_Click(object sender, RoutedEventArgs args) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs args) => Close();
    private void Window_Closing(object? sender, CancelEventArgs args)
    {
        if (!exiting) { args.Cancel = true; Hide(); }
    }
    private void Window_StateChanged(object? sender, EventArgs args)
    { if (WindowState == WindowState.Minimized) Hide(); }
    public void ShowFromTray()
    {
        Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate(); Focus();
    }
    public void PrepareExit()
    {
        exiting = true; refreshPending?.Cancel(); source?.RemoveHook(WindowMessage);
        SystemEvents.PowerModeChanged -= PowerModeChanged; SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
    }
}
