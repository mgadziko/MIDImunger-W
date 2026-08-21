using System.IO;
using System.Text.Json;

namespace MIDImunger.W;

public sealed record WindowPlacementSettings(double Left, double Top, double Height, bool IsMaximized);

/// <summary>
/// Persists the main window's position, height, and maximized state across app launches.
/// </summary>
public static class WindowSettingsService
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MIDImunger-W",
        "window-settings.json");

    public static WindowPlacementSettings? Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<WindowPlacementSettings>(json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(WindowPlacementSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Persisting window placement is a convenience, not critical; ignore failures.
        }
    }
}
