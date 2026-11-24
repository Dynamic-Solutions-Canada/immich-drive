# Store assets

| Folder | Holds |
| --- | --- |
| `microsoft/listings.json`, `listings.md` | The Store listing text in every locale, and the claims it makes (see `listings.md`) |
| `manifest.json` | Every Store screenshot in listing order: its raw source, theme, crop, en-US caption and alt text (LIST-8) |
| `raw/microsoft/` | The raw captures: the test VM's whole desktop at 1:1, each with the `.json` sidecar the capture helper writes |
| `microsoft/screenshots/` | The listing screenshots, rendered from the raw captures; the generated first screenshot is in a folder per locale (`<locale>/00-hero-dark.png`) |
| `web/screenshots/` | The images the repository README shows |

## Screenshots

Captured from the real installed app with synthetic data, never from a personal machine (LIST-8). Every shot is in dark mode on the Windows 11 dark wallpaper (`img19.jpg`), at 1920 x 1080 and 100% scale. The captions in `manifest.json` are the en-US `screenshotCaptions` in `listings.json`, in the same order and count; each other locale has its own translation there. A new or changed screenshot is approved by the maintainer before it is committed.

The test VM has no Immich server, so the library comes from the repository's fake Immich server serving its sample library, and the app does the rest for real:

| Element | Source |
| --- | --- |
| The server (`https://photos.example.net`) | The fake Immich server with `--library` ([tests/fake-immich](../tests/fake-immich/README.md), "Sample library"), behind a session-only Caddy proxy with `tls internal`, so the Connection page shows a normal https address; the guest's hosts file points the name at the proxy, and Caddy's root certificate is trusted only inside the throwaway guest |
| The photos, the user "Alex Rivera" and the partner "Jordan Lee" | The sample library: 48 CC0 photos from Wikimedia Commons with no recognisable people, and synthetic people on `example.invalid` addresses |
| The drive folder (`C:\ImmichDrive`) | A synthetic `settings.json` holding only `SyncRootPath`, written before the first launch, so no Windows account name appears in a path |
| The API key | The fake's test key, shown masked |

Shot 00 is the generated hero (below). Shot 04 shows the tray flyout and uses the `tray` crop; the others use the default `window` crop. The settings window is 1100 x 700 and the Explorer windows 1200 x 700, centered. Shot 03 shows the `2026-08 August` folder at a 160-pixel icon size, set through the window's shell folder view; shot 05 shows the drive root in Details view. For shot 04 the settings window is closed and the tray icon is promoted out of the overflow (`IsPromoted` under `HKCU\Control Panel\NotifyIconSettings`, inside the throwaway guest).

1. **Capture** on the Windows 11 test VM (`windows-dev`) with the windows-build-tools app-test helpers, in a throwaway session: an uncommitted compose override adds `-snapshot` through the image's `ARGUMENTS` setting (check it is in QEMU's command line before Windows boots). Fetch the sample library and start the fake with `compose.sample.yml`, then the proxy on the VM's network. In the guest, add the hosts entry, trust the proxy's root, install the Release dev package, write the synthetic settings, run `screenshot-desktop.ps1 -Mode Prepare`, and connect from the Connection page. Capture each scene with `Save-DesktopCapture`, after a click on blank space clears any focus rectangle. Copy each `.png` and `.json` into `raw/microsoft/` under the manifest's names, run `app_test_clean`, stop the VM and recreate it from the base compose, and remove the fake and the proxy, so nothing persists.
2. **Render** with `npm run shots:render` (winui-app-kit's renderer; its README has the rules): each capture is cropped to 1366 x 768, with nothing composited, shaved or scaled, and written without an alpha channel to `microsoft/screenshots/` (ASSET-9). Recaptioning needs no recapture; a new look does.
3. **Check** with `npm run check:screenshots` (part of `npm run validate`): every capture has its sidecar and was taken dark, at 100% scale, with effects on and the dark wallpaper; every screenshot was written by the renderer at exactly 1366 x 768; the captions match the en-US `screenshotCaptions`; no stray file is present.
4. **Sync** with the next Store release: the Store workflow's `screenshots` input uploads every locale's set, its hero first, with that locale's captions from `listings.json`, and keeps the listing's logos. Check the draft in Partner Center before submitting it.

A UI change that shows in a screenshot means recapturing that shot.

## The first screenshot: a generated hero

Store search shows only the first screenshot, at about 320 x 180, so it is a generated hero (D-123): the split layout, a gold brand panel with the icon, name, headline and subline on the left, beside the real UI enlarged from `raw/microsoft/02-thumbnails-dark.png` on the right: File Explorer showing the drive with real thumbnails, since Explorer is where people use ImmichDrive. Each listing locale has its own, drawn from its `heroHeadline` and `heroSubline` in `listings.json`; en-CA and fr-CA use en-US's and fr-FR's. Its caption is the subline. `npm run hero` draws them (devkit, through winui-app-kit's `scripts/devkit`), and `npm run check:hero` in `validate` fails when one no longer matches its inputs. New or changed headlines are approved by the maintainer, like any screenshot.
