---
name: release
description: Cut an ImmichDrive release: write and approve the notes, bump the version, tag, and push so CI creates the GitHub release and submits to the Microsoft Store, then run the post-release steps. Use only when the user asks to release. Do not use for local dev builds or installs.
---

# Cut a release

Goal: a tagged version whose notes-only GitHub release succeeds and whose Store draft is submitted, tested on Windows 11 and Windows 10 22H2 before it ships.

1. **Clean start.** Stop if the working tree has changes that are not part of this release, or if `HEAD` is not `origin/main` after `git fetch`.
2. **Gate.** Run `npm run validate`, `npm run verify:desktop` and `npm run package:desktop` (it compiles the thumbnail provider too). Stop on any failure.
3. **Tested on both Windows versions (TEST-9).** The release candidate must be installed and exercised on Windows 11, using the checks under "Compatibility verification" in [docs/development.md](../../../docs/development.md#compatibility-verification); install it with `npm run install:desktop -- --store-identity` to test the upgrade over a Store copy. For Windows 10 22H2, either the same run on a 22H2 machine or VM, or a maintainer decision recorded in that section that names what it covers. If the user has confirmed neither, stop and ask; do not tag.
4. **Choose the version and write the notes.** Semver, three parts, never reused (MSIX refuses the same version with different content). Write `release-notes/X.Y.Z.md`: user-facing lines only, with no heading and no AI disclosure (CI adds both), at most 1,500 characters, because the Store publishes it as the listing's "What's new". Show the notes to the user and wait for approval before touching any version file.
5. **Bump and commit together (REL-2).** Set `<Version>` in `Directory.Build.props`, the single source of truth. Bump `DriveManager.CurrentLayoutVersion` only if the on-disk folder or file naming changed. Run the `audit-docs` skill. Commit the version file and the notes file in one commit with the subject `Bump version to vX.Y.Z`.
6. **Tag and push together.** `git tag -a vX.Y.Z -m "vX.Y.Z: <summary>"`, then `git push origin main vX.Y.Z`.
7. **Watch both tag workflows:**
   - `build-msix.yml` builds x64 and ARM64 as a packaging check and creates the GitHub release from the notes file. It fails if the notes are missing. Never attach a package or upload an Actions artifact: this is a paid app in a public repo.
   - `store-publish.yml` uploads the unsigned x64 and ARM64 packages to Partner Center as a **draft** with `Tier1012` (US $0.99 base price) and the notes as "What's new", and names the draft ID in its run summary. It does not publish.
8. **Publish the Store draft (REL-7).** Check the draft with the read-only `inspect-store-submission.yml` workflow (`gh workflow run inspect-store-submission.yml -f submission_id=<id>`): both architectures present, the expected upload, and the price tier. Tell the user what it shows; the ingested regional prices are visible only in Partner Center, so ask the user to check them there. Then run `store-publish.yml` manually on tag `vX.Y.Z` with `submit_draft` set to the draft ID and `no_commit` unchecked: `gh workflow run store-publish.yml --ref vX.Y.Z -f submit_draft=<id> -f no_commit=false`. Confirm it reaches `Certification`. Recovery options are in [docs/packaging.md](../../../docs/packaging.md).
9. **Post-release (REL-9).** Update the promo catalog entry and the website page if this release changes what they say. Bump the `external/` submodules only when kit or promo code changed.

Report: the version and tag, both workflow results with links, the Store submission state, the post-release steps done, and anything the user must still check.
