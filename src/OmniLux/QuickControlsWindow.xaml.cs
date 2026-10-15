using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using OmniLux.Services;
using OmniLux.ViewModels;
using Forms = System.Windows.Forms;

namespace OmniLux;

public partial class QuickControlsWindow : Window
{
    private readonly MainViewModel model;
    private readonly Action openMain;
    public long LastDismissedAt { get; private set; } = -1000;

    public QuickControlsWindow(MainViewModel model, Action openMain)
    {
        InitializeComponent(); this.model = model; this.openMain = openMain; DataContext = model;
    }

    public void ShowNearTray()
    {
        var cursor = Forms.Control.MousePosition;
        var work = Forms.Screen.FromPoint(cursor).WorkingArea;
        nint hwnd = new WindowInteropHelper(this).EnsureHandle();
        // Position in native pixels first so WPF receives the target display's DPI.
        SetWindowPos(hwnd, 0, work.Right - 400, work.Top + 12, 0, 0, 0x0015);
        double scale = GetDpiForWindow(hwnd) / 96d;
        MaxHeight = Math.Max(200, (work.Height - 24) / scale);
        MonitorScroll.MaxHeight = Math.Max(90, MaxHeight - 105);
        Show(); UpdateLayout();
        GetWindowRect(hwnd, out var size);
        int width = size.Right - size.Left, height = size.Bottom - size.Top;
        int x = Math.Clamp(cursor.X - width + 24, work.Left + 8, Math.Max(work.Left + 8, work.Right - width - 8));
        int y = Math.Clamp(cursor.Y - height - 12, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - height - 8));
        // SWP_SHOWWINDOW also handles a process started with a hidden startup window.
        SetWindowPos(hwnd, new nint(-1), x, y, 0, 0, 0x0041);
        Activate(); Focus();
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        _ = model.CommitPreviewsAsync(); LastDismissedAt = Environment.TickCount64; Hide();
    }
    public bool FitsWorkArea()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(hwnd, out var rectangle)) return false;
        var work = Forms.Screen.FromHandle(hwnd).WorkingArea;
        return rectangle.Left >= work.Left && rectangle.Top >= work.Top && rectangle.Right <= work.Right && rectangle.Bottom <= work.Bottom;
    }
    private void Window_Deactivated(object? sender, EventArgs args) => Dismiss();
    private void Window_SourceInitialized(object? sender, EventArgs args) => ThemeManager.ApplyWindow(this);
    private void Window_KeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        model.CancelPreviews(); Dismiss(); args.Handled = true;
    }
    private async void Refresh_Click(object sender, RoutedEventArgs args) => await model.RefreshAsync();
    private void OpenMain_Click(object sender, RoutedEventArgs args) { Dismiss(); openMain(); }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
