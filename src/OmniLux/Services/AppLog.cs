using System.Text.Json;

namespace OmniLux.Services;

public sealed class AppLog(string directory)
{
    private readonly object sync = new();
    public string DirectoryPath => directory;

    public void Write(string action, object? details = null)
    {
        try
        {
            lock (sync)
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "activity.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length > 1_048_576)
                    File.Move(path, path + ".previous", true);
                File.AppendAllText(path, JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, action, details }) + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Logging never prevents monitor control. */ }
    }
}
