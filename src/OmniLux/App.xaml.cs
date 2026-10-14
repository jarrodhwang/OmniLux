using System.Windows;
using OmniLux.Core;
using OmniLux.Native;
using OmniLux.Services;
using OmniLux.ViewModels;

namespace OmniLux;

public partial class App : Application
{
    private SingleInstance? instance;
    private TrayService? tray;
    private MainViewModel? model;
    private MonitorAccess? access;
    private MainWindow? window;
    private AppLog? log;
    private bool quitting;

    protected override async void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        string? smokeDirectory = null;
        try
        {
            smokeDirectory = ValueAfter(args.Args, "--smoke");
            string? probePath = ValueAfter(args.Args, "--probe");
            string? hardwarePath = ValueAfter(args.Args, "--hardware-test");
            string? startupPath = ValueAfter(args.Args, "--startup-test");
            bool demo = args.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
            if (startupPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(startupPath))!);
                await File.WriteAllTextAsync(startupPath, System.Text.Json.JsonSerializer.Serialize(new StartupService().VerifyRoundTrip(),
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                Shutdown(); return;
            }
            if (probePath is not null || hardwarePath is not null)
            {
                using var backend = new DdcMonitorBackend();
                var monitors = await Task.Run(backend.Discover);
                object receipt = hardwarePath is not null ? await Task.Run(() => backend.VerifyHardware(monitors)) : monitors;
                string receiptPath = Path.GetFullPath(hardwarePath ?? probePath!);
                Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
                await File.WriteAllTextAsync(receiptPath, System.Text.Json.JsonSerializer.Serialize(receipt,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                Shutdown(); return;
            }
            instance = new SingleInstance(smokeDirectory is not null || demo ? Guid.NewGuid().ToString("N") : null);
            if (args.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase)) { if (!instance.IsOwner) instance.SignalExit(); Shutdown(); return; }
            if (!instance.IsOwner) { if (!args.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase)) instance.SignalShow(); Shutdown(); return; }
            string directory = smokeDirectory is not null ? Path.Combine(Path.GetFullPath(smokeDirectory), "profile-" + Guid.NewGuid().ToString("N"))
                : demo ? Path.Combine(Path.GetTempPath(), "OmniLux-demo", Guid.NewGuid().ToString("N"))
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniLux");
            log = new AppLog(directory);
            var store = new SettingsStore(directory);
            var loaded = store.Load();
            ThemeManager.Apply(loaded.Settings.Theme);
            access = new MonitorAccess(demo ? new DemoMonitorBackend() : new DdcMonitorBackend());
            model = new MainViewModel(access, store, new StartupService(), log, loaded, ThemeManager.Apply);
            window = new MainWindow(model);
            MainWindow = window;
            tray = new TrayService(model, () => Dispatcher.BeginInvoke(window.ShowFromTray), () => Dispatcher.BeginInvoke(Quit));
            instance.Listen(() => Dispatcher.BeginInvoke(window.ShowFromTray), () => Dispatcher.BeginInvoke(Quit));
            window.EnsureNativeWindow();
            if (!args.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase)) window.Show();
            DispatcherUnhandledException += (_, error) =>
            {
                log.Write("unhandled-ui-error", error.Exception.ToString());
                error.Handled = true;
                MessageBox.Show(window, "OmniLux encountered an error. Details were saved in the local activity log.\n\n" + error.Exception.Message,
                    "OmniLux", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            await model.RefreshAsync();
            if (smokeDirectory is not null) await SmokeRunner.RunAsync(this, window, model, tray, smokeDirectory, demo);
        }
        catch (Exception ex)
        {
            log?.Write("startup-failed", ex.ToString());
            if (smokeDirectory is not null)
            {
                Directory.CreateDirectory(smokeDirectory);
                File.WriteAllText(Path.Combine(smokeDirectory, "smoke.json"), System.Text.Json.JsonSerializer.Serialize(new { failures = 1, error = ex.ToString() }));
            }
            else MessageBox.Show("OmniLux could not start.\n\n" + ex.Message, "OmniLux", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string? ValueAfter(string[] args, string option)
    {
        int index = Array.FindIndex(args, x => x.Equals(option, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"{option} requires an output path.");
        return args[index + 1];
    }

    public void Quit()
    {
        if (quitting) return;
        quitting = true; model?.Stop(); window?.PrepareExit(); tray?.Dispose(); tray = null;
        // A stuck display driver must not stop the user from exiting. Handles are normally
        // disposed by the serialized worker; Windows also releases them on process exit.
        if (access is not null) _ = access.DisposeAsync();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs args)
    {
        model?.Stop(); window?.PrepareExit(); tray?.Dispose(); instance?.Dispose();
        base.OnExit(args);
    }
}
