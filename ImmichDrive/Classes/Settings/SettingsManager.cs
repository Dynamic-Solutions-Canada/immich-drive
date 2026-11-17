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
    /// "Reset settings" (CONF-4, D-104): keeps the current file as <c>settings.json.&lt;UTC time&gt;.reset</c>,
    /// writes a fresh one and applies the default theme and language at once.
    /// <para>
    /// What resets is the user's preferences: theme, language and the settings window's placement. The
    /// drive's connection and bookkeeping carry over (server URL, drive folder, connected flag, layout
    /// and registration versions, last sync, last known version): clearing them would leave the sync
    /// root registered at a folder the app no longer knows and force a full rebuild, and the
    /// Connection page's Disconnect is how a user drops the drive. Start with Windows is Windows' state
    /// and the API key is in the protected store, so neither is touched.
    /// </para>
    /// Callers reload the page that shows the settings, so its controls show the defaults.
    /// </summary>
    public static void ResetSettings()
    {
        var drive = _current;
        var result = Store.Reset();
        _current = result.Settings;

        // Carry the drive over before initialization ends, so no change handler runs for it.
        _current.ServerUrl = drive.ServerUrl;
        _current.Connected = drive.Connected;
        _current.SyncRootPath = drive.SyncRootPath;
        _current.LastSyncUtc = drive.LastSyncUtc;
        _current.LayoutVersion = drive.LayoutVersion;
        _current.RegisteredVersion = drive.RegisteredVersion;
        _current.LastKnownVersion = drive.LastKnownVersion;
        _current.CompleteInitialization();

        Logger.Info("Settings reset; previous file kept at {Backup}", result.BackupPath);
        WinUIAppKit.Theming.AppTheme.Set((WinUIAppKit.Theming.AppThemeChoice)_current.AppTheme);
        WinUIAppKit.Localization.UiText.SetOverride(_current.Language);

        // Writes the carried-over drive state, and the legacy key field if the key is still pending.
        SaveSettings();
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
