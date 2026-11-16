> **Scope:** Building, testing, versioning and releasing ImmichDrive, and the checks a release needs.
> **Governs:** `Directory.Build.props`, `package.json`, `windows-build.json`, `tests/**`, `ImmichDriveMSIX/**`, `.github/workflows/**`

# ImmichDrive development

## Prerequisites

- Windows 11 on x64 or ARM64 for the established build workflow. Building on Windows 10 22H2 has not been validated; the app's configured runtime minimum is separate from the build host.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows SDK packaging tools (`makeappx` / `makepri` / `signtool`); `build-msix.ps1` auto-acquires
  them via the `Microsoft.Windows.SDK.BuildTools` NuGet package if they aren't already installed.

`Directory.Build.props` is the single source of truth for the version and auto-detects the platform
from `PROCESSOR_ARCHITECTURE` (ARM64 → ARM64, otherwise x64). Override with `-p:Platform=x64` or
`-p:Platform=ARM64`.

## Build the app (dev)

```bash
npm run verify:desktop
```

Good for iterating on the settings UI. The Cloud Files provider and the COM thumbnail handler only
register cleanly from the packaged build, so use the MSIX for end-to-end testing.

## Build the MSIX

```bash
npm run package:desktop:dev       # sideload build (dev-signed, updates in place)
npm run package:desktop     # Store build (unsigned; the Store re-signs)
```

- Bump the tracked version for releases only. Local dev revisions change in staging.
- **Identity:** sideload (signed) builds swap in a local dev identity so they form a stable package
  family that updates your install in place; `-NoSign` (Store) builds keep the real Partner Center
  identity declared in `ImmichDriveMSIX/Package.appxmanifest`.
- `generate-msix-images.ps1` renders one master icon and downscales it into every Store logo/tile,
  the app `.ico`, its muted `ImmichDrive-Offline.ico` tray variant, and the in-app PNG. PowerShell
  build scripts must be **ASCII only** (Windows PowerShell 5.1 reads BOM-less `.ps1` as ANSI).

## Releases and distribution

