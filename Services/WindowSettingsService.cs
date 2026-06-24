using System.IO;
using System.Text.Json;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public static class WindowSettingsService
{
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ProcessMonitor", "settings.json");

    public static WindowSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path)) return new WindowSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<WindowSettings>(json) ?? new WindowSettings();
        }
        catch { return new WindowSettings(); }
    }

    public static void Save(WindowSettings settings)
    {
        try
        {
            var path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
