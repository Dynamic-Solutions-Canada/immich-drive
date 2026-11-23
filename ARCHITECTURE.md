# ImmichDrive architecture

```
App.xaml ──► MainWindow (invisible host)
               ├── Tray icon (kit TrayIcon)      - status (muted when offline), context menu
               ├── DriveManager                  - orchestrates client + provider + index
               │     ├── CloudProviderService    - cfapi connection + hydration callbacks
               │     ├── SyncRootService          - register/unregister the sync root
               │     ├── PlaceholderPopulator     - build month + Albums/Favorites/Partners tree
               │     ├── DriveSecurity            - read-only deny ACE + folder icon
               │     ├── UploadService            - watch Upload\, POST drops to Immich
               │     └── AssetIndex (SQLite)      - relativePath ⇄ Immich assetId
               └── SettingsWindow (WinUI 3, kit SettingsShell, on demand)
                     ├── HomePage         - status, drive folder, last-updated
                     ├── ConnectionPage   - server URL + API key + Test/Connect/Disconnect
                     ├── SettingsPage     - kit rows: start with Windows, app theme
                     ├── Our other apps   - apps-promo page
                     └── About            - kit AboutPage, from AboutContent

ImmichDrive.ThumbnailProvider.exe (separate process: out-of-process COM server)
   └── ImmichThumbnailProvider : IThumbnailProvider, IInitializeWithItem
         ├── AssetIndex reader (read-only SQLite)   [linked from ImmichDrive]
         └── ImmichClient (thumbnail GET)           [linked from ImmichDrive]
```

## 1. The two processes

**ImmichDrive.exe** is resident (tray). It is the cloud sync provider: it must be running
for the drive to be "online" and for hydration callbacks to fire. It is also the settings
UI, shown on demand.

**ImmichDrive.ThumbnailProvider.exe** is an **out-of-process COM server** (a real exe,
`OutputType=WinExe`) the *shell* launches on demand, never ImmichDrive.exe. The in-proc
comhost DLL could not host the .NET runtime inside the shell's thumbnail surrogate
(activation failed with `0x80008093`), so the provider is its own exe: it registers a class
factory for the CLSID (`CoRegisterClassObject`), pumps a message loop while the shell uses
it, and exits after a short idle. It runs with package identity, has no UI, and stays lean
and trim/AOT-safe. It shares source with the app via MSBuild `<Compile Include="..\ImmichDrive\…" Link="…">`:
`Models/ImmichAsset.cs`, `Services/ImmichClient.cs`, the read path of `Services/AssetIndex.cs`,
and the `SharedPaths`/`SettingsFile` helpers. Those files therefore stay WinUI-free and
NLog-free.

## 2. Cloud Files API (cfapi) flow

Registration uses the WinRT `StorageProviderSyncRootManager.Register` (clean, declarative)
with a provider id, display name, icon, and the sync-root folder path
(`%UserProfile%\ImmichDrive` by default, configurable). Registration is non-destructive: if
our (stable, FNV-1a-hashed) sync-root id is already registered we leave it intact, since
re-registering tears down placeholders. Then `CfConnectSyncRoot` wires the callback table.
We register `FETCH_DATA` (hydration), `CANCEL_FETCH_DATA`, and `NOTIFY_DELETE` / `NOTIFY_RENAME`, which refuse the user's deletes, renames and moves outside `Upload\` (docs/read-only.md). See [docs/cloud-files.md](docs/cloud-files.md).

