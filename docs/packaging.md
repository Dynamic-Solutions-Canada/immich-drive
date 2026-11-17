> **Scope:** The MSIX manifest, the packaging script, package identities, and the CI and Store workflows.
> **Governs:** `ImmichDriveMSIX/**`, `windows-build.json`, `.github/workflows/{build-msix,store-publish}.yml`, `Directory.Build.props`

# MSIX packaging

`ImmichDriveMSIX/` packages the app **and** the thumbnail extension into one MSIX. MSIX is
required so the COM thumbnail handler and the sync-provider shell integration register
declaratively (no manual HKCR writes).

## Pieces

- `Package.appxmanifest`: one `<Application Id="App">` (the WinUI tray app,
  `Windows.FullTrustApplication`). Its `Extensions` declare:
  - `com:Extension` (`windows.comServer`): an **out-of-process** `com:ExeServer`
    (`ImmichDrive.ThumbnailProvider.exe`) registering the thumbnail provider CLSID. The shell
    launches it on demand to render placeholder thumbnails.
  - `desktop3:CloudFiles` (`windows.cloudFiles`): scopes the thumbnail/state/property handlers
    to our sync-root placeholders only (the same CLSID).
  - `uap5:StartupTask`: auto-start at sign-in so the provider is running to hydrate files.

  `VERSION_PLACEHOLDER` / `ARCH_PLACEHOLDER` are stamped by the build script. Capabilities:
  `internetClient` (reach the Immich server) + `runFullTrust`.
- **Packaging script:** winui-app-kit's `external/winui-app-kit/build/build-msix.ps1`, the family's one
  MSIX script (KIT-9, DIST-1), driven by `windows-build.json`; the app keeps no script of its own.
  It reads the version, publishes `ImmichDrive` (self-contained, `WindowsPackageType=MSIX`) and the
  `companions` entry `ImmichDrive.ThumbnailProvider` (self-contained, so the out-of-proc comhost
  resolves the .NET runtime from its own folder when the shell activates it), assembles the layout
  (exe + extension EXE/DLLs + compiled XAML `.xbf` + stamped manifest + `Images`), builds one
  `resources.pri` for the nine declared languages, packages with `makeappx` and signs with
  `signtool`. The manifest's `ms-resource:Resources/AppDescription` is written for every language
  from the `manifest_strings` catalogs (`ImmichDrive/Localization/Catalogs`). It auto-acquires the
  SDK BuildTools via NuGet when missing. Signing material is always a parameter
  (`-TrustedPfxPath`/`-TrustedPfxPassword`, which the shared bridge supplies); it never generates a
  certificate. `-NoSign` for Store uploads or an unsigned check. The README's "Packaging" section in
  the kit lists every `windows-build.json` key.
