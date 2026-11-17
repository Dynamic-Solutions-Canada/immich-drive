using ImmichDrive.Classes.Settings;
using ImmichDrive.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.IO;
using WinUIAppKit.Localization;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ImmichDrive.Pages;

/// <summary>
/// The server URL, API key and drive folder, with Test, Connect and Disconnect. Results show in one
/// bar, kept as keys rather than finished text so a language change relabels the last result too.
/// </summary>
public sealed partial class ConnectionPage : Page
{
    /// <summary>The permission every API key needs, named in several messages; never translated.</summary>
    private const string UserReadPermission = "user.read";

    // The result on show, so it can be rendered again in a new language.
    private InfoBarSeverity _resultSeverity;
    private string? _resultTitleKey;
    private LocalizedMessage? _resultMessage;
    private bool _resultKeyNotSaved;

    public ConnectionPage()
    {
        InitializeComponent();
        Loaded += (_, _) => UiText.Changed += OnLanguageChanged;
        Unloaded += (_, _) => UiText.Changed -= OnLanguageChanged;
        ShowUserReadNote();
        var s = SettingsManager.Current;
        ServerBox.Text = s.ServerUrl;
        UpdateInsecureUrlWarning();
        ApiKeyBox.Password = ApiKeyStore.ApiKey;
        FolderBox.Text = s.EffectiveSyncRootPath;
        UpdateButtons();
        ShowUnreadableKeyNotice();
    }

    /// <summary>
    /// Explains an empty key box when a saved key exists but no longer decrypts (for example after
    /// restoring a profile on another machine), so the user knows to re-enter it.
    /// </summary>
    private void ShowUnreadableKeyNotice()
    {
        if (!ApiKeyStore.StoredKeyUnreadable)
        {
            return;
        }

        ShowResult(InfoBarSeverity.Warning, "connection.key-unreadable-title", new LocalizedMessage("connection.key-unreadable"));
    }

    /// <summary>Re-renders the text this page builds in code; XAML labels relabel themselves.</summary>
    private void OnLanguageChanged()
    {
        ShowUserReadNote();
        UpdateInsecureUrlWarning();
        RenderResult();
    }

    private void ServerBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateInsecureUrlWarning();

    /// <summary>
    /// Warns, without blocking, when the server address is plain http:// and not this PC (FIX-064):
    /// the API key and photos would then cross the network unencrypted. Many home servers only offer
    /// http:// on the local network, so the address is still accepted.
    /// </summary>
    private void UpdateInsecureUrlWarning()
    {
        bool insecure = IsUnencryptedRemote(ServerBox.Text);
        InsecureUrlBar.Title = UiText.Get("connection.http-warning-title");
        InsecureUrlBar.Message = UiText.Get("connection.http-warning");
        InsecureUrlBar.IsOpen = insecure;
    }

    /// <summary>
    /// True for an absolute http:// address whose host is not loopback (localhost, 127.0.0.0/8, ::1).
    /// Anything that does not parse is left to the connection test to report.
    /// </summary>
    private static bool IsUnencryptedRemote(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback;
    }

    /// <summary>The note that even a connection test needs the user.read permission.</summary>
    private void ShowUserReadNote() =>
        UserReadNote.Text = UiText.Format("connection.user-read-note", ("permission", UserReadPermission));

    /// <summary>
    /// Shows a result in the bar. The key-not-saved warning is cleared; <see cref="NoteKeyNotSaved"/>
    /// adds it after.
    /// </summary>
    /// <param name="severity">The bar's severity.</param>
    /// <param name="titleKey">Catalog key of the title.</param>
    /// <param name="message">The message, or null for none.</param>
    private void ShowResult(InfoBarSeverity severity, string titleKey, LocalizedMessage? message)
    {
        _resultSeverity = severity;
        _resultTitleKey = titleKey;
        _resultMessage = message;
        _resultKeyNotSaved = false;
        RenderResult();
    }

    /// <summary>Renders the stored result in the current language; does nothing before the first one.</summary>
    private void RenderResult()
    {
        if (_resultTitleKey is null)
        {
            return;
        }

        // A key that could not be saved turns a success into a warning and says so after the message.
        bool downgrade = _resultKeyNotSaved && _resultSeverity == InfoBarSeverity.Success;
        string message = _resultMessage?.Text ?? "";
        if (_resultKeyNotSaved)
        {
            message = $"{message} {UiText.Get("connection.key-not-saved")}".Trim();
        }

        ResultBar.IsOpen = true;
        ResultBar.Severity = downgrade ? InfoBarSeverity.Warning : _resultSeverity;
        ResultBar.Title = UiText.Get(_resultTitleKey);
        ResultBar.Message = message;
    }

    /// <summary>
    /// Adds a warning to the result just shown when the API key could not be saved securely. The key
    /// still works until ImmichDrive closes; it is deliberately not written anywhere less safe.
    /// </summary>
    private void NoteKeyNotSaved()
    {
        _resultKeyNotSaved = true;
        RenderResult();
    }

