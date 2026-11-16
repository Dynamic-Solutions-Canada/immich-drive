> **Read when:** adding or changing a native declaration or a cfapi struct.
> **Governs:** `ImmichDrive/Classes/CloudFilter/CfApi.cs`, `ImmichDrive/Services/DriveSecurity.cs`, `ImmichDrive.ThumbnailProvider/**`

# P/Invoke conventions

- The tray icon and menu, window icons, window placement, the flyout window and single instance
  come from winui-app-kit, which holds their Win32 declarations; this app declares none of its own
  for them. Do not add tray or window P/Invoke here; extend the kit instead.
- Cloud Files API P/Invoke lives in `ImmichDrive/Classes/CloudFilter/CfApi.cs`. A one-off `SHChangeNotify`
  declaration lives in `ImmichDrive/Services/DriveSecurity.cs`. The thumbnail extension keeps
  its shell-COM P/Invoke in `ImmichDrive.ThumbnailProvider/`.
- Prefer `[LibraryImport]` (source-generated, AOT/trim-safe) for new declarations; use
  `[DllImport]` only where a marshalling pattern isn't yet supported by the generator.
  `DriveSecurity.SHChangeNotify` is the current `[LibraryImport]` example. The bulk of `CfApi`
  is still `[DllImport]` (predates the convention); don't churn it.
- Group by DLL with a header comment. A class with any `[LibraryImport]` must be `partial`
  (`CfApi` and `DriveSecurity` already are).
- cfapi structs (`CF_PLACEHOLDER_CREATE_INFO`, `CF_CALLBACK_INFO`, `CF_OPERATION_INFO`, …)
  must match `cfapi.h` layout **exactly**: use `[StructLayout(LayoutKind.Sequential)]`, the
  right field order, and verify sizes on-device. A wrong offset silently corrupts hydration
  (a real crash: a wrong `CF_CALLBACK_INFO` field order made `FileIdentity` point at garbage).
- The `CF_CALLBACK_PARAMETERS` / `CF_OPERATION_PARAMETERS` arms are union members: in the
  modeled structs (`CF_CALLBACK_PARAMETERS_FETCHDATA`, `CF_OPERATION_PARAMETERS_TRANSFERDATA`)
  the `uint ParamSize` is followed by 4 bytes of explicit padding (`_pad0`) so the 8-byte-aligned
  union arm starts at offset 8. Omitting that pad was a real crash; keep it on any new arm.
- The tray icon is drawn from the app's `.ico` artwork (normal or muted) at the tray's exact pixel
  size with the kit's `TrayIconRasterizer`, and re-rendered on the kit's `RenderNeeded`; see
  `MainWindow.RenderTrayIcon`. Free unmanaged buffers in `finally` in any declaration added here.
- Never show a balloon or toast. The drive's status signalling is deliberately passive: icon +
  tooltip + flyout, no notification and no sound.