- `generate-msix-images.ps1`: renders **one** master icon (the "Photo Panes" mark: a dark
  slate rounded tile with a 2x2 grid of three tiny photo scenes plus a white cloud pane; an
  original design, deliberately NOT Immich's iris logo, which got the app rejected under
  Store policy 10.1.1.11) and downscales it (4x supersampling per size) into every Store
  tile/logo/splash PNG under `Images\`, **plus** the multi-size app `.ico`
  (`ImmichDrive/Resources/ImmichDrive.ico`), its muted tray variant
  (`ImmichDrive-Offline.ico`, the same mark run through a luminance `ColorMatrix` with an amber
  pip, shown while the server is unreachable), and a 256px in-app PNG
  (`ImmichDrive/Resources/ImmichDrive.png`). The same detailed art is used at every size
  (no simplified small-size variants, by the maintainer's explicit choice). **ASCII only.**

## Identity stamping (dev vs Store)

The manifest's `<Identity>` holds the **real Partner Center** values: `Name`
`27766TechnicallyReal.ImmichDrive`, `Publisher` `CN=C21E6CEF-D0D1-4497-93F9-3718D054DA0E`,
`PublisherDisplayName` `Dynamic Solutions (CA)`. Partner Center validates this display name separately from the signing publisher; keep it aligned with the account's current display name. The v0.1.49 submission was rejected because the previous display name, `Dynamic Solutions Canada`, no longer matched the account.

The Store listing is named **Drive for Immich**, while manifest `DisplayName` values identify
the installed package, app, thumbnail server, and startup task as **ImmichDrive**. Keep that
visible-name distinction separate from `<Identity>`: changing the identity would create a
different package family and break in-place updates for existing Store installs.

- **Signed (sideload) builds**: the packaging script sets `Name` to `dev_identity` (`ImmichDrive`) and `Publisher`
  → the signing cert's subject so sideload installs form a stable local package family that
  updates in place.
- **Explicit Store-identity test builds**: `npm run install:desktop -- --store-identity`
  passes `-StoreIdentity` (`store_identity_switch`), creates a signed `.msixbundle` to match Store distribution,
  retains the Store name, and requires the signing certificate
  to match its publisher. The shared bridge allocates the dev revision against the Store
  family and updates it in place, preserving package data. A higher Store version is needed
  for a later Store update. Default signed builds retain their separate dev family.
- **`-NoSign` (Store) builds**: keep the real identity untouched; the Store re-signs during
  ingestion.

## Distribution (what gets published, and what deliberately does not)

`store-publish.yml` submits to product `9MWC6165N7DH`. It is a short caller of winui-app-kit's reusable Store workflow (CI-10), pinned to the same tag as the `external/winui-app-kit` submodule (the run stops if they differ); the Partner Center helpers and their tests live in the kit, not here. The caller sets the product ID, `price_tier: Tier1012` and a `pre_build` that runs the Cloud Files layout check and generates the MSIX images. Every run first runs `npm run validate` on Linux. A manual run with `dry_run` builds, bundles and checks everything and makes no Store call, so it needs no secrets: use it to prove a workflow or packaging change before a real release (`gh workflow run store-publish.yml --ref main -f dry_run=true`). `credential-expiry.yml` calls the kit's credential reminder the same way.

The kit's workflow pins [Microsoft Store CLI v0.4.3](https://github.com/microsoft/msstore-cli/releases/tag/v0.4.3),
builds unsigned x64 and ARM64 packages on one runner, combines them into a real
architecture-aware `.msixbundle` stamped with the app version, and wraps that in a versioned `.msixupload`,
and uploads it to Partner Center on `v*` tags as a draft. It uploads no public binary artifacts.
MakeAppx must receive `/bv` so the bundle does not get a date-based version that
could outrank the next app release.
**A tag never publishes (REL-7).** The tag run stops at a `PendingCommit` draft with the release notes attached, and its run summary names the draft ID. After reviewing the draft in Partner Center, run the workflow manually **on the same tag** with `submit_draft` set to that ID and `no_commit` unchecked. That run refuses any ref other than `v<version>` from `Directory.Build.props`, verifies the draft is the current pending submission with the expected versioned upload, re-applies and checks the notes from the tag, then commits. Manual full runs also default `no_commit` to true.
Certification and the submission's publishing settings determine when it becomes available.
The read-only `inspect-store-submission.yml` action reports validation details for an exact submission ID without exposing signed upload URLs. The API refuses updates to `CommitFailed` submissions. For a confirmed package-validation failure, correct the package and use `replace_failed_draft` with the exact failed ID, `failed_upload` with its exact upload filename, and `no_commit=false`. The workflow verifies the failed state and upload before deleting only that unpublished submission, then creates a corrected submission from the published app. `repair_draft` remains limited to editable `PendingCommit` drafts; it cannot repair `CommitFailed` or `Canceled` submissions.
Each release includes `release-notes/<version>.md` and its translations `release-notes/<version>.<catalog>.md` for the six other wording catalogs: user-facing lines only, with no heading and no AI disclosure (the GitHub release adds both), each at most 1,500 characters. The workflow stages the package upload,
sets each listing locale's What's new from its own notes (en-CA and fr-CA use the en-US and fr-FR notes), and verifies they were saved.
Other listing fields and publishing settings are preserved.
GitHub release notes use the same blurb and never attach binaries.
The same `submit_draft` run is the recovery path when a commit fails after upload: it validates
the expected upload and notes, then commits without rebuilding or replacing the draft.

The published base price is US $0.99. The API may report it as `PriceId: "Base"`, which
the CLI cannot round-trip. This workflow explicitly supplies `Tier1012`, the US $0.99
tier identified by a [Microsoft maintainer](https://github.com/microsoft/msstore-cli/pull/175#issuecomment-5791491206).
Tier pricing can change converted prices in other markets; review the ingested submission
in Partner Center. The workflow checks for a pending submission before invoking the CLI,
because the CLI would otherwise delete an existing draft.

The repository secrets are `AZURE_AD_TENANT_ID`, `AZURE_AD_APPLICATION_CLIENT_ID`,
`AZURE_AD_APPLICATION_SECRET`, and `SELLER_ID`. All four were present when checked on
September 22, 2026. The first tier-based submission accepted authentication and Tier1012,
but a ZIP of loose MSIX packages ingested as x64 only. Submission 11 was withdrawn to
draft before publication. The API reports it as `Canceled`, so it cannot be edited.
Run `replace_canceled_draft` with its exact ID and `no_commit` disabled to remove only
that canceled submission and publish a corrected bundle. Verify both architectures
and pricing in Partner Center after ingestion.
Future tag runs use the MSIX bundle format.
The corrected [Store run](https://github.com/RyanEwen/ImmichDrive/actions/runs/35898180603)
committed Submission 11 (`1152921505701961960`) with a 0.1.46.0 bundle covering
x64 and ARM64. Partner Center shows US $0.99, with 36 of 240 regional prices changed
from the published Base schedule. At that verification it was in certification with automatic publication enabled.

The [v0.1.50 Store run](https://github.com/Dynamic-Solutions-Canada/immich-drive/actions/runs/41944626406) replaced failed v0.1.49 submission `1152921505702318922` and committed submission `1152921505702319184`. The accepted `0.1.50.0` upload bundles x64 and ARM64, retains `Tier1012`, and reached `Certification` with publishing mode `Immediate`. The publisher display name is now `Dynamic Solutions (CA)`. Windows builds, layout checks, recovery tests, and packaging passed; Windows 10 end-to-end runtime validation remains pending. The [v0.1.50 GitHub release](https://github.com/Dynamic-Solutions-Canada/immich-drive/releases/tag/v0.1.50) contains notes and no binaries.

For manual fallback, run `pwsh -File external/winui-app-kit/build/build-msix.ps1 -Platform x64 -NoSign` and
`-Platform ARM64 -NoSign` from the repo root on Windows, then upload both individual `.msix` files in Partner Center.

`.github/workflows/build-msix.yml` publishes **no binary at all**: no release asset, no Actions
artifact. Two reasons, and both have to stop being true before that changes:

- ImmichDrive is a **paid app in a public repo**. An Actions artifact needs only read access to
  download, which on a public repo is everyone.
- There is **no usable unpackaged build to hand out anyway**. The sync root and the thumbnail COM
  server are registered by `Package.appxmanifest` alone (`com:ExeServer` + `desktop3:CloudFiles`),
  so a build without package identity is not the product. See `cloud-files.md`, where sync-root
  registration is documented as needing package identity.

So the install route is the Microsoft Store (`9MWC6165N7DH`), and anyone wanting to run it from
source builds the MSIX themselves.

**The workflow still builds the MSIX on every run, and that is not pointless.** It is the only
automated check that version/arch stamping, `makepri`, `makeappx` and `signtool` still work
together; drop it and the next packaging break is discovered in a Store submission.

## Store update checks (kit `UpdateService`)

For packaged copies, About opens the Microsoft Store's Updates and downloads page with
`ms-windows-store://downloadsandupdates`. The Store manages the account-wide update
check and installation. The URI opens the page; the app does not claim a fresh scan
completed. The button remains labeled **Check for Updates**; the Store handles any update
or restart prompt.

