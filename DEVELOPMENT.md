# ImmichDrive — Development

## Prerequisites

- Windows 11 (build 22621+)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows SDK packaging tools (`makeappx` / `makepri` / `signtool`) — `build-msix.ps1` auto-acquires
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
npm run build:desktop       # sideload build (dev-signed, updates in place)
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
- **CI still builds the MSIX on every run** (`.github/workflows/build-msix.yml`, x64 and ARM64)
  even though it publishes nothing. That build is the only automated check that manifest
  stamping, `makepri`, `makeappx` and signing still work; without it, a break in packaging would
  first show up during a Store submission.
- **Store submission runs on release tags** through `.github/workflows/store-publish.yml`,
  using CLI v0.4.3 and the configured Entra secrets. Both unsigned architectures stay on one
  runner and go directly to Partner Center. Manual runs default to draft review. The
  workflow uses `Tier1012` for the US $0.99 base price and can change regional prices. See
  [the packaging guide](.codex/docs/installer.md). The local `build-msix.ps1 -NoSign` path
  remains available for manual uploads.

## Architecture & internals

- **[ARCHITECTURE.md](ARCHITECTURE.md)** — the two processes, the cfapi flow, the date layout,
  thumbnails-without-hydration, and storage paths.
- Topic deep-dives live under **`.codex/docs/`**:

  | Topic | Doc |
  |---|---|
  | Cloud Files API / placeholders / hydration | [`.codex/docs/cloud-files.md`](.codex/docs/cloud-files.md) |
  | Immich REST API surface used | [`.codex/docs/immich-api.md`](.codex/docs/immich-api.md) |
  | Thumbnail shell extension (COM) | [`.codex/docs/thumbnails.md`](.codex/docs/thumbnails.md) |
  | Read-only drive + Upload folder | [`.codex/docs/read-only.md`](.codex/docs/read-only.md) |
  | Settings persistence conventions | [`.codex/docs/user-settings.md`](.codex/docs/user-settings.md) |
  | WinUI 3 XAML conventions | [`.codex/docs/xaml.md`](.codex/docs/xaml.md) |
  | P/Invoke conventions | [`.codex/docs/pinvoke.md`](.codex/docs/pinvoke.md) |
  | Versioning / release | [`.codex/docs/versioning.md`](.codex/docs/versioning.md) |
  | MSIX packaging | [`.codex/docs/installer.md`](.codex/docs/installer.md) |

## Tech stack

- .NET 10, target `net10.0-windows10.0.22000.0`, platforms `x64` and `ARM64`
- WinUI 3 / Windows App SDK 1.8
- CommunityToolkit.Mvvm (`[ObservableProperty]`), `System.Text.Json` settings,
  Microsoft.Data.Sqlite index, NLog
- Native tray via `Shell_NotifyIcon`; Cloud Files API via P/Invoke (`cldapi.dll`)

## Windows desktop workflow

Source is authoritative in WSL. Read `.codex/docs/windows-development.md` and use
`npm run build:desktop`, `npm run verify:desktop`, `npm run install:desktop`, and
`npm run package:desktop` from the repository root. The common bridge builds in
a disposable C: Windows TEMP directory and cleans it after use. Never use a D:
checkout or build directly against WSL source with Windows tools. Dev install
versions change only in the temporary copy; never uninstall to bypass an update
error. Store releases keep their existing project-specific release workflow.
