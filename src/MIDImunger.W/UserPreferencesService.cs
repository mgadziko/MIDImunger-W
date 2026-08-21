using System.IO;
using System.Text.Json;

namespace MIDImunger.W;

public sealed record UserPreferences(bool IgnoreActiveSensing, string[] EnabledInputNames, string[] EnabledOutputNames);

/// <summary>
/// Persists user-selected MIDI Thru/input checkboxes and other toggles across app launches.
/// </summary>
public static class UserPreferencesService
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MIDImunger-W",
        "preferences.json");

    public static UserPreferences? Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<UserPreferences>(json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(UserPreferences preferences)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(preferences));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Persisting preferences is a convenience, not critical; ignore failures.
        }
    }
}
