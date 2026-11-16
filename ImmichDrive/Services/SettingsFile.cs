using ImmichDrive.Models;
using System.IO;
using System.Text.Json;

namespace ImmichDrive.Services;

/// <summary>
/// Minimal read-only view of <c>settings.json</c> for the out-of-process thumbnail extension,
/// which only needs the server URL + API key (and must not pull in WinUI / CommunityToolkit /
/// the full <c>UserSettings</c> type). The resident app uses the full <c>SettingsManager</c>.
/// WinUI-free: linked into the thumbnail extension. The protected-store read uses reflection-based
/// System.Text.Json, which is fine because neither executable is trimmed.
/// </summary>
public static class SettingsFile
{
    /// <summary>
    /// The server URL from settings.json and the API key from the protected store, or null when
    /// either is missing. Read-only: never migrates, writes or deletes anything, because the app
    /// owns those files and may be writing them at the same moment.
    /// </summary>
    public static (string ServerUrl, string ApiKey)? ReadConnection()
    {
        try
        {
            string path = SharedPaths.SettingsFilePath;
            if (!File.Exists(path))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            string url = root.TryGetProperty("ServerUrl", out var u) ? u.GetString() ?? "" : "";
            string key = ReadApiKey(root);
            if (url.Length == 0 || key.Length == 0)
            {
                return null;
            }

            return (url, key);
        }
        catch { return null; }
    }

    /// <summary>
    /// The API key, with the same precedence as <c>ApiKeyMigration</c>: a legacy settings.json key
    /// wins when present, otherwise the protected store. A legacy key exists only before the app
    /// has migrated it (the window after an update where the shell starts this extension first, or
    /// while a failed migration is pending), and in that case it is the one the user entered last.
    /// </summary>
    private static string ReadApiKey(JsonElement settingsRoot)
    {
        if (settingsRoot.TryGetProperty(StoredApiKey.LegacySettingsField, out var legacy) &&
            legacy.ValueKind == JsonValueKind.String &&
            legacy.GetString() is { Length: > 0 } legacyKey)
        {
            return legacyKey;
        }

        return ProtectedStore.Load<StoredApiKey>(StoredApiKey.StoreName)?.ApiKey ?? "";
    }

    /// <summary>The drive folder (sync root), falling back to the default if unset.</summary>
    public static string ReadSyncRoot()
    {
        try
        {
            string path = SharedPaths.SettingsFilePath;
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("SyncRootPath", out var p) &&
                    p.GetString() is { Length: > 0 } s)
                {
                    return s;
                }
            }
        }
        catch { /* fall through */ }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ImmichDrive");
    }
}
