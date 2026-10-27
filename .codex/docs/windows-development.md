# Windows desktop workflow

From the WSL repository root, use the same commands across all four apps:

| Command | Result |
| --- | --- |
| `npm run build:desktop` | Signed ARM64 Release dev package |
| `npm run verify:desktop` | Full Windows Debug compile |
| `npm run install:desktop` | Build, update in place, launch |
| `npm run install:desktop -- --skip-build` | Reinstall the latest successful dev package |
| `npm run install:desktop -- --skip-build --dry-run` | Check the install without changing Windows |
| `npm run package:desktop` | Unsigned Store package |

Pass `-- --platform x64` for x64 or `-- --no-launch` when installing without
launching. The working tree is staged in a unique C: Windows TEMP directory,
cleaned after use. Logs and packages return to `.artifacts/windows-build/` in WSL.
Tracked release versions stay unchanged; dev revisions change only in staging.
Never uninstall to bypass an update error or silently replace a publisher.

The policy and implementation are shared, not copied per project. Read the
[shared build/install guide](../../../windows-build-tools/README.md), or open
`$WINDOWS_BUILD_TOOLS/README.md` when using a custom tools location. The launcher is
`scripts/windows-build` and the per-project manifest is `windows-build.json`.

`.codex/commands/build.md`, `install.md`, and `rebuild.md` are Codex task guides,
not automatically registered slash commands. Follow existing release workflows
for publishing, and leave changes uncommitted unless explicitly requested.

The repository skill `$desktop` supports the same build, verify and install
requests. Start a new Codex task to discover newly added repository skills.

## .NET security build floor

`global.json` requires stable SDK 10.0.403 or later within .NET 10, and both packaging workflows install its baseline. Local builds can use later installed 10.0 SDKs. Self-contained builds for the app and thumbnail provider reject selected .NET runtime packs below 10.0.14 through `Directory.Build.targets`, including older explicit overrides, without pinning future builds to that runtime patch. See the [HTTP2Shell assessment](http2shell.md) for the verified older package provenance and the remaining Windows package checks.

Run `pwsh -NoProfile -File .github/scripts/tests/runtime-build-floor.Tests.ps1` for build-floor regression checks. These resolve framework references for both real projects and architectures without restore or compilation. They check the SDK default, reject 10.0.13, and permit 10.0.14 and a synthetic later runtime version. They do not prove package contents or Windows behavior. The packaging-check workflow runs them before building the MSIX.

## Test over an existing Store installation

Use `npm run install:desktop -- --store-identity` only when explicitly replacing the
Store copy for local testing. The bridge keeps the Store name and publisher, uses the
matching shared signing certificate, and allocates a staging-only version higher than
the installed Store family. It updates in place without uninstalling, preserving settings
and package data. Store updates resume when a higher version is published. Default dev
installs continue to use the separate `ImmichDrive` family. This never submits to the Store.
The test build is a signed `.msixbundle`, matching the Store distribution format. Windows
rejects a standalone MSIX update over an installed bundle with `0x80073CF3`.
