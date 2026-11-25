> **Read when:** writing or changing C# in `ImmichDrive/` or `ImmichDrive.ThumbnailProvider/`, especially startup, a file linked into the thumbnail provider, a native call, or anything that might duplicate a shared helper.
> **Governs:** `ImmichDrive/**/*.cs`, `ImmichDrive.ThumbnailProvider/**/*.cs`, `Directory.Build.props`

# Code conventions

The gotchas this app has hit and the helpers to use instead of writing a new copy. The kit-wide conventions are in winui-app-kit's README.

## Gotchas

- Files linked into the thumbnail provider stay WinUI-free and NLog-free; failures there are reported through hooks the app wires to NLog. Check its `.csproj` before adding a dependency to a shared file.
- Startup order matters: single instance, then the API key migration, then the settings load (`App.RestoreSettings`). The kit's settings store rewrites `settings.json` as it loads, so the key must be moved out first.
- Release builds treat warnings as errors (kit default). A kit switch such as `WinUIAppKitWarningsAsErrors`, if ever needed, goes before the kit's props import in `Directory.Build.props`, with a comment saying why.
- The sync root must be registered before callbacks connect, and the process must stay alive while the drive is online; that is why the app is resident.
- Packaged AppData is redirected per package. Both processes find the data folder through the breadcrumb `SharedPaths` writes to `C:\ProgramData\ImmichDrive`.
- cfapi structs must match `cfapi.h` exactly; a wrong offset silently corrupts hydration. See [pinvoke.md](pinvoke.md).
- PowerShell build scripts are ASCII only: Windows PowerShell 5.1 reads a BOM-less `.ps1` as ANSI.
- `global::` does not parse inside an interpolated string; assign to a local first.

## Shared helpers (do not duplicate)

- For tray, flyout, window, theme, startup, single-instance, update and settings-file plumbing, use the kit; never re-add an app copy.
- For replacing a file atomically, use the kit's `AtomicFile`.
- For any credential, use the store from `CredentialStore.Create`; for the API key specifically, `ApiKeyStore`.
- For a settings schema change, add a step to `SettingsMigrations`.
- For the data folder, settings path or index path, use `SharedPaths`.
- For the GitHub owner and repository, use `SourceRepository`.
