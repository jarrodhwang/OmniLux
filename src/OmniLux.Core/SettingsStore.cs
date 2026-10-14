using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniLux.Core;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Theme { get; set; } = "System";
    public bool TimersPaused { get; set; }
    public List<BrightnessSchedule> Schedules { get; set; } = [];
}

public sealed record SettingsLoadResult(AppSettings Settings, string? Warning);

public sealed class SettingsStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string FilePath => Path.Combine(directory, "settings.json");

    public SettingsLoadResult Load()
    {
        if (!File.Exists(FilePath)) return new(new(), null);
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options)
                ?? throw new JsonException("Empty settings.");
            if (settings.SchemaVersion != 1) return new(new(), "Settings are from a newer OmniLux version. They were left intact; timers are disabled.");
            settings.Schedules ??= [];
            if (settings.Theme is not ("System" or "Light" or "Dark")) settings.Theme = "System";
            var invalid = settings.Schedules.RemoveAll(x => x is null || !SchedulePlanner.IsValid(x));
            settings.Schedules = settings.Schedules.DistinctBy(x => x.Id).ToList();
            return new(settings, invalid > 0 ? "Invalid timer entries were ignored. Check your timers before enabling them." : null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Never overwrite corrupt settings while loading; keep them for recovery.
            return new(new(), "Settings could not be read. Your saved file was kept; timers are disabled.");
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        if (File.Exists(FilePath))
            File.Replace(temporary, FilePath, FilePath + ".bak", true);
        else
            File.Move(temporary, FilePath);
    }
}