- **The Microsoft Store is the install route**
  ([listing](https://apps.microsoft.com/detail/9MWC6165N7DH)). There is no portable download and
  no unpackaged download, because there is no honest one to offer: the Cloud Files sync root and
  the `IThumbnailProvider` COM server are registered only by `Package.appxmanifest`
  (`com:ExeServer` + `desktop3:CloudFiles`), so a build without package identity is not a working
  product. Building the MSIX yourself, as above, is how you run your own copy.
- **A GitHub release carries notes and the tag, nothing else.** No MSIX is attached, and the CI
  run publishes no Actions artifact either: this is a paid app in a public repo, and an Actions
  artifact is downloadable by anyone with read access, which on a public repo means anyone.
- **`ci.yml` is the push and pull request gate:** it runs `npm run validate` on Linux every time, and compiles both projects for x64 and ARM64 on Windows when app source, packaging or build configuration changed. `credential-expiry.yml` fails weekly from 60 days before the Store credential expires, using the `STORE_SECRET_EXPIRES` repository variable.
- **CI still builds the MSIX on every run** (`.github/workflows/build-msix.yml`, x64 and ARM64)
  even though it publishes nothing. That build is the only automated check that manifest
  stamping, `makepri`, `makeappx` and signing still work; without it, a break in packaging would
  first show up during a Store submission.
- **Store submission runs on release tags** through `.github/workflows/store-publish.yml`,
  using CLI v0.4.3 and the configured Entra secrets. Both unsigned architectures stay on one
  runner and are uploaded to Partner Center as a draft with the notes attached; a second,
  manual run on the tag submits it (see the packaging guide). The
  workflow uses `Tier1012` for the US $0.99 base price and can change regional prices. See
  [the packaging guide](packaging.md). The local `build-msix.ps1 -NoSign` path
  remains available for manual uploads.

## Architecture & internals

- **[ARCHITECTURE.md](../ARCHITECTURE.md)**: the two processes, the cfapi flow, the date layout,
  thumbnails-without-hydration, and storage paths.
- Topic deep-dives:

  | Topic | Doc |
  |---|---|
  | Cloud Files API, placeholders, hydration | [`docs/cloud-files.md`](cloud-files.md) |
  | Immich REST API surface used | [`docs/immich-api.md`](immich-api.md) |
  | Thumbnail shell extension (COM) | [`docs/thumbnails.md`](thumbnails.md) |
  | Read-only drive and Upload folder | [`docs/read-only.md`](read-only.md) |
  | MSIX packaging and Store workflows | [`docs/packaging.md`](packaging.md) |

- Coding conventions for agents and contributors are in [`.agents/guides/`](../.agents/guides/)
  (settings, UI, P/Invoke).

## Versioning

- `<Version>` in `Directory.Build.props` is the single source of truth. The assembly version, the
  MSIX manifest (`VERSION_PLACEHOLDER`, stamped by `build-msix.ps1`) and the release tag all derive
  from it. MSIX needs four parts, so the script appends `.0`; the Store requires that fourth part
  to be `0`. Local dev builds raise it only in the staged copy.
- `DriveManager.CurrentLayoutVersion` is separate from `<Version>`. Bump it only when the on-disk
  folder or file naming changes; it forces a one-time clean rebuild of the placeholder tree, with
  old index metadata carried forward so the library is not re-enriched over the network.
- **In-app update check:** for packaged copies, the About page's **Check for Updates** opens the
  Microsoft Store's updates page, and the Store handles the rest. For unpackaged development
  builds, `Services/UpdateService.cs` checks GitHub Releases when clicked and offers **View
  Release** for a newer tag. The app makes no automatic update check.
- Releases follow the `release` skill in `.agents/skills/release/`.

## Tech stack

- .NET 10, target `net10.0-windows10.0.22000.0`, platforms `x64` and `ARM64`
- WinUI 3 / Windows App SDK 1.8
- CommunityToolkit.Mvvm (`[ObservableProperty]`), `System.Text.Json` settings,
  Microsoft.Data.Sqlite index, NLog
- Native tray via `Shell_NotifyIcon`; Cloud Files API via P/Invoke (`cldapi.dll`)

The SDK target remains Windows build 22000; it does not set the runtime minimum. Both executables set `SupportedOSPlatformVersion` to `10.0.19045.0`, the WinUI project sets the same `TargetPlatformMinVersion`, and the MSIX manifest uses that minimum too. Keep these aligned when changing OS requirements. This enables Windows 10 22H2 installation without changing the packaging script's target-framework paths. Mica falls back to a solid colour on Windows 10; the flyout's optional rounded-corner request is ignored when unsupported.

## Tests

`npm test` runs every portable test and `npm run validate` runs the whole Linux gate: `npm run format:check`, then the tests. `npm run format` rewrites every C# project outside `external/` to the root `.editorconfig`, which is winui-app-kit's file copied unchanged from the tag the app pins; never hand-edit it. Both work on Linux with either the .NET SDK pinned in `global.json` or Docker; `scripts/dotnet` and `scripts/pwsh` pick whichever is present, and Docker runs are capped at 3 GB (`KIT_DOTNET_MEMORY`). Run them as a non-root user. `npm run test:scripts` runs only the PowerShell script tests.

`scripts/run-tests` finds the tests itself, so a new harness needs no registration:

- Every project under `tests/` is a portable harness (`tests/<App>.<Area>.Tests/`, run with `dotnet run`), and each has a README saying what it covers and does not prove: API key migration, Upload folder status, and the Cloud Files structure layout.
- Every `*.Tests.ps1` under `.github/scripts/` tests a Store workflow helper.

Neither proves anything about the installed app; see the compatibility checks below.

## Compatibility verification

The native Cloud Files structure layout has a dependency-free executable check. Run it from a disposable Windows TEMP copy, following the staging policy in the shared desktop build guide:

```powershell
dotnet run --project tests/ImmichDrive.CloudFilterLayout.Tests/ImmichDrive.CloudFilterLayout.Tests.csproj -c Release
# npm test also runs this check on Linux x64, which shares the Windows x64 layout.
```

Run it natively on x64 and ARM64 to check the ABI on both shipping architectures. It compares the linked production declaration against the offsets in Microsoft's `cfapi.h`, including the request key after both pointers. It does not call Cloud Files or alter a sync root.

Windows 10 22H2 end-to-end verification is still pending. A compile or package build does not establish compatibility. Before releasing this compatibility change, complete these checks on Windows 10 22H2 and Windows 11:

- Build both shipping architectures on Windows, including XAML and MSIX packaging. Investigate platform-compatibility warnings introduced by lowering the runtime minimum, and confirm the XAML compiler accepts `TargetPlatformMinVersion=10.0.19045.0`.
- Verify package installation and update, startup and sign-in launch, settings and tray flyout in light/dark themes, readable backgrounds and icons, and multi-monitor/DPI behavior. On Windows 10, include top and side taskbars: the flyout currently anchors to the bottom-right of the work area regardless of the tray position.
- Verify sync-root registration, thumbnails without original-file hydration, opening and attaching photos/videos, pinning and freeing space, Upload processing, and recovery after the server goes offline.
- Verify downloads of large files spanning multiple chunks, cancellation mid-download followed by reopening, concurrent opens of the same file, and video seeking. The corrected native structure now sends the actual request key to Windows; cancellation handling remains a no-op and must be exercised on both OS versions.

Complete these checks before tagging a release. v0.1.49 and v0.1.50 shipped with them still pending, by an explicit one-time exception that does not establish runtime compatibility and does not extend to later releases. Windows CI passed the layout check and built x64 and ARM64 packages for [v0.1.49](https://github.com/Dynamic-Solutions-Canada/immich-drive/actions/runs/41942150001). A `v*` tag starts Store submission independently of the GitHub packaging workflow. Both workflows run the layout check before building, but neither supplies Windows 10 runtime evidence. For a compile/package check without publication, manually dispatch `build-msix.yml` on a pushed branch.

Windows 10 22H2 reached [end of standard support](https://learn.microsoft.com/en-us/windows/release-health/release-information) on October 14, 2025; ESU enrollment has separate terms. As checked on November 4, 2026, [.NET 10's supported OS list](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) includes specific Windows 10 Enterprise/IoT versions but does not list 22H2. Enabling compatibility does not establish Microsoft-supported .NET operation on 22H2. Windows 10 Enterprise LTSC 2021 and IoT Enterprise LTSC 2021 use build 19044 and are excluded by this app's 19045 minimum, even though those editions have different servicing lifecycles. Supporting those editions would require a separate compatibility decision and validation.

## Build and install

Source is authoritative in WSL. Use `npm run package:desktop:dev`, `npm run verify:desktop`,
`npm run install:desktop` and `npm run package:desktop` from the repository root; the
[shared Windows build tools](https://github.com/Dynamic-Solutions-Canada/windows-build-tools) document what each does, how the disposable Windows TEMP copy
works, and the local build VM. Never build a D: checkout or point Windows tools at the WSL source,
and never uninstall to get past an update error.

Specific to this app:

- `verify:desktop` compiles only `ImmichDrive/`. Changes to the thumbnail provider, or to a file
  linked into it, need `package:desktop`, which compiles both projects.
- Dev installs use the separate `ImmichDrive` package family, so they sit beside a Store install.
  `npm run install:desktop -- --store-identity` replaces the Store copy in place for testing an
  update over it; it builds a signed `.msixbundle`, because Windows rejects a plain MSIX over an
  installed bundle (`0x80073CF3`). Store updates resume once a higher version is published.
- A worktree created without submodules needs `git submodule update --init` before a Windows build.
