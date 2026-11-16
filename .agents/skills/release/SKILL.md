---
name: release
description: Cut an ImmichDrive release: bump the version, write the notes, tag, and push so CI creates the GitHub release and submits to the Microsoft Store. Use only when the user asks to release. Do not use for local dev builds or installs.
---

# Cut a release

Goal: a tagged version whose GitHub release (notes only) and Store submission both succeed.

1. Confirm the compatibility checks in [docs/development.md](../../../docs/development.md#compatibility-verification) are done for this release, on Windows 11 and on Windows 10 22H2. If they are not, stop and ask the user.
2. Bump `<Version>` in `Directory.Build.props`, the single source of truth (semver, three parts). Never reuse a version: MSIX refuses the same version with different content. Bump `DriveManager.CurrentLayoutVersion` only if the on-disk folder or file naming changed.
3. Write `release-notes/X.Y.Z.txt`: user-facing notes, at most 1,500 characters. The Store workflow publishes them as the listing's "What's new", and the GitHub release uses the same text.
4. Run the `audit-docs` skill.
5. Commit `Bump version to vX.Y.Z`, tag `git tag -a vX.Y.Z -m "vX.Y.Z: <summary>"`, and push `git push origin main vX.Y.Z`.
6. Watch both tag workflows:
   - `build-msix.yml` builds x64 and ARM64 as a packaging check and creates the GitHub release with notes only. Never attach a package or upload an Actions artifact: this is a paid app in a public repo, and an unpackaged build could not register the sync root or the thumbnail handler anyway.
   - `store-publish.yml` submits the unsigned x64 and ARM64 packages to Partner Center with `Tier1012` (US $0.99 base price). After it passes, check the ingested regional prices. Details are in [docs/packaging.md](../../../docs/packaging.md); `build-msix.ps1 -NoSign` remains the manual upload path.

Report: the version and tag, both workflow results with links, the Store submission state, and anything the user must still check.
