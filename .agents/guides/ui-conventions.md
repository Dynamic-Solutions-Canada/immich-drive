> **Read when:** adding or changing any window, page, flyout, card, or other interface element.
> **Governs:** `ImmichDrive/**/*.xaml`, `ImmichDrive/**/*.xaml.cs`, `ImmichDrive/Windows/**`, `ImmichDrive/Pages/**`

# UI conventions

This app's own interface conventions. The shared family rules are in the standards' [ui.md](https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/standards/ui.md); this file records only what is specific to this app.

- Settings UI is WinUI 3 (`Microsoft.WindowsAppSDK`). `SettingsWindow` uses `MicaBackdrop`,
  `ExtendsContentIntoTitleBar` + a custom 48px title-bar grid (with an `Image` icon), and a
  `NavigationView` + `Frame`. Page switching is a `Tag`→`typeof(Page)` `switch` in
  `GetPageTypeFromTag`, with no MVVM routing framework. Pages live under `Pages/`: **HomePage**,
  **ConnectionPage**, **AboutPage** (in the nav), plus **SettingsPage** (the built-in
  `NavigationView` settings cog, `IsSettingsVisible="True"`).
- Pages are `Page` objects (not `UserControl`), shaped as
  `ScrollViewer Padding="32,16,32,32"` → `StackPanel Spacing` `MaxWidth="~760"`, content
  grouped in card `Border`s (`CardBackgroundFillColorDefaultBrush` / `CardStrokeColorDefaultBrush`,
  `BorderThickness="1"`, `CornerRadius="8"`/`4`, padded ~`16`–`20`).
- **Per-setting cards** (SettingsPage) wrap each setting in a card `Border` holding a two-column
  `Grid` (`Width="*"` label/description column + `Width="Auto"` control column, `ColumnSpacing="16"`),
  so a wrapping description never pushes under the control. SettingsPage currently has just the
  **App theme** and **Start at sign-in** cards; AboutPage uses the same card/Grid shape, including
  a GitHub/Source-code link.
- Use built-in styles: `TitleTextBlockStyle`, `SubtitleTextBlockStyle`, `CaptionTextBlockStyle`,
  `BodyStrongTextBlockStyle`, `AccentButtonStyle`. Icons via `FontIcon Glyph="&#xExxx;"` (Segoe
  Fluent Icons) or `SymbolIcon`.
- **Code identifiers** on ConnectionPage use shared page resources: `CodeTextBlockStyle`
  (Consolas, 13px) and `CodeBlockBorderStyle` (theme-aware secondary fill, 4px corners).
  Required API permissions appear one per line in a code block; optional permissions use
  code badges beside wrapping feature descriptions. Keep prose in the normal UI font.
- **In-app images** (title-bar icon, flyout, About) set their `Source` to the high-res PNG
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
- **Settings window placement** is restored and captured with the kit's `WindowChrome`
  (`RestorePlacement`, `CapturePlacement`) into `SettingsWindowPlacement`.
- **Status colours** come from a small local `<Grid.Resources>` palette of `SolidColorBrush`es over
  `{ThemeResource}` **Color** keys (`SystemFillColorSuccess`, `SystemFillColorCaution`,
  `SystemFillColorCritical`, `TextFillColorSecondary`, `TextFillColorDisabled`), looked up by key in
  code-behind. Note the `…Brush` variants exist for all of these but the bare **Color** key does not
  (`SystemFillColorAttention` is brush-only); check `generic.xaml` before using one.
- Bindable settings are `[ObservableProperty]` partial properties on `UserSettings`, read/written
  through `SettingsManager.Current`. These pages drive them from **code-behind event handlers**
  (e.g. `ThemeCombo_SelectionChanged`, `StartupToggle_Toggled`) rather than `{x:Bind … Mode=TwoWay}`;
  if you do bind a control directly, bind `TwoWay` to `SettingsManager.Current` and guard side-effects
  with the `_initializing` flag.
- In page code-behind, fully-qualify `Microsoft.UI.Xaml.Visibility` and `FocusState` (they
  collide with WinRT `Windows.UI.Xaml.*`). `global::` does not parse inside interpolated
  strings; assign to a local first.
- Strings are inline for now: the app is English only. Localization will use winui-app-kit's
  catalogs (LOC-6 in the standards), not a per-app resource dictionary.
