# Localization

ImmichDrive's interface is in the family's nine languages (LOC-1): English (United States and Canada), Spanish, French (France and Canada), German, Simplified Chinese, Brazilian Portuguese and Japanese. It follows Windows' language list unless the user picks a language in Settings (LOC-4).

## How it works

- Text lives in JSON catalogs in `ImmichDrive/Localization/Catalogs/`, one per wording catalog, embedded in the app and read through winui-app-kit's `UiText` (LOC-6). Keys are dotted and area-first (`connection.test-ok`), placeholders are `{{name}}` (LOC-7).
- `en-CA` reads the `en-US` catalog and `fr-CA` reads `fr-FR` (LOC-5), through `Classes/AppLanguages.cs`. Keep that list, the catalog files and the manifest's `<Resource Language>` entries in step with `external/winui-app-kit/build/languages.json`.
- XAML labels use `l10n:Localize` and relabel themselves when the language changes. Text built in code relabels on `UiText.Changed`. Status and error text that services produce is stored as a `LocalizedMessage`, never as finished text, so it follows a language change too.
- Shared labels (Settings, About, Cancel, Open logs folder and the rest) are the kit's `kit.*` keys, translated in the kit. Never define a `kit.` key in the app's catalogs.
- The package manifest's description is `ms-resource:Resources/AppDescription`, written for every language from these catalogs by winui-app-kit's packaging script (`manifest_strings` in `windows-build.json`). It is the one key without dots, because the manifest's resource syntax allows only word characters; the About and Home pages use it too.
- Numbers and times follow the Windows region, not the interface language (LOC-4), through `DriveStatusText.Count`, `ShortTime` and `ShortDateTime`.
- `npm run check:l10n` (in `validate`) fails on a missing or extra key, a placeholder mismatch, an em dash, a key the code uses that no catalog defines, a catalog key nothing uses, or a manifest language list that differs from the family's (LOC-9). Number and time formats go through `DriveStatusText`'s helpers, so every status line formats them the same way.

## Review status (LOC-12)

| Catalog | Status |
| --- | --- |
| en-US | Source text |
| es-ES | AI-drafted, not reviewed by a native speaker |
| fr-FR | AI-drafted, not reviewed by a native speaker |
| de-DE | AI-drafted, not reviewed by a native speaker |
| zh-CN | AI-drafted, not reviewed by a native speaker |
| pt-BR | AI-drafted, not reviewed by a native speaker |
| ja-JP | AI-drafted, not reviewed by a native speaker |

The Store listing (`store-assets/microsoft/listings.json`) and each version's release notes (`release-notes/<version>.<catalog>.md`) carry the same six languages, translated 2026-11-17 (the first two screenshot captions on 2026-11-18) and likewise unreviewed. A review covers the catalog, its listing record and the notes together.

Update a row when a native speaker reviews that catalog.

### Terms to translate consistently

- **File Explorer** uses Windows' own name in each language: Explorador de archivos, Explorateur de fichiers, Datei-Explorer, 文件资源管理器, Explorador de Arquivos, エクスプローラー.
- **Drive** (the folder ImmichDrive presents) is unidad, lecteur, Laufwerk, 驱动器, unidade, ドライブ.
- **API key** is clave de API, clé d'API, API-Schlüssel, API 密钥, chave de API, API キー.
- **Pinned photos** (Windows' "Always keep on this device") are fotos ancladas, photos épinglées, angeheftete Fotos, 已固定的照片, fotos fixadas, ピン留めした写真.

## What stays English (LOC-13)

- **The product names** ImmichDrive and Immich.
- **The folders the drive creates:** Albums, Favorites, Partners, Upload and the month folders (`2026-06 June`). They are the on-disk layout, not interface text: renaming them would rebuild every user's placeholder tree (`DriveManager.CurrentLayoutVersion`), and Windows, scripts and the thumbnail provider address them by name. Interface text that refers to them uses the English folder name inside the translated sentence.
- **Immich's own names:** permission identifiers (`user.read`, `asset.read` and the rest), and the menu path "Account Settings → API Keys", which is quoted as Immich labels it in English.
- **Content from the server:** album names, the user's name and email, and error details that come from Windows, .NET or the server, shown inside a translated sentence ("Something went wrong: {{reason}}").
- **WinUI's own control text** (a text box's Cut, Copy, Paste and Select All menu, and similar built-in labels) follows the Windows display language, not the language picked in Settings. This is winui-app-kit's decision (LOC-13): setting the process-wide language override would make "Use system setting" keep the last language chosen.
- **Logs and diagnostics:** the log files and the thumbnail provider's diagnostic log.
- **Legal text:** the licence and the third-party notices.
