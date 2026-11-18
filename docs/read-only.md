> **Scope:** How the drive stays read-only and how the writable Upload folder works.
> **Governs:** `ImmichDrive/Services/{DriveSecurity,UploadService}.cs`

# Read-only drive + Upload folder

The drive mirrors Immich one-way (Immich → PC). To stop the user from corrupting that mirror, it's
**read-only** in two layers; the one writable spot is the **Upload** folder, which pushes files to
Immich.

## Read-only: the running provider refuses deletes and renames

While ImmichDrive runs, `CloudProviderService` registers the Cloud Files callbacks
`CF_CALLBACK_TYPE_NOTIFY_DELETE` and `CF_CALLBACK_TYPE_NOTIFY_RENAME`. For both, Microsoft documents
that "the user application that performs the deletion [rename/move] is blocked. A response is
expected from the sync provider"
([CF_CALLBACK_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/cfapi/ne-cfapi-cf_callback_type)).
The provider answers with `CfExecute` `ACK_DELETE` / `ACK_RENAME`:

- **Allowed:** requests from ImmichDrive's own process (`CF_PROCESS_INFO.ProcessId`, available because
  the root connects with `CF_CONNECT_FLAG_REQUIRE_PROCESS_INFO`), which covers prune and folder
  retirement; and deletes or moves that stay inside `Upload\`.
- **Refused** (`STATUS_ACCESS_DENIED`): every other delete, rename or move of a placeholder file or
  folder, including moves out of the drive.
- Every request gets an answer, even after an internal error (then a refusal), because an
  unanswered one holds the user's app until the 60-second callback timeout.

When ImmichDrive isn't running there is no provider to ask, and Windows does not refuse on its own:
measured on a test VM (2026-11-17), with the provider stopped a delete, rename or new file succeeded
and only opening a placeholder failed ("The cloud file provider is not running"). Local changes
never reach the server either way; the next sync re-creates any placeholder that was removed.

## Read-only: the folder ACE stops new files and edits

`DriveSecurity.EnsureReadOnly(syncRoot)` installs an inheritable deny ACE equivalent to
`(OI)(CI)(WD,WEA)` for the current user: WriteData/AddFile and WriteExtendedAttributes. That
**blocks new files and edits to file contents**, which no Cloud Files callback reports.
WriteAttributes stays allowed because Explorer uses it to set cloud-file pin attributes.

**Delete is deliberately not denied.** Earlier versions also denied Delete and DeleteChild. That
ACE outlives the app: MSIX runs no code at uninstall, and Windows' own unregistration of the sync
root could then not remove the placeholders, so uninstalling left a folder the user could not
delete. With delete allowed, uninstall leaves at most empty folders, deletable normally (measured
on a test VM, 2026-11-17). `EnsureReadOnly` replaces the older ACE once, the next time an existing
drive connects, and logs it.

**Why the provider still works:** cfapi placeholder ops (`CfCreatePlaceholders`) and hydration
(`CfExecute`) are performed by the `cldflt` filter and **bypass the user's deny ACE**. This is the
same way OneDrive's read-only folders behave. The provider's own prune and folder deletes are plain
`File.Delete`/`Directory.Delete`; with delete no longer denied they need no ACL override, and the
callback allows them because they come from ImmichDrive's process.

Cloud folder badge updates convert ordinary populated directories to in-sync placeholders, then
call `CfSetInSyncState` on later passes. These operations use `WRITE_DAC`, which is not denied by
the content-write ACE. They do not grant permission to add or edit files.

**Deliberate gap:** we do NOT deny `AD` (AddSubdirectory): the provider creates
month/album/partner folders with a normal `CreateDirectory` (cfapi only bypasses for *file*
placeholders, not folders). The only leak is the user can make an empty folder, but never put a
file in it (WD denied).

Checked on connect on a background thread. The correct existing ACE is left alone, avoiding a
slow remove-and-add operation over large libraries. If an older ACE still denies WriteAttributes or Delete,
`EnsureReadOnly` replaces just the app's old ACE in one security-descriptor update, preserving
unrelated rules. An `icacls` timeout in cleanup terminates the process before returning, so it
cannot continue changing permissions after the next step. The drive is always read-only (no
setting). `RemoveReadOnly` remains for disconnect and layout-migration cleanup.

**Connect ordering:** on connect, before the first sync starts (awaited off the UI thread), `SetFolderIcon` writes the root `desktop.ini` (or leaves it alone if the stable icon path is already set), then `EnsureReadOnly` applies the ACE. The icon must come first: the ACE denies creating files, so on a new drive `desktop.ini` could not be written afterwards. A drive from an older version has its delete-blocking ACE replaced here, before the sync's prune deletes anything. Checking a correct ACE is quick. Then, in the background, `EnsureUploadWritable` gives `Upload\` its own writable ACL. `desktop.ini` remains Hidden and
System; excluding it from the ACL is unnecessary. The root ReadOnly *attribute* makes Explorer read
the icon file and does not change the ACL.

## Upload folder

`DriveSecurity.EnsureUploadWritable` creates `Upload\` and runs `icacls /inheritance:r` + `/grant …F`
so it escapes the inherited deny. `UploadService` (a `FileSystemWatcher`) watches it: when a dropped
file becomes readable-exclusive (finished copying), it `POST /api/assets` (multipart: `assetData`,
`deviceAssetId`, `deviceId`, `fileCreatedAt`, `fileModifiedAt`) and **deletes the local file** on
success. The asset then reappears in its date/album folders on the next sync, which is its "final destination".
Failed uploads are left in Upload and retried on next app start.

`UploadService` also owns the folder's Explorer sync badge through `CloudFolderState`. It
converts an ordinary Upload directory to a cloud folder in place, preserving its writable ACL.
Startup and upload passes check the actual files recursively: an empty folder is in sync;
unfinished copies, failed uploads, and files whose local deletion failed remain pending.
File and directory changes (including deletions) invalidate the cached badge, and failed
Cloud Files state updates are retried on later ticks. Empty subfolders do not count as uploads.
