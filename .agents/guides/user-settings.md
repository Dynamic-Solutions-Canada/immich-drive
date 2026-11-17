> **Read when:** adding, renaming or removing a setting, or changing how settings are stored.
> **Governs:** `ImmichDrive/ViewModels/UserSettings.cs`, `ImmichDrive/Classes/Settings/**`, `ImmichDrive/Services/SettingsFile.cs`

# Settings conventions

`UserSettings : ObservableObject` (`ViewModels/UserSettings.cs`) holds all settings as
`[ObservableProperty]` partial properties. `SettingsManager` loads and saves it through winui-app-kit's
versioned `JsonSettingsStore` at `%AppData%\ImmichDrive\settings.json`: PascalCase (the thumbnail
provider reads `ServerUrl` and `SyncRootPath` by name), defaults written, nulls omitted, a
`SchemaVersion` first, and an atomic replace.

## Schema versions

- `Classes/Settings/SettingsMigrations.cs` holds one step per version; a file without a version is
  version 0, so the current version is the number of steps. Steps are never removed or reordered.
- A renamed, removed or reshaped field needs a new step, and a harness case in
  `tests/ImmichDrive.ApiKeyMigration.Tests` when it touches the key or the window placement.
- On an upgrade the store keeps `settings.json.v<old>.bak`; an unreadable file is kept as
  `settings.json.bad` and defaults are written. `App.SettingsLoad` says which happened.
- The API key migration must run before the store loads (see `App.RestoreSettings`), because the
  store rewrites the file as it loads.

## Conventions

- One `[ObservableProperty]` per setting, PascalCase, with a sensible default.
- Side-effects go in a partial `On<Name>Changed(value)`, guarded by `if (_initializing) return;`
  so loading the file doesn't fire them. `CompleteInitialization()` repairs nulls and flips
  `_initializing` off at the end of restore.
- `[JsonIgnore]` for runtime-only / computed properties.
- Bind UI `Mode=TwoWay` to `SettingsManager.Current.<Name>`; call `SettingsManager.SaveSettings()`
  after meaningful edits.

## Current settings

| Property | Meaning |
|---|---|
| `ServerUrl` | Immich base URL (no trailing `/api`). |
| `Connected` | Whether the sync root is currently registered/connected. |
| `SyncRootPath` | Local folder presented as the drive. Empty = default `%UserProfile%\ImmichDrive`. |
| `EffectiveSyncRootPath` | `[JsonIgnore]` computed: `SyncRootPath` or the default if unset. |
| `LastSyncUtc` | Timestamp of the last successful timeline populate. |
| `LayoutVersion` | On-disk layout/naming revision; a bump triggers a one-time clean rebuild. |
| `RegisteredVersion` | App version the sync root was last registered with; a change re-registers to refresh the icon. |
| `AppTheme` | 0 = system, 1 = light, 2 = dark (the kit's `AppThemeChoice`); its side-effect applies it to every window. |
| `Language` | The interface language picked in Settings (an identity such as `fr-CA`), or null to follow Windows (LOC-4); the kit's language row applies it. |
| `LastKnownVersion` | For update/"what's new" detection. |
| `SettingsWindowPlacement` | The settings window's last normal size and position (kit `WindowPlacement`), or null to centre it. |
| `PendingLegacyApiKey` | Written as `ApiKey`, and only while the protected store refuses the key during the one-time migration; null and omitted otherwise. Never set it anywhere else. |

The drive is always read-only; there is no `ReadOnlyDrive` setting. There is no `Recent\` folder
and no `RecentDays` setting.

## Startup

Start at sign-in is not a setting: Windows owns it. The kit's `StartupRegistration`
(`MainWindow.Startup`, task id `ImmichDriveAutoStart`, Run value `ImmichDrive` when unpackaged) enables
it once on first run, never overrides a user who turned it off in Windows Settings or a policy, and
the Settings page shows the real state, disabled with the reason when it cannot change it.

## Credentials

Never add a credential to `UserSettings`: settings.json is plain text that users copy and attach
to support requests. The Immich API key lives in `ApiKeyStore` / `ProtectedStore` (DPAPI, current
user); see [ARCHITECTURE.md](../../ARCHITECTURE.md), "Where the Immich API key lives".
