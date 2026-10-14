using Microsoft.Win32;

namespace OmniLux.Services;

public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OmniLux";
    public string Command => $"\"{Environment.ProcessPath ?? throw new InvalidOperationException("Application path unavailable.")}\" --tray";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
        if (IsEnabled != enabled) throw new IOException("Windows did not save the startup setting.");
    }

    public object VerifyRoundTrip()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        object? original = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var kind = original is not null ? key.GetValueKind(ValueName) : RegistryValueKind.String;
        bool enabled = false, disabled = false, restored;
        try
        {
            SetEnabled(true); enabled = IsEnabled;
            SetEnabled(false); disabled = !IsEnabled && key.GetValue(ValueName) is null;
        }
        finally
        {
            if (original is not null) key.SetValue(ValueName, original, kind);
            else key.DeleteValue(ValueName, false);
            restored = Equals(key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames), original);
        }
        return new { enabled, disabled, restored, quotesExecutable = Command.StartsWith('"') && Command.EndsWith("\" --tray", StringComparison.Ordinal) };
    }
}
