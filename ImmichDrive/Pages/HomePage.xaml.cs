using ImmichDrive.Classes;
using ImmichDrive.Classes.Settings;
using ImmichDrive.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.IO;
using WinUIAppKit.Localization;

namespace ImmichDrive.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            DriveManager.Current.StatusChanged += OnStatusChanged;
            UiText.Changed += UpdateUi;   // the status lines are built here; XAML labels relabel themselves
            UpdateUi();
        };
        Unloaded += (s, e) =>
        {
            DriveManager.Current.StatusChanged -= OnStatusChanged;
            UiText.Changed -= UpdateUi;
        };
    }

    private void OnStatusChanged() => DispatcherQueue.TryEnqueue(UpdateUi);

    private void UpdateUi()
    {
        var dm = DriveManager.Current;
        FolderText.Text = SettingsManager.Current.EffectiveSyncRootPath;
        ConfigureButton.Visibility = dm.IsConfigured
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;
        RefreshButton.IsEnabled = dm.Status == DriveStatus.Online;
        RetryButton.Visibility = dm.CanRetry || dm.IsCheckingConnection
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        RetryButton.IsEnabled = !dm.IsCheckingConnection;
        RetryText.Text = UiText.Get(dm.IsCheckingConnection ? "common.checking" : "common.try-again");

        // Its content is a glyph and a label, so give the button the label as its screen-reader name (UI-14).
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(RetryButton, RetryText.Text);

        (StatusBar.Severity, StatusBar.Title, StatusBar.Message) = dm.Status switch
        {
            DriveStatus.Online when dm.SyncIssue != null =>
                (InfoBarSeverity.Warning, UiText.Get("status.sync-issue"), dm.SyncIssue.Text),
            DriveStatus.Online => (InfoBarSeverity.Success, UiText.Get("home.online"),
                dm.StatusDetail?.Text ?? UiText.Get("home.connected")),
            DriveStatus.Connecting => (InfoBarSeverity.Informational, UiText.Get("status.connecting"),
                dm.StatusDetail?.Text ?? ""),
            DriveStatus.Offline => (InfoBarSeverity.Warning, UiText.Get("status.offline"), UiText.Get("home.offline")),
            DriveStatus.Error => (InfoBarSeverity.Error, UiText.Get("home.problem"),
                dm.StatusDetail?.Text ?? UiText.Get("home.something-wrong")),
            _ => (InfoBarSeverity.Warning, UiText.Get("home.not-connected"),
                UiText.Get(dm.IsConfigured ? "home.disconnected" : "home.get-started")),
        };

        var (done, total) = dm.Progress;
        bool syncing = dm.Status == DriveStatus.Online && dm.IsPopulating;
        SyncProgress.Visibility = syncing ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        if (syncing) { SyncProgress.Maximum = Math.Max(total, 1); SyncProgress.Value = Math.Min(done, total); }
        DetailText.Text = SyncDetail(syncing, done, total);

        string? pins = DriveStatusText.PinProgress(dm);
        PinProgressText.Visibility = pins is null ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        PinProgress.Visibility = pins is null ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        if (pins is not null)
        {
            var (pinDone, pinTotal, _) = dm.PinProgress;
            PinProgressText.Text = pins;
            PinProgress.Maximum = pinTotal;
            PinProgress.Value = pinDone;
        }
    }

    /// <summary>
    /// The line under the drive folder: sync progress while syncing, otherwise when the drive was last
    /// updated, or nothing before the first sync.
    /// </summary>
    private static string SyncDetail(bool syncing, int done, int total)
    {
        if (syncing)
        {
            return total > done
                ? UiText.Format("home.syncing", ("done", DriveStatusText.Count(done)), ("total", DriveStatusText.Count(total)))
                : UiText.Get("status.finishing");
        }

        var lastSync = SettingsManager.Current.LastSyncUtc;
        return lastSync > DateTimeOffset.MinValue
            ? UiText.Format("home.last-updated", ("time", DriveStatusText.ShortDateTime(lastSync)))
            : "";
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string path = SettingsManager.Current.EffectiveSyncRootPath;
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => DriveManager.Current.Refresh();

    private void RetryButton_Click(object sender, RoutedEventArgs e) =>
        _ = DriveManager.Current.RetryConnectionAsync();

    private void ConfigureButton_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.GetCurrent()?.Select(SettingsWindow.ConnectionTag);
}
