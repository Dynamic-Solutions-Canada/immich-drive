---
name: add-setting
description: Add a persisted user setting to ImmichDrive, with its default, side-effect and UI control. Use when a new option must survive restarts. Do not use for credentials, which never go in settings.
---

# Add a user setting

Goal: a new setting that persists to `settings.json`, applies its side-effect safely, and appears in the UI if it is user-visible.

1. Read [.agents/guides/user-settings.md](../../guides/user-settings.md). If the value is a credential (key, token, password), stop: it belongs in `ProtectedStore`, as the guide's "Credentials" section explains.
2. In `ImmichDrive/ViewModels/UserSettings.cs`, add an `[ObservableProperty]` partial property in PascalCase with a sensible default. It serialises automatically.
3. If changing it needs a side-effect, add a partial `On<Name>Changed(value)` that starts with `if (_initializing) return;`, so loading the file does not trigger it.
4. If it is runtime-only or computed, mark it `[JsonIgnore]`.
5. If it is user-visible, add the control on the relevant page following [.agents/guides/ui-conventions.md](../../guides/ui-conventions.md), and call `SettingsManager.SaveSettings()` after the user changes it.
6. Add a row to the "Current settings" table in the settings guide.
7. Run `npm run validate` and `npm run verify:desktop`.

Report: the property, its default, any side-effect, where it appears in the UI, and the compile result.
