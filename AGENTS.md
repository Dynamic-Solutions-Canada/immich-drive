# ImmichDrive: Agent Instructions

ImmichDrive is a paid Windows 10 22H2 and Windows 11 app that shows a user's [Immich](https://immich.app) photo and video library in File Explorer as a cloud drive, without storing the files on the device. It uses the Windows Cloud Files API to create placeholder files that show real thumbnails and download only when opened. It ships only through the Microsoft Store as one MSIX built from WSL through the shared Windows build bridge. The "Our other apps" page comes from the apps-promo submodule at `external/promo`.

## Standards

This repo follows the shared standards at https://github.com/Dynamic-Solutions-Canada/app-standards. Read the index for this stack, https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/stacks/windows.md, then the topic file for the area you are changing. If `external/standards/` exists, it is the same content pinned for offline use. Where this file differs from the standards, this file wins and says why.

## Architecture

- **`ImmichDrive/`** is the resident app: the WinUI 3 settings window, the tray icon on an invisible host window, and the Cloud Files sync provider. It registers the sync root, lays down placeholders from the Immich timeline, and serves hydration callbacks for as long as it runs.
- **`ImmichDrive.ThumbnailProvider/`** is an out-of-process COM thumbnail server (an exe, because the in-process DLL failed to activate in the shell surrogate). The shell launches it to draw thumbnails for placeholders without hydrating them.
- **Shared files are linked, not copied:** the thumbnail provider compiles the asset model, `ImmichClient`, `AssetIndex`, `SharedPaths`, `SettingsFile`, `ProtectedStore`, `AtomicFile` and `StoredApiKey` from `ImmichDrive/` (see its `.csproj`).
- **`ImmichDriveMSIX/`** packages both, and the manifest is the only place the sync root integration and the COM server are registered.
- Full design, data flow and storage locations: [ARCHITECTURE.md](ARCHITECTURE.md).

## Behaviour

