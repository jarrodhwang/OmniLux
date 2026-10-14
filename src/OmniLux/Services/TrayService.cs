using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;
using OmniLux.ViewModels;

namespace OmniLux.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Icon trayIcon;
    private readonly MainViewModel model;
    private readonly Action show, quit;
    public bool IsVisible => icon.Visible;

    public TrayService(MainViewModel model, Action show, Action quit)
    {
        this.model = model; this.show = show; this.quit = quit;
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/OmniLux.ico")).Stream;
        trayIcon = new Icon(stream, 32, 32);
        icon = new Forms.NotifyIcon { Icon = trayIcon, Text = "OmniLux · Monitor brightness", ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) show(); };
        icon.MouseDoubleClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) show(); };
        menu.Opening += (_, _) => BuildMenu();
        model.Changed += UpdateTooltip;
        BuildMenu();
    }

    private void BuildMenu()
    {
        foreach (Forms.ToolStripItem item in menu.Items.Cast<Forms.ToolStripItem>().ToArray()) item.Dispose();
        menu.Items.Clear();
        menu.Items.Add("Open OmniLux", null, (_, _) => show());
        menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (var monitor in model.Monitors)
        {
            var row = new Forms.ToolStripMenuItem(monitor.Name + (monitor.Actual.HasValue ? $" · {monitor.Actual}%" : " · unavailable")) { Enabled = monitor.CanControl };
            if (monitor.CanControl)
            {
                foreach (int brightness in new[] { 20, 40, 60, 80, 100 })
                    row.DropDownItems.Add($"{brightness}%", null, (_, _) => monitor.Brightness = brightness);
                row.DropDownItems.Add(new Forms.ToolStripSeparator());
                row.DropDownItems.Add("Dim by 5%", null, (_, _) => monitor.Brightness = Math.Max(0, monitor.Brightness - 5));
                row.DropDownItems.Add("Brighten by 5%", null, (_, _) => monitor.Brightness = Math.Min(100, monitor.Brightness + 5));
            }
            menu.Items.Add(row);
        }
        var all = new Forms.ToolStripMenuItem("All adjustable displays") { Enabled = model.HasAdjustable };
        foreach (int brightness in new[] { 20, 50, 80, 100 })
            all.DropDownItems.Add($"{brightness}%", null, (_, _) => model.SetAll(brightness));
        menu.Items.Add(all);
        menu.Items.Add("Refresh monitors", null, async (_, _) => await model.RefreshAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        var pause = new Forms.ToolStripMenuItem("Pause brightness timers") { Checked = model.TimersPaused };
        pause.Click += (_, _) => model.TimersPaused = !model.TimersPaused;
        menu.Items.Add(pause);
        var startup = new Forms.ToolStripMenuItem("Start with Windows") { Checked = model.StartupEnabled };
        startup.Click += (_, _) => model.StartupEnabled = !model.StartupEnabled;
        menu.Items.Add(startup);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit OmniLux", null, (_, _) => quit());
    }

    private void UpdateTooltip()
    {
        var adjustable = model.Monitors.Where(x => x.CanControl).Select(x => $"{x.Name} {x.Actual}%");
        string text = "OmniLux · " + string.Join(" · ", adjustable);
        if (model.TimersPaused) text += " · Timers paused";
        icon.Text = text.Length > 63 ? text[..60] + "…" : text;
    }

    public void Dispose()
    {
        model.Changed -= UpdateTooltip;
        icon.Visible = false; icon.Dispose(); menu.Dispose(); trayIcon.Dispose();
    }
}
