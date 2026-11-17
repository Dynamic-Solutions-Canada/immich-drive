> **Read when:** adding or changing any window, page, flyout, card, or other interface element.
> **Governs:** `ImmichDrive/**/*.xaml`, `ImmichDrive/**/*.xaml.cs`, `ImmichDrive/Windows/**`, `ImmichDrive/Pages/**`

# UI conventions

This app's own interface conventions. The shared family rules are in the standards' [ui.md](https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/standards/ui.md); this file records only what is specific to this app.

- Settings UI is WinUI 3 (`Microsoft.WindowsAppSDK`). `SettingsWindow` hosts winui-app-kit's
  `SettingsShell` (UI-1, UI-2): the kit owns the title bar, Mica, the window icon, placement, the
  built-in Settings item and the footer ("Our other apps", then "About"). The window adds only its
  own items, **Home** and **Connection**, in `BuildMenu`, and maps each tag to a page in `PageFor`.
  Open a page from code with `SettingsWindow.GetCurrent()?.Select(tag)`, which is safe before the
  window has loaded. About is the kit's `AboutPage`, given `AboutContent.Create()`; product cards
  go in its `ExtraCards`, after the standard ones.
- Pages are `Page` objects (not `UserControl`), shaped as `ScrollViewer` then
  `StackPanel Style="{StaticResource PageContentStyle}"`, a `PageTitleStyle` title, and content in
  `Border Style="{StaticResource CardBorderStyle}"` cards. Section headings use
  `FirstSectionHeaderStyle` and `SectionHeaderStyle`. Never write padding, corner radius or header
  sizes inline (UI-3); the styles come from the kit's `Styles.xaml`, merged in `App.xaml`.
- Buttons with a glyph use `StackPanel Style="{StaticResource IconLabelStyle}"` with a
  `FontIcon Style="{StaticResource ButtonGlyphStyle}"` then the label. Such a button gets no
  screen-reader name from its content, so give it one: `l10n:Localize.Name` with the label's key, or
  `AutomationProperties.SetName` in code when the label changes (UI-14). Secondary text uses
  `Foreground="{ThemeResource TextFillColorSecondaryBrush}"`, not `Opacity`. Text built in code uses
  `SecondaryText.Create` or `SecondaryText.Caption`, never a brush looked up in
  `Application.Current.Resources`, which ignores the window's theme.
- **SettingsPage** is the kit's shared rows (UI-4): `StartupSetting` (Windows' real startup state,
  TRAY-10), `LanguageSetting` and `ThemeSetting`, which apply at once and raise an event the page saves, and
  `ResetSetting` (CONF-4) in its own card, which runs `SettingsManager.ResetSettings` and names what it keeps in `KeptNote`. The kit's
  "settings were reset" notice goes in its `NoticeHost` (CONF-2).
- Use built-in styles for text: `SubtitleTextBlockStyle`, `CaptionTextBlockStyle`,
  `BodyStrongTextBlockStyle`, `AccentButtonStyle`. Icons via `FontIcon Glyph="&#xExxx;"` (Segoe
  Fluent Icons), written as `\uExxx` in C#; a decorative icon beside its label is hidden from screen
  readers, and an icon-only control has an `AutomationProperties.Name` (UI-14).
- **Code identifiers** on ConnectionPage use shared page resources: `CodeTextBlockStyle`
  (Consolas, 13px) and `CodeBlockBorderStyle` (theme-aware secondary fill, 4px corners).
  Required API permissions appear one per line in a code block; optional permissions use
  code badges beside wrapping feature descriptions. Keep prose in the normal UI font.
- **In-app images** (the shell's title-bar icon, the flyout, About) use to the high-res PNG
  (`App.IconImagePath` → `Resources/ImmichDrive.png`), **not** the multi-frame `.ico`, so they
  downscale crisply to 16/22/32px. The `.ico` (`App.IconPath`) is used only for the OS window /
  taskbar / alt-tab icon, applied with the kit's `WindowIcon.Apply`, and for the tray artwork.
- **Tray status flyout** (`Windows/StatusFlyout.xaml`) only builds content. The kit's
  `FlyoutController` owns the window: placement by the taskbar, desktop acrylic, rounded corners on
  Windows 11 (square on Windows 10), dismissal on focus loss or Esc, and parking it off-screen so it
  reopens instantly. Never hide or close it yourself; call its `Dismiss`. The host creates it once.
  It refreshes only while open, and calls `UpdateSize` only when the measured content size changes,
  because `UpdateUi` runs once per synced asset.
- **Theme:** `AppTheme.Track(window)` for every window; the `AppTheme` setting's side-effect calls
  the kit's `AppTheme.Set`, which every tracked window follows.
- **Settings window placement** is restored by `WindowChrome.SetUpSettingsWindow` and captured with
  `WindowChrome.CapturePlacement` into `SettingsWindowPlacement` when the window closes.
- **Status colours** come from a small local `<Grid.Resources>` palette of `SolidColorBrush`es over
  `{ThemeResource}` **Color** keys (`SystemFillColorSuccess`, `SystemFillColorCaution`,
  `SystemFillColorCritical`, `TextFillColorSecondary`, `TextFillColorDisabled`), looked up by key in
  code-behind. Note the `…Brush` variants exist for all of these but the bare **Color** key does not
  (`SystemFillColorAttention` is brush-only); check `generic.xaml` before using one.
- Bindable settings are `[ObservableProperty]` partial properties on `UserSettings`, read/written
  through `SettingsManager.Current`. These pages drive them from **code-behind event handlers**
  (e.g. the kit rows' `ChoiceChanged`) rather than `{x:Bind … Mode=TwoWay}`;
  if you do bind a control directly, bind `TwoWay` to `SettingsManager.Current` and guard side-effects
  with the `_initializing` flag.
- In page code-behind, fully-qualify `Microsoft.UI.Xaml.Visibility` and `FocusState` (they
  collide with WinRT `Windows.UI.Xaml.*`). `global::` does not parse inside interpolated
  strings; assign to a local first.
- No user-facing literal in code or XAML (LOC-6): text comes from `Localization/Catalogs/*.json`
  through `UiText` in code and `l10n:Localize` in XAML, with dotted keys. Text built in code
  relabels on `UiText.Changed`; status text from services is a `LocalizedMessage`. Shared labels are
  the kit's `kit.*` keys; never define one in the app's catalog. See
  [docs/localization.md](../../docs/localization.md), including what stays English.
