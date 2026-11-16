# Upload folder status harness

Checks how `UploadService` reports the `Upload` folder as pending or complete to Windows. It links the production `UploadService` and stubs `ImmichClient`, `CloudFolderState` and NLog in `Program.cs`.

Covers: an empty folder, failed, thrown and in-flight uploads, files deleted from outside, a file still being copied, empty and nested folders, a failed Cloud Files status update that retries, a folder that cannot be listed, and the real file watcher seeing drops and removals.

Does not prove: that uploads reach an Immich server, or that Explorer shows the folder's sync status; both need the installed app.

Run with `npm test`, or alone with `scripts/dotnet run --project tests/ImmichDrive.UploadStatus.Tests`. The watcher cases wait a few seconds each.
