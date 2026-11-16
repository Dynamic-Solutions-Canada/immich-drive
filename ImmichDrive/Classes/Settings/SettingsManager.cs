using ImmichDrive.Services;
using ImmichDrive.ViewModels;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImmichDrive.Classes.Settings;

/// <summary>
/// Manages app settings, serialized as JSON to <c>%AppData%\ImmichDrive\settings.json</c>.
/// The out-of-process thumbnail extension reads the same file via <see cref="SettingsFile"/>.
/// </summary>
public static class SettingsManager
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private static string SettingsFilePath => SharedPaths.SettingsFilePath;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy = null, // PascalCase to match property names (and SettingsFile reader)
    };

    private static UserSettings _current = new();

    public static UserSettings Current
    {
        get => _current ??= new UserSettings();
        set => _current = value;
    }

    public static UserSettings RestoreSettings(string? filePath = null)
    {
        filePath ??= SettingsFilePath;
        try
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var deserialized = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                if (deserialized != null)
                {
                    _current = deserialized;
                    _current.CompleteInitialization();
                    Logger.Info("Settings restored");
                    return _current;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error restoring settings");
        }

        Logger.Warn("Settings not found or unreadable — loading defaults");
        _current = new UserSettings();
        _current.CompleteInitialization();
        return _current;
    }

    /// <summary>
    /// Writes the current settings atomically (temporary file, then rename), so the thumbnail
    /// extension never reads a half-written file. The API key is not part of <see cref="UserSettings"/>
    /// and is only carried here while <see cref="ApiKeyStore.PendingLegacyKey"/> says the protected
    /// store refused it, so this save cannot erase the user's only copy.
    /// </summary>
    public static void SaveSettings(string? filePath = null)
    {
        filePath ??= SettingsFilePath;
        try
        {
            string json = JsonSerializer.Serialize(_current, JsonOptions);

            if (ApiKeyStore.PendingLegacyKey is { } pendingKey)
            {
                json = ApiKeyMigration.CarryLegacyKey(json, pendingKey);
            }

            AtomicFile.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error saving settings");
        }
    }
}
