> **Scope:** Building, testing, versioning and releasing ImmichDrive, and the checks a release needs.
> **Governs:** `Directory.Build.props`, `package.json`, `windows-build.json`, `tests/**`, `ImmichDriveMSIX/**`, `.github/workflows/**`

# ImmichDrive development

## Prerequisites

- Windows 11 on x64 or ARM64 for the established build workflow. Building on Windows 10 22H2 has not been validated; the app's configured runtime minimum is separate from the build host.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows SDK packaging tools (`makeappx` / `makepri` / `signtool`); winui-app-kit's packaging script auto-acquires
  them via the `Microsoft.Windows.SDK.BuildTools` NuGet package if they aren't already installed.
- [lefthook](https://lefthook.dev) for the git hooks in `lefthook.yml` (they block committing signing material, credentials, app data files and files over 5 MB). The main check runs winui-app-kit's `scripts/devkit precommit`, which needs Node 22.12 or later, below 25, and installs its pinned tools inside the kit on first use. Install it once per machine, then run `lefthook install` once per clone; the Paseo worktree setup runs it for you.
- The `external/` submodules (`git submodule update --init`): winui-app-kit supplies the shared build
  configuration, and apps-promo the "Our other apps" page.

`Directory.Build.props` holds the app version, the single source of truth, after importing
winui-app-kit's shared build properties, which also pick the platform from the build machine's
architecture. Override with `-p:Platform=x64` or `-p:Platform=ARM64`.

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
- The app icon and MSIX images come from the SVG masters in `assets/brand/` (`icon.svg`, and `icon-mono.svg` for the light taskbar), sized by devkit through `npm run icons`, which writes `ImmichDriveMSIX/Images/`, `ImmichDrive/Resources/ImmichDrive.ico` and `.png`, and `assets/generated/` (the README, website and promo icon). The output is committed; never edit it by hand. `npm run check:icons`, part of `validate`, fails when it no longer matches the masters; it needs Node 22.12 or later, below 25. The offline tray icon is not generated; see [packaging.md](packaging.md).
- PowerShell build scripts must be **ASCII only** (Windows PowerShell 5.1 reads BOM-less `.ps1` as ANSI).

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
- **`ci.yml` is the push and pull request gate:** it runs `npm run validate` on Linux every time, and compiles both projects for x64 and ARM64 on Windows when app source, packaging or build configuration changed. `credential-expiry.yml` fails weekly from 60 days before the Store credential expires, using the organization variable `STORE_SECRET_EXPIRES` (`gh variable set STORE_SECRET_EXPIRES --org Dynamic-Solutions-Canada --body YYYY-MM-DD`).
- **CI still builds the MSIX on every run** (`.github/workflows/build-msix.yml`, x64 and ARM64)
  even though it publishes nothing. That build is the only automated check that manifest
  stamping, `makepri`, `makeappx` and signing still work; without it, a break in packaging would
  first show up during a Store submission.
- **Store submission runs on release tags** through `.github/workflows/store-publish.yml`, a caller of winui-app-kit's reusable Store workflow,
  using CLI v0.4.3 and the configured Entra secrets. Both unsigned architectures stay on one
  runner and are uploaded to Partner Center as a draft with the notes attached; a second,
  manual run on the tag submits it (see the packaging guide). The
  workflow uses `Tier1012` for the US $0.99 base price and can change regional prices. See
  [the packaging guide](packaging.md). The kit's packaging script with `-NoSign`
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
  MSIX manifest (`VERSION_PLACEHOLDER`, stamped by winui-app-kit's packaging script) and the release tag all derive
  from it. MSIX needs four parts, so the script appends `.0`; the Store requires that fourth part
  to be `0`. Local dev builds raise it only in the staged copy.
- `DriveManager.CurrentLayoutVersion` is separate from `<Version>`. Bump it only when the on-disk
  folder or file naming changes; it forces a one-time clean rebuild of the placeholder tree, with
  old index metadata carried forward so the library is not re-enriched over the network.
- **In-app update check:** for packaged copies, the About page's **Check for Updates** opens the
  Microsoft Store's updates page, and the Store handles the rest. For unpackaged development
  builds, the kit's `UpdateService` checks GitHub Releases when clicked and offers **View
  Release** for a newer tag. The app makes no automatic update check.
- Releases follow the `release` skill in `.agents/skills/release/`.

## Tech stack

- .NET 10, target `net10.0-windows10.0.22000.0`, platforms `x64` and `ARM64`
- WinUI 3 / Windows App SDK 2.7 (the manifest depends on the `Microsoft.WindowsAppRuntime.2` framework, MinVersion 2.7.0.0)
- CommunityToolkit.Mvvm (`[ObservableProperty]`), Microsoft.Data.Sqlite index, NLog
- [winui-app-kit](https://github.com/Dynamic-Solutions-Canada/winui-app-kit) for the tray, flyout,
  settings store, protected store, startup, theme, window chrome, update check, logging and the
  shared build configuration; package versions come from `Directory.Packages.props` and the kit
- Cloud Files API via P/Invoke (`cldapi.dll`)

The SDK target remains Windows build 22000; it does not set the runtime minimum. Both executables set `SupportedOSPlatformVersion` to `10.0.19045.0`, the WinUI project sets the same `TargetPlatformMinVersion`, and the MSIX manifest uses that build with a revision, `10.0.19045.5011` (see below). Keep these aligned when changing OS requirements. This enables Windows 10 22H2 installation without changing the packaging script's target-framework paths. Mica falls back to a solid colour on Windows 10; the flyout's optional rounded-corner request is ignored when unsupported.

## Tests

`npm test` runs every portable test and `npm run validate` runs the whole Linux gate: `npm run format:check`, `check:gitignore`, `check:l10n`, `check:listing`, `check:notices`, `check:screenshots` (the Store screenshots in `store-assets/manifest.json`, checked by winui-app-kit's renderer as [store-assets/README.md](../store-assets/README.md) describes; no Docker needed, and `npm run shots:render` renders them in a capped container), `check:hero` (the generated first Store screenshot in each listing locale is exactly what devkit draws from its inputs; after changing the icon, the Home page capture or a `heroHeadline` or `heroSubline` in `listings.json`, run `npm run hero` and commit the output; the first run downloads devkit's pinned fonts, about 27 MB, into the user's cache), `check:fake-immich` (the fake Immich server against a recorded Immich release, [tests/fake-immich](../tests/fake-immich/README.md)), then the tests. `npm run format` rewrites every C# project outside `external/` to the root `.editorconfig`, which is winui-app-kit's file copied unchanged from the tag the app pins; never hand-edit it. Both run through winui-app-kit's `scripts/dotnet` and `scripts/pwsh`, which work on Linux with either the .NET SDK pinned in `global.json` or Docker; Docker runs are capped at 3 GB (`KIT_DOTNET_MEMORY`). Run them as a non-root user. `npm run check:l10n` is the kit's catalog check ([localization](localization.md)). `npm run check:listing` is the kit's Store listing check, configured by the `listing` block in `windows-build.json`. `npm run check:notices` fails when `ImmichDrive/THIRD-PARTY-NOTICES.txt` no longer matches the packages or the About page's link to it; `npm run generate:notices` rewrites it (both are the kit's `generate-notices.py`, which reads the restore graph `format:check` leaves).

The kit's `scripts/run-tests` finds the harnesses itself, so a new one needs no registration: every project under `tests/` is a portable harness (`tests/<App>.<Area>.Tests/`, run with `dotnet run`), and each has a README saying what it covers and does not prove: API key migration, Upload folder status, and the Cloud Files structure layout. The Store workflow's helpers and their tests are winui-app-kit's, tested there.

Neither proves anything about the installed app; see the compatibility checks below.

## Compatibility verification

The native Cloud Files structure layout has a dependency-free executable check. Run it from a disposable Windows TEMP copy, following the staging policy in the shared desktop build guide:

```powershell
dotnet run --project tests/ImmichDrive.CloudFilterLayout.Tests/ImmichDrive.CloudFilterLayout.Tests.csproj -c Release
# npm test also runs this check on Linux x64, which shares the Windows x64 layout.
```

Run it natively on x64 and ARM64 to check the ABI on both shipping architectures. It compares the linked production declaration against the offsets in Microsoft's `cfapi.h`, including the request key after both pointers. It does not call Cloud Files or alter a sync root.

Windows 10 22H2 status: a user reported the Store release 0.1.50 working on Windows 10 22H2 (reported 2026-11-16). The winui-app-kit migration that followed (tray, flyout, startup, settings store) was accepted on Windows 11 only, on the maintainer's decision; the kit documents its Windows 10 differences (square flyout corners, acrylic only where supported). A compile or package build does not establish compatibility. Before releasing this compatibility change, complete these checks on Windows 10 22H2 and Windows 11:

- Build both shipping architectures on Windows, including XAML and MSIX packaging. Investigate platform-compatibility warnings introduced by lowering the runtime minimum, and confirm the XAML compiler accepts `TargetPlatformMinVersion=10.0.19045.0`.
- Verify package installation and update, startup and sign-in launch, settings and tray flyout in light/dark themes, readable backgrounds and icons, and multi-monitor/DPI behavior. On Windows 10, include top and side taskbars: the flyout currently anchors to the bottom-right of the work area regardless of the tray position.
- Verify sync-root registration, thumbnails without original-file hydration, opening and attaching photos/videos, pinning and freeing space, Upload processing, and recovery after the server goes offline.
- Verify downloads of large files spanning multiple chunks, cancellation mid-download followed by reopening, concurrent opens of the same file, and video seeking. The corrected native structure now sends the actual request key to Windows; cancellation handling remains a no-op and must be exercised on both OS versions.

- For the release after 0.1.50 (the shared settings window, localization, the kit's packaging and the thumbnail log), also check, in light, dark and high contrast:
  - Home, Connection, Settings (Start with Windows, Language, Theme), About (with the "How it works" and "Immich" cards, the privacy policy and third-party notices links), Our other apps, the tray flyout and the tray menu.
  - Switch the language to German and to Japanese and look for clipped or overlapping text on every page, the flyout and the tray menu; check that a WinUI control's own text, such as a text box's Cut, Copy and Paste menu, is translated too; then return to "Use system setting".
  - On Connection, an `http://` address on the network shows the unencrypted-connection warning (also read by a screen reader), while `https://` and `http://localhost` do not, and the http address still connects.
  - Settings, Reset settings: Cancel changes nothing; Reset returns the theme, language and window size to defaults, leaves the drive connected and syncing, and leaves a `settings.json.<time>.reset` file in the logs folder.
  - With a settings file made unreadable, the next launch opens Settings with the "settings were reset" notice.
  - Make a thumbnail fail (for example, disconnect the server and open a folder that was never shown), then find `thumbnail-provider.log` through About, "Open logs folder": it should name no file paths, and `C:\ProgramData\ImmichDrive\thumb.log` should be gone after a launch.

Complete these checks before tagging a release. v0.1.49 and v0.1.50 shipped with them still pending, by an explicit one-time exception that does not establish runtime compatibility and does not extend to later releases. Windows CI passed the layout check and built x64 and ARM64 packages for [v0.1.49](https://github.com/Dynamic-Solutions-Canada/immich-drive/actions/runs/41942150001). A `v*` tag starts Store submission independently of the GitHub packaging workflow. Both workflows run the layout check before building, but neither supplies Windows 10 runtime evidence. For a compile/package check without publication, manually dispatch `build-msix.yml` on a pushed branch.

**Windows 10 needs the October 2024 update or later.** .NET 9 and later turn on CET shadow stacks by default ([Microsoft: CET support](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/9.0/cet-support)). On a Windows 10 build without Windows' CET fixes, the runtime stops at startup: the Event Log shows ".NET Runtime ... internal error ... exit code 0x80131506" in KERNELBASE, and a console app prints "Your Windows doesn't fully support CET. Please install all available Windows updates." This was reproduced on 2026-11-17 with build 19045.2965, for the current app, for the v0.1.50 release and for a plain .NET 10 console app. A .NET maintainer reads the same fatal error as CET support and points at "1904x.5007" as the lowest fixed version ([dotnet/runtime#121922](https://github.com/dotnet/runtime/issues/121922)); that number comes from a community measurement that ntdll `10.0.19041.5007` first shipped in KB5044273, October 8, 2024, OS build 19045.5011 ([dotnet/runtime#110920](https://github.com/dotnet/runtime/issues/110920#issuecomment-2939318863)). Microsoft confirms only that fully patched supported Windows works and does not list the revision; [KB5044273](https://support.microsoft.com/en-us/help/5044273) does not mention CET. So the manifest's `MinVersion` is `10.0.19045.5011`: Windows refuses the install on an older build with an "update Windows" message instead of the app failing silently. Opting out with `<CETCompat>false</CETCompat>` would run everywhere but drop CET hardening on every machine, Windows 11 included; it is used only for local diagnosis, never committed. Re-check the threshold if a user on 19045.5011 or later reports this crash.

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