- The drive is read-only except `Upload\`; see [docs/read-only.md](docs/read-only.md).
- Status is passive: tray icon, tooltip and flyout only. No toast, balloon or sound, including when the server is unreachable; the app retries quietly on a backoff.
- The Immich API key is never written to `settings.json` or a log. It lives in `ApiKeyStore` and `ProtectedStore`, as described in ARCHITECTURE.md.
- No build output is ever published: no MSIX on a GitHub release, no Actions artifact, no portable build. CI still builds the MSIX on every run as the packaging check; keep it. See [docs/packaging.md](docs/packaging.md).
- Permanent identifiers never change, because installed copies depend on them: the Partner Center package identity, the startup task id `ImmichDriveAutoStart`, the mutex `ImmichDrive.Instance`, the data folder name `ImmichDrive`, the sync root id format and provider id, the thumbnail provider CLSID, and the protected key file name `protected-immich-api-key.dat`.

## Conventions and gotchas

- Files linked into the thumbnail provider stay WinUI-free and NLog-free; failures there are reported through hooks the app wires to NLog. Check its `.csproj` before adding a dependency to a shared file.
- The sync root must be registered before callbacks connect, and the process must stay alive while the drive is online; that is why the app is resident.
- Packaged AppData is redirected per package. Both processes find the data folder through the breadcrumb `SharedPaths` writes to `C:\ProgramData\ImmichDrive`.
- cfapi structs must match `cfapi.h` exactly; a wrong offset silently corrupts hydration. See [.agents/guides/pinvoke.md](.agents/guides/pinvoke.md).
- PowerShell build scripts are ASCII only: Windows PowerShell 5.1 reads a BOM-less `.ps1` as ANSI.
- `global::` does not parse inside an interpolated string; assign to a local first.

## Shared helpers (do not duplicate)

- For replacing a file atomically, use `AtomicFile` (`ImmichDrive/Services/AtomicFile.cs`).
- For any credential, use `ProtectedStore`; for the API key specifically, `ApiKeyStore`.
- For the data folder, settings path or index path, use `SharedPaths`.
- For the GitHub owner and repository, use `SourceRepository`.

## Commands

| Command | Proves | Does not prove |
| --- | --- | --- |
| `npm run verify:desktop` | The main app compiles on Windows, XAML included | The thumbnail provider compiles; that the app runs |
| `npm run package:desktop` | Both projects compile and an unsigned Store-identity MSIX packages | Signing; that the app runs |
| `npm run package:desktop:dev` | A signed dev package builds | That it installs or runs |
| `npm run install:desktop` | The dev package updates in place and launches | Behaviour; check it in the running app |
| `npm test` | Every portable harness under `tests/` and every Store workflow script test pass, on Linux (host .NET SDK or Docker; run as a non-root user) | Windows data protection, the shell, the installed app; each harness README says what it leaves out |
| `npm run validate` | The whole Linux gate passes (today, `npm test`) | A Windows compile; also run `verify:desktop` |

Before finishing a change, run `npm run validate` and `npm run verify:desktop`, plus `npm run package:desktop:dev` when the thumbnail provider or a file linked into it changed. Use the `desktop` skill for build and install requests.

## Release

Releases are tag-driven: a `v*` tag makes `build-msix.yml` create a notes-only GitHub release and `store-publish.yml` submit to the Microsoft Store. Use the `release` skill.

## Guidance map

| Guide, doc, or skill | Read or use when | Governs |
| --- | --- | --- |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Changing how the processes, sync provider, thumbnails or storage fit together | Whole app |
| [docs/development.md](docs/development.md) | Building, versioning, compatibility checks | Build and release setup |
| [docs/cloud-files.md](docs/cloud-files.md) | Touching sync-root registration, placeholders, populate or hydration | `ImmichDrive/Classes/CloudFilter/**`, sync and populate services |
| [docs/immich-api.md](docs/immich-api.md) | Calling or changing an Immich endpoint | `ImmichDrive/Services/ImmichClient.cs` |
| [docs/thumbnails.md](docs/thumbnails.md) | Changing the thumbnail provider or a file it links | `ImmichDrive.ThumbnailProvider/**` |
| [docs/read-only.md](docs/read-only.md) | Changing folder permissions or the Upload flow | `DriveSecurity.cs`, `UploadService.cs` |
| [docs/packaging.md](docs/packaging.md) | Changing the manifest, packaging script or release workflows | `ImmichDriveMSIX/**`, `.github/workflows/**` |
| [.agents/guides/user-settings.md](.agents/guides/user-settings.md) | Adding or changing a setting | `UserSettings.cs`, `Classes/Settings/**` |
| [.agents/guides/ui-conventions.md](.agents/guides/ui-conventions.md) | Any window, page, flyout or card | `ImmichDrive/**/*.xaml(.cs)` |
| [.agents/guides/pinvoke.md](.agents/guides/pinvoke.md) | Any native declaration, cfapi struct, or tray and icon code | `NativeMethods.cs`, `CfApi.cs` |
| `add-setting` skill | Adding a persisted setting | `UserSettings.cs` and its page |
| `add-settings-page` skill | Adding a settings window page | `Pages/**`, `SettingsWindow.xaml(.cs)` |
| `desktop` skill | Building, verifying or installing locally | Local Windows builds |
| `commit` skill | Committing changes | Commits |
| `release` skill | Cutting a release | `Directory.Build.props`, `release-notes/**`, tags |
| `audit-docs` skill | Before a release or after a significant change | Every file in this table |

## Keeping docs current

| What changed | Update |
| --- | --- |
| A setting added, renamed or removed | `.agents/guides/user-settings.md` |
| An Immich endpoint, parameter or permission | `docs/immich-api.md`, and the permission list on the Connection page and in `README.md` |
| Placeholder layout, naming or populate behaviour | `docs/cloud-files.md`, `ARCHITECTURE.md` |
| A file linked into the thumbnail provider | `docs/thumbnails.md`, the Architecture section above |
| Where the app writes data, or the credential store | `ARCHITECTURE.md` |
| Manifest, packaging script or workflows | `docs/packaging.md`, the `release` skill |
| An npm command | This file's Commands table, the `desktop` skill, `docs/development.md`, `paseo.json` |
| A test harness added or changed | Its README; `npm test` finds it by itself |
| A file, guide or skill moved or removed | The guidance map above and every skill that names it |
