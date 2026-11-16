using ImmichDrive.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinUIAppKit.Settings;

namespace ImmichDrive.Services;

/// <summary>What <see cref="ApiKeyMigration.Run"/> found and did.</summary>
internal enum ApiKeyMigrationOutcome
{
    /// <summary>No key anywhere: a fresh install, or the user never entered one.</summary>
    NoKey,

    /// <summary>The key was already in the protected store and settings.json held none.</summary>
    AlreadyMigrated,

    /// <summary>The key moved from settings.json to the protected store, and settings.json was rewritten without it.</summary>
    Migrated,

    /// <summary>
    /// The protected store could not be written or did not read back the same key. settings.json was
    /// left untouched, so the key is not lost; the caller keeps carrying it there until a later launch succeeds.
    /// </summary>
    StoreWriteFailed,

    /// <summary>
    /// The key is safely in the protected store, but settings.json could not be rewritten. The next
    /// ordinary settings save drops the field, and the next launch retries anyway.
    /// </summary>
    SettingsRewriteFailed,

    /// <summary>A protected file exists but cannot be decrypted (restored profile, other user, corruption).</summary>
    StoredKeyUnreadable,
}

/// <summary>The outcome and the key the app should use for this session ("" when none).</summary>
internal readonly record struct ApiKeyMigrationResult(ApiKeyMigrationOutcome Outcome, string ApiKey);

/// <summary>
/// One-time move of the Immich API key out of plain-text settings.json into
/// <see cref="ProtectedStore"/>. Runs on every launch and is a no-op once done. WinUI-free and
/// NLog-free so the portable test harness can link it; the app logs the outcome.
/// </summary>
/// <remarks>
/// <para>Order matters: the key is written to the store and <b>read back</b> before settings.json is
/// touched, so no failure can leave the key in neither place.</para>
/// <para>If settings.json holds a key and the store also holds one, settings.json wins. Only a
/// version older than this one writes the key there, so after a downgrade and re-upgrade it is the
/// one the user entered last.</para>
/// </remarks>
internal static class ApiKeyMigration
{
    /// <summary>
    /// Migrates if needed and returns the key to use.
    /// </summary>
    /// <param name="settingsPath">Full path of settings.json.</param>
    /// <param name="store">The protected store the key moves into (<see cref="CredentialStore.Create"/>).</param>
    public static ApiKeyMigrationResult Run(string settingsPath, ProtectedStore store)
    {
        string? legacyKey = ReadLegacyKey(settingsPath);
        if (legacyKey is null)
        {
            return ReadStoredKey(store);
        }

        // Phase 1: copy into the store and prove it landed. Save swallows its own failures,
        // so the read-back is the only reliable success signal.
        store.Save(StoredApiKey.StoreName, new StoredApiKey { ApiKey = legacyKey });
        string? readBack = store.Load<StoredApiKey>(StoredApiKey.StoreName)?.ApiKey;
        if (readBack != legacyKey)
        {
            return new(ApiKeyMigrationOutcome.StoreWriteFailed, legacyKey);
        }

        // Phase 2: only now is it safe to drop the plain-text copy.
        bool rewritten = TryRemoveLegacyKey(settingsPath);
        var outcome = rewritten ? ApiKeyMigrationOutcome.Migrated : ApiKeyMigrationOutcome.SettingsRewriteFailed;
        return new(outcome, legacyKey);
    }

    /// <summary>
    /// The non-empty legacy key from settings.json, or null when the file, the field or a value is
    /// missing, or the file cannot be parsed. Reads the raw JSON because <c>UserSettings</c> no
    /// longer has the property.
    /// </summary>
    internal static string? ReadLegacyKey(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!doc.RootElement.TryGetProperty(StoredApiKey.LegacySettingsField, out var field))
            {
                return null;
            }

            if (field.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? key = field.GetString();
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads the store when settings.json holds no key.</summary>
    private static ApiKeyMigrationResult ReadStoredKey(ProtectedStore store)
    {
        string? stored = store.Load<StoredApiKey>(StoredApiKey.StoreName)?.ApiKey;
        if (!string.IsNullOrEmpty(stored))
        {
            return new(ApiKeyMigrationOutcome.AlreadyMigrated, stored);
        }

        // A file that exists but does not decrypt is different from no file: the user needs to be
        // told to re-enter the key rather than see a silent "Not configured".
        if (store.Exists(StoredApiKey.StoreName))
        {
            return new(ApiKeyMigrationOutcome.StoredKeyUnreadable, "");
        }

        return new(ApiKeyMigrationOutcome.NoKey, "");
    }

    /// <summary>
    /// Rewrites settings.json with the key field removed and every other field kept as written,
    /// atomically (temporary file, then rename; no backup is left). Returns false on any failure,
    /// in which case the original file is unchanged.
    /// </summary>
    private static bool TryRemoveLegacyKey(string settingsPath)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject();
            if (root is null)
            {
                return false;
            }

            root.Remove(StoredApiKey.LegacySettingsField);
            AtomicFile.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
