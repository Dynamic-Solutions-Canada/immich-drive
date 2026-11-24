> **Scope:** Which Immich API key permissions ImmichDrive uses, for users creating a key. Linked from the README's Setup section.
> **Governs:** Nothing in code; keep it in step with the Connection page's permission list. The endpoint detail is in [immich-api.md](immich-api.md).

# API key permissions

You do **not** need **All permissions**. To browse your photos with thumbnails and open or attach originals, select these four:

| Permission | What it enables |
| --- | --- |
| `user.read` | Test connection, connecting the drive, and reconnecting after the server comes back. |
| `asset.read` | List your photos and favorites, and read file names and sizes. |
| `asset.view` | Display thumbnails in Explorer without downloading originals. |
| `asset.download` | Open or attach original photos and videos. |

Add these for the features you use:

| Permission | What it enables |
| --- | --- |
| `album.read` | Mirror your Albums folders. |
| `partner.read` | Mirror libraries shared with you in Partners folders. |
| `asset.upload` | Upload files dropped into the Upload folder. |

Select all **seven** to use every feature. Delete, update, API key management and admin permissions are not needed. Without `asset.upload`, dropped files stay in Upload because the server rejects them.

**If Test connection rejects your key:** check that it includes `user.read`. A key with only asset and album permissions cannot pass the connection check. A successful test checks the URL and `user.read`, not the permissions for thumbnails, downloads or other features.

These permissions match [Immich's endpoint declarations](https://github.com/immich-app/immich/tree/main/server/src/controllers).
