---
name: desktop
description: Build, verify, or install this repository's Windows desktop app from WSL using the shared disposable Windows build bridge. Use for local desktop development, not releases or Store publishing.
---

# Windows desktop development

Read [.codex/docs/windows-development.md](../../../.codex/docs/windows-development.md)
and the shared helper guide it links. Select the root npm command matching the
request: `build:desktop`, `verify:desktop`, `install:desktop`, or `package:desktop`.
When asked to rebuild and try a change, use `npm run install:desktop`.

The bridge owns staging, dev version allocation, shared signing, in-place update,
launch and cleanup. Do not recreate these steps manually or edit tracked versions
for a dev build. Never uninstall or change publisher to work around an error.

Report the resulting version and available runtime evidence. Compilation and
install dry-runs do not establish that the app runs correctly. If installation
was requested, verify the actual installed version and running package where
possible. Keep commits and publishing outside this local development workflow.