Unpackaged development builds compare the newest GitHub release (by version, `vX.Y.Z` tags only)
with the running version and offer **View Release** when newer. The kit's `UpdateService` does both,
built once in `Services/SourceRepository.cs` with the owner and repository.

## Notes

- The MSIX minimum is Windows 10 22H2 with the October 2024 update (`10.0.19045.5011`), on x64 or ARM64. The revision is in the manifest only: `SupportedOSPlatformVersion` in both projects and `TargetPlatformMinVersion` in the WinUI project stay `10.0.19045.0`, since they take no revision. Why the revision: see [development.md](development.md#compatibility-verification). Expected but unverified: the Store may still offer the app on an older Windows 10 build, since no Microsoft statement says it filters by revision, but the Store installs through Windows' own deployment, whose full-version check refuses the install with an "update Windows" message. The listing's requirements line covers the rest. The SDK target stays `net10.0-windows10.0.22000.0`; the packaging script's output paths still use that target. Windows 10 end-to-end validation is pending; see [development.md](development.md#compatibility-verification) for the acceptance checklist.
- Bump tracked `<Version>` for releases only. Dev installs use a staging-only
  revision through `npm run install:desktop`; see the [shared Windows build tools](https://github.com/Dynamic-Solutions-Canada/windows-build-tools).
- The thumbnail handler + cloud-files shell integration register via the manifest
  (`com:ExeServer` + `desktop3:CloudFiles`), not HKCR pokes.
- Packaged AppData is redirected per-package; the app writes a breadcrumb to
  `C:\ProgramData\ImmichDrive\location.txt` (never redirected) so the out-of-process thumbnail
  extension can find `settings.json`, `index.db` and the protected API key file.
- The Cloud Files sync root is registered at **runtime** (`StorageProviderSyncRootManager`),
  not by the installer.

## Local WSL build and install

Use `npm run package:desktop:dev`, `npm run verify:desktop`, `npm run install:desktop`,
and `npm run package:desktop` from WSL. See the [shared Windows build tools](https://github.com/Dynamic-Solutions-Canada/windows-build-tools).
Direct PowerShell packaging commands above describe release internals, not the
local development entry point. Never uninstall to bypass a same-version error.
