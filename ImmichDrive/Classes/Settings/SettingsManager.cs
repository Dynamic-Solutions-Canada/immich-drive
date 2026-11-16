using ImmichDrive.Services;
using ImmichDrive.ViewModels;
using System.Text.Json.Nodes;
using WinUIAppKit.Settings;

namespace ImmichDrive.Classes.Settings;

/// <summary>
/// Loads and saves <see cref="UserSettings"/> through the kit's versioned
/// <see cref="JsonSettingsStore{T}"/> at <c>%AppData%\ImmichDrive\settings.json</c> (CONF-1 to CONF-4).
/// The out-of-process thumbnail extension reads the same file via <see cref="SettingsFile"/>, so
/// property names stay PascalCase and the file is replaced atomically.
/// </summary>
public static class SettingsManager
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// One step per schema version, never removed or reordered once shipped. A file written before
    /// versions existed is version 0.
    /// </summary>
    private static readonly IReadOnlyList<Action<JsonObject>> Migrations = SettingsMigrations.Steps;

    private static JsonSettingsStore<UserSettings>? _store;

    private static UserSettings _current = new();

    /// <summary>The settings in use. Valid after <see cref="RestoreSettings"/>.</summary>
    public static UserSettings Current
    {
        get => _current ??= new UserSettings();
        set => _current = value;
    }

    private static JsonSettingsStore<UserSettings> Store =>
        _store ??= new JsonSettingsStore<UserSettings>(SharedPaths.SettingsFilePath, Migrations);

    /// <summary>
    /// Loads settings (never throws) and says what happened, so the caller can open settings on first
    /// run or explain a reset. Must run after <see cref="ApiKeyStore.Initialize"/>: the store rewrites
    /// the file as it loads, and the API key migration has to read the old file first.
    /// </summary>
    public static SettingsLoadResult<UserSettings> RestoreSettings()
    {
        var result = Store.Load();
        _current = result.Settings;
        _current.CompleteInitialization();

        switch (result.Outcome)
        {
            case SettingsLoadOutcome.Migrated:
                Logger.Info("Settings upgraded from schema version {From}; previous file kept at {Backup}", result.FileSchemaVersion, result.BackupPath);
                break;
            case SettingsLoadOutcome.Unreadable:
                Logger.Warn("Settings could not be read and were reset; the old file is kept at {Backup}", result.BackupPath);
                break;
            case SettingsLoadOutcome.NewerVersion:
                Logger.Warn("Settings were written by a newer version; loaded what this version understands");
                break;
            default:
                Logger.Info("Settings restored ({Outcome})", result.Outcome);
                break;
        }

        // The load kept whatever "ApiKey" field the file had. Make it match the key store: carried only
        // while the protected store refuses the key, dropped as soon as the key is safely stored.
        if (_current.PendingLegacyApiKey != ApiKeyStore.PendingLegacyKey)
        {
            SaveSettings();
        }

        return result;
    }

    /// <summary>
    /// Writes the current settings atomically. The API key is not a setting; its legacy field is
    /// written only while <see cref="ApiKeyStore.PendingLegacyKey"/> is set, so a save can never erase
    /// the user's only persisted copy. Failures are logged, never thrown.
    /// </summary>
    public static void SaveSettings()
    {
        _current.PendingLegacyApiKey = ApiKeyStore.PendingLegacyKey;

        try
        {
            Store.Save(_current);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error saving settings");
        }
    }
}
