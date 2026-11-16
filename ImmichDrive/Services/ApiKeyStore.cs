using ImmichDrive.Models;

namespace ImmichDrive.Services;

/// <summary>
/// The resident app's single source for the Immich API key. The key lives encrypted in
/// <see cref="ProtectedStore"/> and in memory here; it is never part of <c>UserSettings</c>, so it
/// is never written to settings.json (except as <see cref="PendingLegacyKey"/>, below) or logged.
/// Call <see cref="Initialize"/> once at startup, after settings are restored and before anything
/// saves them. WinUI-free and NLog-free so the portable test harness can link it; the app wires
/// <see cref="LogInfo"/> and <see cref="LogWarning"/> to NLog.
/// </summary>
internal static class ApiKeyStore
{
    /// <summary>Receives routine state messages. Messages never contain the key (LOG-6).</summary>
    internal static Action<string>? LogInfo { get; set; }

    /// <summary>Receives problem messages. Messages never contain the key (LOG-6).</summary>
    internal static Action<string>? LogWarning { get; set; }

    /// <summary>The key to send as <c>x-api-key</c>, or "" when none is configured.</summary>
    public static string ApiKey { get; private set; } = "";

    /// <summary>
    /// The key settings.json must keep carrying because the protected store refused it at startup.
    /// Null in every normal state. While set, it follows the current key, so a key the user enters
    /// while the store is still failing is not lost either. See
    /// <see cref="ApiKeyMigrationOutcome.StoreWriteFailed"/>.
    /// </summary>
    public static string? PendingLegacyKey { get; private set; }

    /// <summary>
    /// True when a saved key exists but cannot be decrypted, so the drive can say "enter your key
    /// again" instead of a bare "Not configured". Cleared once a new key is saved.
    /// </summary>
    public static bool StoredKeyUnreadable { get; private set; }

    /// <summary>Loads the key, migrating it out of settings.json on the first launch of this version.</summary>
    /// <param name="settingsPath">Full path of settings.json.</param>
    public static void Initialize(string settingsPath)
    {
        var result = ApiKeyMigration.Run(settingsPath);
        ApiKey = result.ApiKey;
        PendingLegacyKey = result.Outcome == ApiKeyMigrationOutcome.StoreWriteFailed ? result.ApiKey : null;
        StoredKeyUnreadable = result.Outcome == ApiKeyMigrationOutcome.StoredKeyUnreadable;

        LogOutcome(result.Outcome);
    }

    /// <summary>
    /// Saves a key the user entered (or clears it when empty). Returns false when the protected store
    /// would not accept it, and the caller should tell the user.
    /// </summary>
    /// <remarks>
    /// On failure the key still works for this session. Normally it then lasts only until the app
    /// closes, because it is never written anywhere less safe. The one exception is a startup
    /// migration that already failed: settings.json still holds the user's only persisted key, so
    /// it keeps carrying the current one rather than lose it.
    /// </remarks>
    public static bool TrySet(string key)
    {
        // Nothing changed and nothing is pending: skip a needless re-encrypt on every page save.
        if (key == ApiKey && PendingLegacyKey is null && !StoredKeyUnreadable)
        {
            return true;
        }

        ApiKey = key;

        if (key.Length == 0)
        {
            ProtectedStore.Clear(StoredApiKey.StoreName);
            PendingLegacyKey = null;
            StoredKeyUnreadable = false;
            return true;
        }

        ProtectedStore.Save(StoredApiKey.StoreName, new StoredApiKey { ApiKey = key });
        bool saved = ProtectedStore.Load<StoredApiKey>(StoredApiKey.StoreName)?.ApiKey == key;

        // Phase: settle the pending state only on a confirmed save, never before attempting it.
        if (saved)
        {
            PendingLegacyKey = null;
            StoredKeyUnreadable = false;
        }
        else if (PendingLegacyKey is not null)
        {
            PendingLegacyKey = key;
            LogWarning?.Invoke("The API key still cannot be saved to the protected store; settings.json keeps carrying it");
        }
        else
        {
            LogWarning?.Invoke("The API key could not be saved to the protected store; it will last only until the app closes");
        }

        return saved;
    }

    /// <summary>Records what startup found. Never logs the key itself (LOG-6).</summary>
    private static void LogOutcome(ApiKeyMigrationOutcome outcome)
    {
        switch (outcome)
        {
            case ApiKeyMigrationOutcome.Migrated:
                LogInfo?.Invoke("API key moved from settings.json to the protected store");
                break;
            case ApiKeyMigrationOutcome.StoreWriteFailed:
                LogWarning?.Invoke("API key could not be moved to the protected store; settings.json keeps it until a later launch succeeds");
                break;
            case ApiKeyMigrationOutcome.SettingsRewriteFailed:
                LogWarning?.Invoke("API key moved to the protected store, but settings.json could not be rewritten yet; the next save removes it");
                break;
            case ApiKeyMigrationOutcome.StoredKeyUnreadable:
                LogWarning?.Invoke("A saved API key exists but cannot be decrypted; the user must enter it again");
                break;
            default:
                LogInfo?.Invoke($"API key state at startup: {outcome}");
                break;
        }
    }
}
