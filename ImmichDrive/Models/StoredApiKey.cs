namespace ImmichDrive.Models;

/// <summary>
/// The Immich API key as kept in <see cref="Services.ProtectedStore"/>. Plain POCO, WinUI-free:
/// linked into the thumbnail extension, which reads the key to fetch thumbnails.
/// </summary>
public sealed class StoredApiKey
{
    /// <summary>Protected-store name. Permanent: changing it orphans every installed copy's key.</summary>
    public const string StoreName = "immich-api-key";

    /// <summary>
    /// The settings.json field that held the key before it moved to the protected store. Read only
    /// to migrate it, and by the thumbnail extension during the window before the app has migrated.
    /// </summary>
    public const string LegacySettingsField = "ApiKey";

    /// <summary>The key sent as <c>x-api-key</c>.</summary>
    public string ApiKey { get; set; } = "";
}
