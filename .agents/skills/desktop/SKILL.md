---
name: desktop
description: Build, verify, or install ImmichDrive's Windows desktop app from WSL through the shared disposable Windows build bridge. Use for local desktop development, not releases or Store publishing.
---

# Windows desktop development

Goal: the requested build, compile check or install, run through the shared bridge with evidence of the result.

1. Read the [shared Windows build tools](https://github.com/Dynamic-Solutions-Canada/windows-build-tools) guide for how the bridge stages, signs and installs, and "Build and install" in [docs/development.md](../../../docs/development.md) for this app's specifics.
2. Pick the root npm command that matches the request: `verify:desktop` to compile, `package:desktop:dev` for a signed dev package, `package:desktop` for an unsigned Store-identity package, `install:desktop` to build, update in place and launch. "Rebuild and try it" means `install:desktop`.
3. `verify:desktop` compiles only the main app. When the thumbnail provider or a file linked into it changed, also run `package:desktop`, which compiles both.
4. Let the bridge own staging, dev version allocation, shared signing, in-place update, launch and cleanup. Do not recreate these steps by hand or edit tracked versions for a dev build. Never uninstall or change the publisher to work around an error.

Report: the resulting version and the runtime evidence available. A compile or install dry run does not show the app works. If an install was requested, verify the installed version and the running package where possible.