    private void UpdateButtons()
    {
        bool online = DriveManager.Current.Status == DriveStatus.Online;
        DisconnectButton.Visibility = online ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    /// <summary>
    /// Saves the server URL to settings and the API key to the protected store. Returns false when
    /// the key could not be saved securely; callers report that with <see cref="NoteKeyNotSaved"/>.
    /// </summary>
    private bool SaveFields()
    {
        var s = SettingsManager.Current;
        s.ServerUrl = ServerBox.Text.Trim();
        bool keySaved = ApiKeyStore.TrySet(ApiKeyBox.Password);
        // Note: the drive folder is NOT written from here — it's read-only in the UI and only ever
        // changed via ChangeFolder_Click (which also moves the drive when it's live).
        SettingsManager.SaveSettings();
        return keySaved;
    }

    // ── Drive folder: read-only, changed via a preset/Browse dialog ──────────────────────

    private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsManager.Current;
        string current = s.EffectiveSyncRootPath;
        bool online = DriveManager.Current.Status == DriveStatus.Online;

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string home = Path.Combine(profile, "ImmichDrive");
        string pics = Path.Combine(pictures, "Immich");

        var paths = new List<string>();
        var radios = new RadioButtons();

        void AddOption(string label, string path, bool select = false)
        {
            int idx = paths.Count;
            paths.Add(path);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = label });
            panel.Children.Add(new TextBlock
            {
                Text = path,
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            radios.Items.Add(new RadioButton { Content = panel });
            if (select)
            {
                radios.SelectedIndex = idx;
            }
        }

        // Surface the current location first (pre-selected) when it isn't one of the presets.
        if (!PathsEqual(current, home) && !PathsEqual(current, pics))
        {
            AddOption(UiText.Get("connection.folder-current"), current, select: true);
        }

        AddOption(UiText.Get("connection.folder-home"), home, select: PathsEqual(current, home));
        AddOption(UiText.Get("connection.folder-pictures"), pics, select: PathsEqual(current, pics));

        var browseButton = new Button { Content = UiText.Get("connection.folder-browse"), Margin = new Thickness(0, 8, 0, 0) };
        browseButton.Click += async (object _, RoutedEventArgs __) =>
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            picker.FileTypeFilter.Add("*");
            var window = SettingsWindow.GetCurrent();
            if (window == null)
            {
                return;
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                AddOption(UiText.Get("connection.folder-chosen"), folder.Path, select: true);
            }
        };

        var content = new StackPanel { Spacing = 4 };
        if (online)
        {
            content.Children.Add(new TextBlock
            {
                Text = UiText.Get("connection.move-note"),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }

        content.Children.Add(radios);
        content.Children.Add(browseButton);

        var dialog = new ContentDialog
        {
            Title = UiText.Get(online ? "connection.move-title" : "connection.choose-title"),
            Content = content,
            PrimaryButtonText = UiText.Get(online ? "connection.move-confirm" : "connection.choose-confirm"),
            CloseButtonText = UiText.Get("kit.dialog.cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        int sel = radios.SelectedIndex;
        if (sel < 0 || sel >= paths.Count)
        {
            return;
        }

        string chosen = paths[sel];
        if (PathsEqual(chosen, current))
        {
            return; // no change
        }

        s.SyncRootPath = chosen;
        SettingsManager.SaveSettings();
        FolderBox.Text = s.EffectiveSyncRootPath;

        // If the drive is live, reconnecting relocates it: SyncRootService re-registers at the new
        // path and DriveManager retires (and deletes) the old folder. Otherwise the new path just
        // takes effect on the next Connect.
        if (online)
        {
            await MoveDriveAsync();
        }
    }

    private async Task MoveDriveAsync()
    {
        Busy.IsActive = true;
        ChangeFolderButton.IsEnabled = false;
        try
        {
            await DriveManager.Current.ConnectAsync();
            var dm = DriveManager.Current;
            if (dm.Status == DriveStatus.Online)
            {
                ShowResult(InfoBarSeverity.Success, "connection.moved-title",
                    new LocalizedMessage("connection.moved", ("path", SettingsManager.Current.EffectiveSyncRootPath)));
            }
            else
            {
                ShowResult(InfoBarSeverity.Error, "connection.move-failed-title", dm.StatusDetail);
            }
        }
        finally
        {
            Busy.IsActive = false;
            ChangeFolderButton.IsEnabled = true;
            UpdateButtons();
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        bool keySaved = SaveFields();
        Busy.IsActive = true;
        TestButton.IsEnabled = false;
        try
        {
            using var client = new ImmichClient(SettingsManager.Current.ServerUrl, ApiKeyStore.ApiKey,
                TimeSpan.FromSeconds(15));
            var (reachability, who) = await client.ProbeAsync();
            if (reachability == ImmichReachability.Ok)
            {
                ShowResult(InfoBarSeverity.Success, "connection.test-ok-title",
                    new LocalizedMessage("connection.test-ok", ("user", who), ("permission", UserReadPermission)));
            }
            else if (reachability == ImmichReachability.Unauthorized)
            {
                ShowResult(InfoBarSeverity.Error, "connection.test-rejected-title",
                    new LocalizedMessage("connection.test-rejected", ("permission", UserReadPermission)));
            }
            else
            {
                ShowResult(InfoBarSeverity.Error, "connection.test-unreachable-title",
                    new LocalizedMessage("connection.test-unreachable"));
            }

            if (!keySaved)
            {
                NoteKeyNotSaved();
            }
        }
        finally
        {
            Busy.IsActive = false;
            TestButton.IsEnabled = true;
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        bool keySaved = SaveFields();
        Busy.IsActive = true;
        ConnectButton.IsEnabled = false;
        try
        {
            await DriveManager.Current.ConnectAsync();
            var dm = DriveManager.Current;
            bool online = dm.Status == DriveStatus.Online;
            ShowResult(
                online ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                online ? "connection.connected-title" : "connection.connect-failed-title",
                dm.StatusDetail);

            if (!keySaved)
            {
                NoteKeyNotSaved();
            }
        }
        finally
        {
            Busy.IsActive = false;
            ConnectButton.IsEnabled = true;
            UpdateButtons();
        }
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        DriveManager.Current.Disconnect();
        ShowResult(InfoBarSeverity.Informational, "connection.disconnected-title",
            new LocalizedMessage("connection.disconnected"));
        UpdateButtons();
    }
}
