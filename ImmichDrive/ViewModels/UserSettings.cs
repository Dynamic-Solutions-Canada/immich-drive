using CommunityToolkit.Mvvm.ComponentModel;
using ImmichDrive.Services;
using System.IO;
using System.Text.Json.Serialization;
using WinUIAppKit.Theming;
using WinUIAppKit.Windowing;

namespace ImmichDrive.ViewModels;

/// <summary>
/// All user settings. Every <c>[ObservableProperty]</c> auto-serializes to settings.json via
/// <see cref="ImmichDrive.Classes.Settings.SettingsManager"/>. Side-effects live in partial
/// <c>On…Changed</c> methods guarded by <see cref="_initializing"/>. See
/// <c>.agents/guides/user-settings.md</c>.
/// </summary>
public partial class UserSettings : ObservableObject
{
    /// <summary>Suppresses side-effects while deserializing.</summary>
    [JsonIgnore] private bool _initializing = true;

    // ── Connection ───────────────────────────────────────────────────
    /// <summary>Immich base URL (without the trailing /api).</summary>
    [ObservableProperty] public partial string ServerUrl { get; set; } = "";

    // The API key is deliberately not a setting: it lives encrypted in the protected store via
    // Services.ApiKeyStore, so it never reaches settings.json (SEC-1, CONF-5). The one exception is
    // PendingLegacyApiKey below.

    /// <summary>
    /// Null in every normal state, and then not written. Set only while the protected store refuses the
    /// key during the one-time migration out of settings.json (<see cref="ApiKeyStore.PendingLegacyKey"/>),
    /// so the user's only persisted copy survives the settings store rewriting the file. It keeps the
    /// pre-migration field name so the migration and the thumbnail provider read it unchanged.
    /// </summary>
    [JsonPropertyName(Models.StoredApiKey.LegacySettingsField)]
    public string? PendingLegacyApiKey { get; set; }

    /// <summary>Whether the drive should be connected/registered.</summary>
    [ObservableProperty] public partial bool Connected { get; set; }

    // ── Drive ────────────────────────────────────────────────────────
    /// <summary>Folder presented as the drive. Empty = default %UserProfile%\ImmichDrive.</summary>
    [ObservableProperty] public partial string SyncRootPath { get; set; } = "";

    /// <summary>Last successful timeline populate (UTC).</summary>
    [ObservableProperty] public partial DateTimeOffset LastSyncUtc { get; set; }

    /// <summary>On-disk layout/naming revision; a bump triggers a one-time clean rebuild.</summary>
    [ObservableProperty] public partial int LayoutVersion { get; set; }

    /// <summary>App version the sync root was last registered with; a change re-registers to refresh the icon.</summary>
    [ObservableProperty] public partial string RegisteredVersion { get; set; } = "";

    // ── App ──────────────────────────────────────────────────────────
    /// <summary>0 = system, 1 = light, 2 = dark: the values of WinUIAppKit's AppThemeChoice.</summary>
    [ObservableProperty] public partial int AppTheme { get; set; }

    // Start at sign-in is not stored here: Windows owns it (the startup task, or the Run key when
    // unpackaged), and the Settings page reads the real state through the kit's StartupRegistration.

    [ObservableProperty] public partial string LastKnownVersion { get; set; } = "";

    // ── Settings window geometry ─────────────────────────────────────
    /// <summary>The settings window's last normal size and position, or null to centre it.</summary>
    [ObservableProperty] public partial WindowPlacement? SettingsWindowPlacement { get; set; }

    /// <summary>The resolved sync-root folder (default if unset).</summary>
    [JsonIgnore]
    public string EffectiveSyncRootPath =>
        string.IsNullOrWhiteSpace(SyncRootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ImmichDrive")
            : SyncRootPath;

    // ── Side-effects ─────────────────────────────────────────────────
    partial void OnAppThemeChanged(int value)
    {
        if (_initializing)
        {
            return;
        }

        // Every open window follows the change (kit); the caller saves the settings.
        WinUIAppKit.Theming.AppTheme.Set((AppThemeChoice)value);
    }

    /// <summary>Repairs nulls from older/partial files and ends the initializing window.</summary>
    public void CompleteInitialization()
    {
        ServerUrl ??= "";
        SyncRootPath ??= "";
        _initializing = false;
    }
}
