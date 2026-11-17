using ImmichDrive.Classes;
using ImmichDrive.Classes.Settings;
using ImmichDrive.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Diagnostics;
using System.IO;
using WinUIAppKit.Localization;
using WinUIAppKit.Theming;
using WinUIAppKit.Tray;
using WinUIAppKit.Windowing;

namespace ImmichDrive.Windows;

/// <summary>
/// The tray flyout shown on a left-click. Unlike the right-click menu (a static snapshot), it follows
/// <see cref="DriveManager.StatusChanged"/> and updates the sync progress live while open. The kit's
/// <see cref="FlyoutController"/> owns showing, placement by the taskbar, the acrylic backdrop, rounded
/// corners where Windows supports them, and dismissal on focus loss or Esc; this class only builds
/// content. Created once by the host and kept: closing parks it off-screen so it reopens instantly.
/// </summary>
public sealed partial class StatusFlyout : Window
{
    /// <summary>Fixed flyout width in device-independent pixels; the height follows the content.</summary>
    private const double WidthDip = 300;

    private readonly FlyoutController _controller;

    /// <summary>The content size last measured, so per-asset status updates re-place only on a change.</summary>
    private global::Windows.Foundation.Size _lastSize;

    public StatusFlyout()
    {
        InitializeComponent();

        if (File.Exists(App.IconImagePath))
        {
            try
            {
                Icon.Source = new BitmapImage(new Uri(App.IconImagePath));
            }
            catch
            {
                // The header icon is decoration; the flyout works without it.
            }
        }

        AppTheme.Track(this);
        _controller = new FlyoutController(this, Root, WidthDip);
        _controller.Opening += UpdateUi;
        DriveManager.Current.StatusChanged += OnStatusChanged;

        // Static labels relabel themselves (l10n:Localize); the status lines are built here.
        UiText.Changed += OnStatusChanged;
    }

    /// <summary>Opens the flyout by the tray icon, or closes it when it is open.</summary>
    /// <param name="anchor">The tray icon's position, from the click.</param>
    public void Toggle(TrayAnchor anchor) => _controller.Toggle(anchor.X, anchor.Y);

    /// <summary>Re-parks the flyout after the display layout changes.</summary>
    public void Park() => _controller.Park();

    /// <summary>Closes the flyout after one of its buttons opened something else.</summary>
    private void Dismiss() => _controller.Dismiss();

    /// <summary>Refreshes the content only while the flyout is open; <see cref="FlyoutController.Opening"/> refreshes it on open.</summary>
    private void OnStatusChanged()
    {
        if (_controller.IsOpen)
        {
            DispatcherQueue.TryEnqueue(UpdateUi);
        }
    }

    private void UpdateUi()
    {
        var dm = DriveManager.Current;
        var (done, total) = dm.Progress;
        bool syncing = dm.Status == DriveStatus.Online && dm.IsPopulating;
        bool checking = dm.IsCheckingConnection;

        // An error shows its own detail; every other state shares the tray's wording.
        StatusText.Text = !checking && dm.Status == DriveStatus.Error && dm.StatusDetail is { } error
            ? error.Text
            : DriveStatusText.Summary(dm, includeChecking: true);

        StatusDot.Fill = (Microsoft.UI.Xaml.Media.Brush)Root.Resources[
            checking || dm.Status == DriveStatus.Connecting ? "DotBusy" : dm.Status switch
            {
                DriveStatus.Online => "DotOnline",
                DriveStatus.Offline => "DotWarning",
                DriveStatus.Error => "DotError",
                _ => "DotIdle",
            }];

        // While offline, say how stale the view is rather than leaving the user guessing. The retries
        // are silent by design, so this line is the only place they're mentioned. (Error needs no
        // second line — its StatusText is already the detail.)
        string? detail = dm.Status switch
        {
            DriveStatus.Offline when checking => null,
            DriveStatus.Offline => OfflineDetail(dm),
            DriveStatus.Online when dm.SyncIssue != null => dm.SyncIssue.Text,
            _ => null,
        };
        DetailText.Text = detail ?? "";
        DetailText.Visibility = detail is null ? Visibility.Collapsed : Visibility.Visible;

        SyncProgress.Visibility = syncing ? Visibility.Visible : Visibility.Collapsed;
        if (syncing) { SyncProgress.Maximum = Math.Max(total, 1); SyncProgress.Value = Math.Min(done, total); }

        string? pins = DriveStatusText.PinProgress(dm);
        PinProgressText.Visibility = pins is null ? Visibility.Collapsed : Visibility.Visible;
        PinProgress.Visibility = pins is null ? Visibility.Collapsed : Visibility.Visible;
        if (pins is not null)
        {
            var (pinDone, pinTotal, _) = dm.PinProgress;
            PinProgressText.Text = pins;
            PinProgress.Maximum = pinTotal;
            PinProgress.Value = pinDone;
        }

        RetryButton.Visibility = dm.CanRetry || checking ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.IsEnabled = !checking;
        RetryText.Text = UiText.Get(checking ? "common.checking" : "common.try-again");
        RefreshButton.IsEnabled = dm.Status == DriveStatus.Online;

        RefitIfResized();
    }

    /// <summary>
    /// Rows appear and disappear as the state changes, so re-place the flyout when its content changes
    /// size. Skipped when the size is unchanged: <see cref="UpdateUi"/> runs once per synced asset, and
    /// moving the window that often would make it judder.
    /// </summary>
    private void RefitIfResized()
    {
        Root.Measure(new global::Windows.Foundation.Size(WidthDip, double.PositiveInfinity));
        var size = Root.DesiredSize;
        if (size == _lastSize)
        {
            return;
        }

        _lastSize = size;
        _controller.UpdateSize();
    }

    /// <summary>
    /// The offline line: how stale the listed photos are when the server answered earlier this
    /// session ("last updated 12:04"), otherwise just that retries continue.
    /// </summary>
    private static string OfflineDetail(DriveManager dm) =>
        dm.LastContactUtc > DateTimeOffset.MinValue
            ? UiText.Format("flyout.offline-since", ("time", DriveStatusText.ShortTime(dm.LastContactUtc)))
            : UiText.Get("flyout.offline");

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        DriveManager.Current.Refresh();   // stays open; progress shows live

    private void RetryButton_Click(object sender, RoutedEventArgs e) =>
        _ = DriveManager.Current.RetryConnectionAsync();   // stays open; the status updates live

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string path = SettingsManager.Current.EffectiveSyncRootPath;
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch { }
        Dismiss();
    }

    private void OpenImmichButton_Click(object sender, RoutedEventArgs e)
    {
        string url = SettingsManager.Current.ServerUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
        Dismiss();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Dismiss();
        MainWindow.ShowSettings();
    }
}
