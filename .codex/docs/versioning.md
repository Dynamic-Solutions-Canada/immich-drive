# Versioning

- `<Version>` in `Directory.Build.props` is the **single source of truth** (currently
  `0.1.48`). App assembly version, the MSIX manifest (`VERSION_PLACEHOLDER`, stamped by
  `build-msix.ps1`), and the CI release tag all derive from it.
- The MSIX manifest needs a 4-part version, so `build-msix.ps1` reforms the 3-part `<Version>`
  as `X.Y.Z.0` (appends `.0`). The Store requires the 4th part (revision) to be `0`, so that
  slot is `0` for Store releases. Local dev builds increment it only in staging.
- Bump tracked `<Version>` for releases only. `npm run build:desktop` and
  `npm run install:desktop` assign a temporary dev revision without editing source.
- Release: bump → follow `.codex/commands/update-docs.md` → commit `Bump version to vX.Y.Z` (no Co-Authored-By trailer)
  → `git tag -a vX.Y.Z` → push. The `build-msix` Action builds x64 + ARM64 and creates a
  GitHub release from the tag.
- Add user-facing notes in `release-notes/X.Y.Z.txt` before tagging. The Store workflow
  verifies the English listings' What's new text before committing; GitHub uses the same blurb.
- **That release carries notes and the tag only, never a package.** CI attaches no MSIX and
  uploads no Actions artifact, because this is a paid app in a public repo and because an
  unpackaged build cannot register the sync root or the thumbnail handler anyway. The MSIX is
  still built on every run as the packaging check. The separate `store-publish.yml` submits
  on release tags with the verified US $0.99 `Tier1012`; manual runs default to draft review.
  See `installer.md`.

## In-app update check

The About page's **Check for Updates** button opens the Microsoft Store's Updates and
downloads page for packaged copies. The Store manages update discovery and installation.
The Store handles any update or restart prompt.

For unpackaged development builds, `Services/UpdateService.cs` checks GitHub Releases
when clicked and offers **View Release** for a newer tag. There is no automatic update
network check in the app.

## Layout version (separate from `<Version>`)

`DriveManager.CurrentLayoutVersion` (currently `4`) is independent of the app `<Version>`. Bump
it **only** when the on-disk folder/file naming scheme changes; doing so forces a one-time clean
rebuild of the placeholder tree (old index metadata is carried forward first to avoid
re-enriching the whole library over the network).

## Local WSL build and install

Use `npm run build:desktop`, `npm run verify:desktop`, `npm run install:desktop`,
and `npm run package:desktop` from WSL. See [the common desktop workflow](windows-development.md).
Direct PowerShell packaging commands above describe release internals, not the
local development entry point. Never uninstall to bypass a same-version error.