- **Placeholders** are created with `CfCreatePlaceholders`, one `CF_PLACEHOLDER_CREATE_INFO`
  per asset, with `FileIdentity` = UTF-8 Immich asset id, `FsMetadata` = size + timestamps
  (the asset's capture time is set on all four `FILE_BASIC_INFO` fields), and a clean
  `RelativeFileName`. Folders are created as plain directories.
- **Hydration**: on `FETCH_DATA` we read the file's `FileIdentity` (asset id), the requested
  byte range, and a transfer key, then offload the transfer to a task. We stream
  `/api/assets/{id}/original` and push 1 MiB chunks with
  `CfExecute(CF_OPERATION_TYPE_TRANSFER_DATA)`; on completion the range is satisfied and
  Windows marks the region in-sync. On error we report `STATUS_UNSUCCESSFUL` so the open
  fails instead of hanging.
- **State**: newly created placeholders are "in sync, dehydrated." Opening hydrates the
  needed range. We never proactively download (no local copies) except files the user opens.

## 3. Folder & file layout (newest first)

Top level under the sync root: month folders named `yyyy-MM MMMM` (e.g. `2026-06 June`),
plus `Albums`, `Favorites`, `Partners`, and a writable `Upload` folder.

`PlaceholderPopulator` walks the Immich timeline newest-month-first and, per month bucket:
- derives the month-folder name from the **bucket's own year/month**, *not* a local-time
  conversion. Parsing the bucket key as UTC and calling `ToLocalTime()` rolled the month
  boundary backwards for negative UTC offsets (June's assets landed in a "May" folder), so
  the date is used as-is.
- names each file with the **clean original Immich filename** (sanitized; an id-based stem
  as a last-resort fallback). There is no timestamp prefix. Because each placeholder carries
  the asset's real capture time, sorting a folder by Date (descending) in Explorer shows
  newest-first while the name stays clean for attaching.
- disambiguates name collisions within a folder by appending ` (2)`, ` (3)`, ….

`Albums\<name>\`, `Favorites\`, and `Partners\<name>\<month>\` mirror the corresponding
Immich views; their placeholders share the same asset id as the timeline copy, so thumbnails
and hydration work identically. Populate is **self-healing and incremental** (already-indexed
assets are skipped, missing placeholders are recreated from the stored size with no network)
and **prunes** assets/albums/partners removed from Immich, but only when the relevant fetch
succeeded, so a transient network error can never wipe the library. A `LayoutVersion` bump
triggers a one-time clean rebuild (carrying the old index's metadata forward to avoid
re-enriching the whole library).

**Auto-refresh** (`DriveManager`): the newest month bucket is re-polled every ~1 min (catch
new phone photos fast) and the whole library every ~15 min. A single lock serializes all
populate work so polls never overlap each other or the initial/manual populate.

## 4. Read-only drive + upload

The drive is **always read-only to the user** (`DriveSecurity`), via an inherited deny ACE
for the current account (`icacls /deny`) blocking create/edit/delete/rename. The cldflt
filter's placeholder + hydration operations bypass the deny (as OneDrive's read-only folders
do), and the provider lifts the deny per-item when it needs to prune. The sync-root folder
also gets a custom Explorer icon via a `desktop.ini` written at connect (the folder is
flagged `ReadOnly` so Explorer honors it).

Write Attributes remains available so Explorer can pin photos and folders. `PinHydrationService`
watches these changes, downloads pinned placeholders, and resumes pending downloads after a
restart or a temporary connection failure. The drive status reports failed downloads.

The only writable spot is the `Upload` folder (inheritance broken, full control granted).
`UploadService` watches it; a file dropped there is POSTed to Immich (`/api/assets`) and the
local copy deleted. Sync is therefore one-way (Immich → PC) for everything the user sees,
with `Upload` as the single PC → Immich path.
The upload service also updates its Explorer badge: no remaining files means in sync;
files still copying, uploading, or left after a failure keep the folder pending. It checks
disk contents rather than the upload queue so a failed attempt cannot appear complete.

## 5. Thumbnails without hydration

The shell resolves the thumbnail handler for placeholder files via the manifest's
`desktop3:CloudFiles` extension (scoped to the sync root; it does **not** override the
system thumbnailer for other files). `ImmichThumbnailProvider`:
1. `IInitializeWithItem::Initialize` gives us the `IShellItem`; we read its path via
   `SIGDN_FILESYSPATH` **without opening the stream** (so no hydration).
2. We confirm the path is inside the sync root (else return `E_FAIL` so the shell's default
   handler runs), then map the relative path → assetId through the SQLite index.
3. We GET `/api/assets/{id}/thumbnail?size=preview`, decode to an HBITMAP, and return it.

## 6. Settings & index storage

`SettingsManager` (static) serializes `UserSettings` to `%AppData%\ImmichDrive\settings.json`.
`AssetIndex` is a SQLite DB at `%AppData%\ImmichDrive\index.db`. The thumbnail extension,
running out of process inside the shell, must read the **same** physical files; when the app
is packaged (MSIX) AppData is redirected per-package, so the app records its resolved data
folder in a breadcrumb at `C:\ProgramData\ImmichDrive\location.txt`, which is never
redirected, and the extension reads it from there. The sync-root icon is copied to a fixed, version-independent
path (`C:\ProgramData\ImmichDrive`) so an app update doesn't leave a broken icon.

### Where the Immich API key lives

The API key is not a setting and is never written to `settings.json` or to a log. `ApiKeyStore`
holds it in memory and persists it through `ProtectedStore` to `protected-immich-api-key.dat` in
the same data folder, encrypted with Windows data protection (DPAPI) scoped to the current
Windows user. A copy of that file is useless on another machine or under another account; it does
not protect against software already running as the same user.

- **Thumbnail extension:** links `ProtectedStore` read-only and decrypts the same file (it runs as
  the same user). If a legacy `ApiKey` field is still in `settings.json` it uses that instead, with
  the same precedence as the migration below; that happens only in the window after an update in
  which the shell starts it before the app has migrated, or while a failed migration is pending.
- **Migration:** versions before this change kept the key in `settings.json` in plain text. On
  each launch `ApiKeyMigration` checks for that field; if present it writes the key to the
  protected store, reads it back to confirm, and only then rewrites `settings.json` without it
  (temporary file, then rename; no backup is kept). If the store cannot be written, the key stays
  in `settings.json` and the drive stays connected; the next launch retries. If `settings.json`
  holds a key and the store holds another, `settings.json` wins, since only an older version
  writes there.
- **Unreadable key:** if the protected file exists but will not decrypt (a profile restored onto
  another machine, a different Windows user), the file is left alone and the app asks the user to
  enter the key again instead of showing a bare "Not configured".
- **After uninstall:** the file sits beside `settings.json` in the data folder. For the Store
  (MSIX) install that folder is inside the package's redirected AppData, which Windows deletes
  when the package is uninstalled. An unpackaged development build uses the real
  `%AppData%\ImmichDrive`, where the file remains after the app is removed; delete that folder to
  remove it.

## 7. Theme / window / tray

The shared plumbing comes from winui-app-kit (`external/winui-app-kit`, pinned to a release tag):

- **Startup order** (`App.OnLaunched`, kit `AppBootstrap`): crash logging, then claim the single
  instance (mutex `ImmichDrive.Instance`), then load the API key, migrating it out of
  `settings.json`, then load settings through the kit's versioned `JsonSettingsStore`, then create
  the host. The key migration must come before the settings load, because the store rewrites the
  file as it loads.
- **Host:** `MainWindow` is the kit's hidden `HostWindow` ("ImmichDrive Host"), which answers a
  second launch's `ImmichDrive_ShowSettings` message and runs the exit steps (stop tray updates,
  save settings, remove the tray icon) before the process ends.
- **Tray:** the kit's `TrayIcon` (GUID `48633E3A-477E-41D2-8596-F1851C4D2045`, permanent) and native
  `TrayMenu`. The icon is the app's `.ico` artwork, normal or muted, rendered at the tray's exact size;
  tooltip updates are coalesced to about one a second during a sync.
- **Flyout:** `StatusFlyout` on the kit's `FlyoutController`.
- **Windows and theme:** the settings window uses Mica, a custom title bar and a `NavigationView`;
  its icon and saved placement come from the kit's `WindowIcon` and `WindowChrome`, and every
  window follows the kit's `AppTheme`.
- **Start at sign-in:** the kit's `StartupRegistration` over the manifest startup task.

## 8. When the server goes away

A mounted drive whose server has vanished is a recoverable state (`DriveStatus.Offline`),
distinct from a misconfiguration (`DriveStatus.Error`, e.g. a rejected API key, which the user must fix).
`ImmichClient.ProbeAsync` tells the two apart: it pings `/users/me` and returns
`Ok` / `Unauthorized` / `Unreachable`.

**How we notice.** Nothing polls just to check. Offline is inferred from work that already
happens: a populate that fails with a network-shaped exception, or a failed hydration
(`CloudProviderService.TransferFailed`, usually the first thing the user experiences). Either
one triggers a single confirming probe; one failed request never mutes the tray on its own.

**How we recover.** While offline the timeline timers stand down and a watchdog re-probes on a
backing-off schedule (30s → 60s → 120s → 300s). A successful probe restores `Online` and kicks a
populate; if the drive never came up at all (server down at launch, VPN not connected yet) the
watchdog runs the full connect instead. `DriveManager.RetryConnectionAsync` is the same check on
demand, wired to "Try again" in the flyout, the home page, and the tray menu.

**How it's surfaced: passively.** The tray icon swaps to `Resources\ImmichDrive-Offline.ico`
(greyscale mark + amber pip; a committed file with no generator since the app icon moved to devkit, see [docs/packaging.md](docs/packaging.md)) and the tooltip says why. The flyout shows a coloured
status dot, a plain-language line, and how stale the listing is. **No balloon or toast, no
sound, no dialog**: a server that is down for a day must not nag once an hour.

## 9. Where the app writes, and what uninstall leaves

| Location | What | After uninstall (Store install) |
| --- | --- | --- |
| Data folder: `%AppData%\ImmichDrive`, redirected into the package's private AppData when installed from the Store | `settings.json` (plus `settings.json.v<n>.bak` after an upgrade, `.bad` if it was unreadable, or `.<UTC time>.reset` after Reset settings), `index.db`, `protected-immich-api-key.dat`, the folder-retire log, the app logs (one file per launch, current plus three older), and `thumbnail-provider.log` (the thumbnail provider's failures, capped at 256 KB plus one `.1`, no paths) | Removed: Windows deletes a package's redirected AppData on uninstall (confirmed on Windows 10 and 11 test VMs, 2026-11-17). |
| Drive folder: `%UserProfile%\ImmichDrive` by default, or the folder the user chose | Placeholders, any files the user opened (hydrated) or pinned, the `Upload` folder, and a hidden `desktop.ini` for the folder icon | Windows removes the online-only placeholders when it unregisters the sync root; what remains is the folder tree, `desktop.ini`, any file the user opened (hydrated) or pinned, and anything still in `Upload`, all deletable normally. This holds because the read-only ACE no longer denies delete (docs/read-only.md); measured on a test VM (2026-11-17). A drive last run by an older version (before the ACE was narrowed) keeps the delete-denying ACE until the new version connects it once; uninstalled before that, it is left with placeholders the user can't delete or open, recoverable without admin rights with `icacls <folder> /reset /t`, `attrib -r` recursively, then delete (README, "Uninstall"). |
| Sync root registration (Windows `StorageProviderSyncRootManager`) | The Explorer navigation pane entry for the drive | Removed: Windows unregisters the sync root on uninstall (confirmed on Windows 10 and 11 test VMs, 2026-11-17, with the drive connected). |
| `C:\ProgramData\ImmichDrive` (not redirected) | `location.txt` (the breadcrumb), `ImmichDrive.ico` (stable folder icon). Earlier versions also wrote `thumb.log` here; the app deletes it | Remains. Holds no credentials or photos. |
| Startup | The MSIX startup task `ImmichDriveAutoStart`; unpackaged dev builds use the per-user `Run` registry value `ImmichDrive` (with `--silent`) instead | The startup task goes with the package. A dev build's `Run` entry remains until the app turns it off. |

Nothing else is written: no other registry keys, no server-side data beyond what the user uploads through `Upload`, and no telemetry. A dev build that is not packaged writes its data folder to the real `%AppData%\ImmichDrive`, which remains after it is removed.

## Status

Implemented and shipping, validated on-device. The cfapi connect/hydration loop, the
sync-root registration + custom nav-pane icon, the out-of-process COM thumbnail handler
scoped to the sync root, and the read-only/upload behavior all run against a live Immich
server. `ImmichClient` still handles both timeline response shapes (columnar SoA vs array)
defensively across Immich versions.
