---
name: add-settings-page
description: Add a page to the ImmichDrive settings window and its navigation. Use when a new group of settings or information needs its own page. Do not use for adding a single setting to an existing page.
---

# Add a settings page

Goal: a new page reachable from the settings window's navigation, shaped like its siblings.

1. Read [.agents/guides/ui-conventions.md](../../guides/ui-conventions.md) and open an existing page in `ImmichDrive/Pages/` to copy its shape.
2. Create `ImmichDrive/Pages/<Name>Page.xaml` and `.xaml.cs` as a WinUI 3 `Page`, following the guide's page and card layout.
3. In `ImmichDrive/SettingsWindow.xaml`, add a `NavigationViewItem` with `Tag="<Name>Page"` and an icon (`SymbolIcon` or a `FontIcon` glyph).
4. In `ImmichDrive/SettingsWindow.xaml.cs`, add the `Tag` to `typeof(<Name>Page)` case in `GetPageTypeFromTag`.
5. In the page's code-behind, fully qualify `Microsoft.UI.Xaml.Visibility` and `FocusState` if used.
6. Run `npm run validate` and `npm run verify:desktop`.

Report: the page, its navigation entry, and the compile result. Say that the page has not been seen running unless it was installed and checked.
