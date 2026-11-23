# Microsoft Store listing

`listings.json` is the source of every field a user can read on the ImmichDrive Store listing (LIST-1): title, short and full description, features, keywords and screenshot captions, in the shared `schemaVersion: 1` / `locales` shape. Partner Center is an output; change the wording here first. Field names are semantic, not Partner Center import IDs. Release notes are not in this file: they come from `release-notes/<version>.md` (REL-6).

The file holds all nine identities (LIST-3, LIST-7, LOC-11): full records for `en-US` and the six other wording catalogs, and `en-CA` and `fr-CA` as `{"copyFrom": ...}` of `en-US` and `fr-FR`. The six translations were drafted on 2026-11-17 from the `en-US` text, using the interface catalogs' terms (see [docs/localization.md](../../docs/localization.md)); they are AI-drafted and not yet reviewed by a native speaker (LOC-12). The first two screenshot captions were rewritten on 2026-11-18 and translated the same way, also unreviewed. The hero's `heroHeadline` and `heroSubline` (2026-11-23) translate the maintainer's approved English the same way. `check:listing` holds the full records to the app's wording catalogs.

`npm run check:listing` (winui-app-kit's shared listing check, configured by the `listing` block in `windows-build.json`) validates the file and the current version's release notes, and `npm run validate` runs it.

## Where this text came from

It starts from the live listing as published on 2026-11-16, read from the public Store product data, with these corrections:

- "Drive for Immich" becomes "ImmichDrive" everywhere (D-012, BRAND-1). The listing title already shows "ImmichDrive"; the full description still said "Drive for Immich".
- The live description and one feature say the app follows the Windows display language and offers nine languages in Settings. The app is English only and has no language picker, so that paragraph and feature are removed. They looked written for a localization change that never shipped.
- The live requirements say Windows 11. The app also runs on Windows 10 22H2 (build 19045), so the requirements now say so.
- "Screenshots show the English interface." is dropped, since the app has only one language.
- One sentence is added: the API key is stored encrypted for the Windows account. That is true from the release that ships the protected key store; do not publish this text with an older build.

A listing-only Store submission made on 2026-11-16 (submission 1152921505702422473) published this text for `en-US` and `en-CA`. The Store's seven translated listings had only the name corrected in that submission. The translated text in this file reaches the Store at the next submission.

## Limits

| Field | Limit | Current |
| --- | --- | --- |
| `title` | Must be the reserved product name | "ImmichDrive" |
| `shortDescription` | 270 characters | 150 |
| `fullDescription` | 4,000 characters | 1,365 |
| `features` | Up to 20, each up to 200 characters | 12, longest 77 |
| `keywords` | Up to 7, each up to 40 characters, 21 words combined | 7, longest 21 characters, 21 words (at the limit) |
| `screenshotCaptions` | One per screenshot, each up to 200 characters | 6, longest 88 |
| Release notes ("What's new") | 1,500 characters | From `release-notes/` |

## Claims to keep true

- **Name:** "ImmichDrive", one word, in every field (D-012).
- **Tagline:** the first sentence of `shortDescription` is the README header tagline (LIST-12). Change both together.
- **Requirements:** Windows 10 22H2 with updates from October 2024 or later (build 19045.5011, the manifest's `MinVersion`) or Windows 11 (D-018, PLAT-6). Earlier Windows 10 builds lack the CET support .NET 10 needs; see docs/development.md, "Compatibility verification". An Immich server and an API key are needed and not included.
- **Language:** the interface is in nine languages and follows the Windows display language unless the user picks one in Settings; the drive's folder names stay English, and every locale's listing says so by naming the folders as they appear.
- **Read-only mirror:** only `Upload` is writable; the rest of the drive cannot change or delete server content.
- **On demand:** files download only when opened; thumbnails never download the original.
- **Privacy:** the library stays on the user's server; the API key is stored encrypted for the Windows account and never written to the settings file or logs; no telemetry or crash reporting. Network requests go to the configured Immich server, the public "Our other apps" catalog, and, in development builds only, GitHub's release check.
- **Independence:** an independent third-party client, not affiliated with or endorsed by the Immich project.

## Console-only fields

| Field | Value |
| --- | --- |
| Category | Photo & video |
| Price | US $0.99 base price, submitted as `Tier1012` by `store-publish.yml`; regional prices follow the tier |
| Privacy policy URL | https://www.dynamic-solutions.ca/privacy-policy |
| Support | contact@dynamic-solutions.ca and https://dynamic-solutions.ca |
| Website | https://dynamic-solutions.ca |
| Age rating | Set in Partner Center; not readable from the public data |

## Open questions before the next submission

- **Screenshots:** the first is a generated hero in each locale (D-123): the headline and subline from `heroHeadline` and `heroSubline` on the brand card; its caption is the subline, and it claims only what the listing says (photos in File Explorer, thumbnails, download on open, your own server). The other five were recaptured on 2026-11-18 with synthetic data ([store-assets/README.md](../README.md)). The Store workflow uploads all six with their captions in every locale (step 4 there). The README set in `store-assets/web/screenshots/` was recaptured in the same session (LIST-8).
- **Publishing:** nothing yet pushes this file to Partner Center. The corrected text goes in by hand, or with a listing-sync step added to `store-publish.yml` later (LIST-6).
