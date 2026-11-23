using ImmichDrive.Classes;
using ImmichDrive.Classes.Settings;
using ImmichDrive.Services;
using ImmichDrive.Windows;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using WinUIAppKit;
using WinUIAppKit.Hosting;
using WinUIAppKit.Localization;
using WinUIAppKit.Logging;
using WinUIAppKit.Settings;
using WinUIAppKit.Startup;
using WinUIAppKit.Tray;
using WinUIAppKit.Windowing;

namespace ImmichDrive;

/// <summary>
/// The invisible host window (kit <see cref="HostWindow"/>). It owns the tray icon and lives as long as
/// the process, which keeps the Cloud Files sync provider connected; the visible UI is the on-demand
/// <see cref="SettingsWindow"/> and the tray <see cref="StatusFlyout"/>.
/// </summary>
public sealed partial class MainWindow : Window
{
    // ── Permanent identifiers (BRAND-10): installed copies and Windows depend on them. ──
    /// <summary>The host window title a second launch looks for.</summary>
    public const string HostWindowTitle = "ImmichDrive Host";

    /// <summary>The registered message a second launch posts to show settings.</summary>
    public const string ShowSettingsMessageName = "ImmichDrive_ShowSettings";

    /// <summary>The single-instance mutex.</summary>
    private const string MutexName = "ImmichDrive.Instance";

    /// <summary>The registered tray callback message.</summary>
    private const string TrayCallbackMessageName = "ImmichDrive_TrayCallback";

    /// <summary>The tray icon's identity; Windows keys the user's show/hide choice on it. Never change it.</summary>
    private static readonly Guid TrayGuid = new("48633E3A-477E-41D2-8596-F1851C4D2045");

    /// <summary>Start at sign-in: the manifest startup task, or the Run key when unpackaged.</summary>
    public static StartupRegistration Startup { get; } = new("ImmichDriveAutoStart", "ImmichDrive");

    private readonly HostWindow _host;
    private readonly TrayIcon _tray;
    private readonly StatusFlyout _flyout;

    /// <summary>The tray icon variant last drawn, so a status change re-renders only when it differs.</summary>
    private bool _mutedIconShown;

    /// <summary>
    /// Claims the single instance. When another copy runs, asks it to show settings (unless this launch
    /// came from sign-in) and returns false. Call before settings load.
    /// </summary>
    public static bool ClaimSingleInstance()
    {
        if (SingleInstance.TryClaim(MutexName))
        {
            return true;
        }

        if (!Startup.IsSilentLaunch())
        {
            SingleInstance.SignalRunningInstance(HostWindowTitle, ShowSettingsMessageName);
        }

        return false;
    }

    public MainWindow()
    {
        InitializeComponent();

        _host = new HostWindow(this, HostWindowTitle, ShowSettingsMessageName);
        _host.ShowRequested += ShowSettings;

        _flyout = new StatusFlyout();

        // Tray icon: left click toggles the live status flyout, double click opens settings, right
        // click the menu. Re-rendered when scaling, the taskbar theme or the drive state changes.
        _tray = new TrayIcon(_host.Hwnd, TrayGuid, TrayCallbackMessageName);
        _tray.Clicked += anchor => _flyout.Toggle(anchor);
        _tray.DoubleClicked += _ => ShowSettings();
        _tray.ContextMenuRequested += ShowTrayMenu;
        _tray.RenderNeeded += () => UpdateTray(forceIcon: true);
        _tray.DisplayChanged += _flyout.Park;
        UpdateTray(forceIcon: true);

        DriveManager.Current.StatusChanged += OnDriveStatusChanged;

        // A language change relabels the tooltip now; the menu is built when it opens, so it follows.
        UiText.Changed += OnLanguageChanged;

        // Teardown runs on Exit, and when Windows signs out, shuts down or closes the app for an update, in this
        // order (kit HostWindow).
        _host.AddExitStep("stop tray updates", () =>
        {
            DriveManager.Current.StatusChanged -= OnDriveStatusChanged;
            UiText.Changed -= OnLanguageChanged;
        });
        _host.AddExitStep("save settings", SettingsManager.SaveSettings);
        _host.AddExitStep("remove the tray icon", _tray.Dispose);

        _ = RegisterStartupOnFirstRunAsync();

        // The thumbnail provider's old log held full photo paths in ProgramData (FIX-063).
        _ = Task.Run(SharedPaths.DeleteLegacyThumbnailLog);

        // Connect in the background when configured. When settings were reset, open the Settings page,
        // which shows the notice on this launch only (CONF-2); otherwise show settings on first run or
        // with no usable configuration.
        if (DriveManager.Current.IsConfigured)
        {
            _ = DriveManager.Current.ConnectAsync();
        }

        if (App.SettingsLoad?.NeedsResetNotice == true)
        {
            ShowSettingsPage();
        }
        else if (!DriveManager.Current.IsConfigured)
        {
            ShowSettings();
        }
    }

    /// <summary>
    /// Shows the settings window, creating it if needed, and brings it to the front. Used by the tray,
    /// the flyout and a second launch.
    /// </summary>
    public static void ShowSettings()
    {
        var window = SettingsWindow.GetCurrent() ?? new SettingsWindow();
        window.Activate();
        WindowChrome.BringToFront(window);
    }

    /// <summary>
    /// The tray menu's "Settings" (TRAY-5), and the launch that reset unreadable settings: the settings
    /// window on its Settings page.
    /// </summary>
    private static void ShowSettingsPage()
    {
        ShowSettings();
        SettingsWindow.GetCurrent()?.Select(WinUIAppKit.Pages.SettingsShell.SettingsTag);
    }

    /// <summary>
    /// On the first launch only, enables start at sign-in while it is still in its initial state; never
    /// overrides a user who turned it off in Windows Settings, or a policy (TRAY-10). When packaged,
    /// also removes a Run entry left by an earlier unpackaged build.
    /// </summary>
    private static async Task RegisterStartupOnFirstRunAsync()
    {
        Startup.RemoveUnpackagedEntries();
        if (App.SettingsLoad?.Outcome == SettingsLoadOutcome.FirstRun)
        {
            await Startup.RegisterOnFirstRunAsync();
        }
    }

    // ── Tray icon ───────────────────────────────────────────────────
    /// <summary>
    /// True when the drive isn't usable, so the tray shows the muted icon. Connecting keeps the normal
    /// one: a brief startup blink between the two variants would be worse than no signal.
    /// </summary>
    private static bool WantsMutedIcon =>
        DriveManager.Current.Status is DriveStatus.Offline or DriveStatus.Error or DriveStatus.Disconnected;

    /// <summary>
    /// Reflects the drive state in the tray: the icon goes muted when the drive isn't live and the
    /// tooltip says why. Deliberately silent: no balloon, so nothing pops or chimes.
    /// </summary>
    /// <param name="forceIcon">Re-render the icon even when the variant did not change (scaling or theme changed).</param>
    private void UpdateTray(bool forceIcon = false)
    {
        bool muted = WantsMutedIcon;
        if (forceIcon || muted != _mutedIconShown || _trayIconPng is null)
        {
            _trayIconPng = RenderTrayIcon(muted);
            _mutedIconShown = muted;
        }

        _tray.Update(_trayIconPng, TrayTooltip());
    }

    private byte[]? _trayIconPng;

    /// <summary>
    /// Draws the app's .ico artwork (normal or muted) at the tray's exact pixel size, so it stays crisp
    /// at any scaling. Falls back to the normal icon when the muted one is missing.
    /// </summary>
    private static byte[] RenderTrayIcon(bool muted)
    {
        string path = muted && File.Exists(App.OfflineIconPath) ? App.OfflineIconPath : App.IconPath;
        int size = TrayIcon.IconPixelSize;

        return TrayIconRasterizer.Render(size, (graphics, side) =>
        {
            // Pick the frame closest to the supersampled canvas, then scale it smoothly.
            using var icon = new Icon(path, (int)side, (int)side);
            using var bitmap = icon.ToBitmap();
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(bitmap, 0, 0, side, side);
        });
    }

    /// <summary>Relabels the tray tooltip in the new interface language.</summary>
    private void OnLanguageChanged() => UpdateTray();

    /// <summary>The tray tooltip in the current language (shared wording with the menu and flyout).</summary>
    private static string TrayTooltip() => DriveStatusText.Tooltip(DriveManager.Current);

    // Throttle tray updates: during a sync, StatusChanged fires once per asset (thousands of times).
    // Rewriting the tray that fast makes the icon flicker. Coalesce to about one a second, with a
    // trailing update so the final "Up to date" still lands.
    private long _lastTipTicks;
    private bool _tipUpdatePending;

    private void OnDriveStatusChanged()
    {
        if (_tipUpdatePending)
        {
            return;
        }

        long now = Environment.TickCount64;
        long since = now - _lastTipTicks;
        if (since >= 750)
        {
            _lastTipTicks = now;
            App.MainDispatcherQueue.TryEnqueue(() => UpdateTray());
        }
        else
        {
            _tipUpdatePending = true;
            _ = Task.Delay((int)(750 - since)).ContinueWith(_ =>
            {
                _tipUpdatePending = false;
                _lastTipTicks = Environment.TickCount64;
                App.MainDispatcherQueue.TryEnqueue(() => UpdateTray());
            });
        }
    }

    // ── Context menu ────────────────────────────────────────────────
    /// <summary>
    /// The right-click menu (TRAY-5): a status line, the drive actions, then Settings, Open logs
    /// folder and Exit. A snapshot; the flyout is the live view.
    /// </summary>
    private void ShowTrayMenu(int x, int y)
    {
        var dm = DriveManager.Current;
        string status = DriveStatusText.Summary(dm);

        // Offline still counts as "on": Disconnect is how the user stops the drive and its retries.
        bool driveOn = dm.Status is DriveStatus.Online or DriveStatus.Offline;

        var menu = new TrayMenu()
            .Add(status, () => { }, enabled: false)
            .AddSeparator();

        if (dm.CanRetry)
        {
            menu.Add(UiText.Get(dm.IsCheckingConnection ? "common.checking" : "common.try-again"),
                    () => _ = dm.RetryConnectionAsync(),
                    enabled: !dm.IsCheckingConnection)
                .AddSeparator();
        }

        menu.Add(UiText.Get("common.open-drive-folder"), OpenDriveFolder)
            .Add(UiText.Get("common.refresh"), dm.Refresh, enabled: dm.Status == DriveStatus.Online)
            .Add(UiText.Get(driveOn ? "common.disconnect" : "common.connect"), () => ToggleDrive(driveOn))
            .AddSeparator()
            .Add(UiText.Get("kit.settings.title"), ShowSettingsPage)
            .Add(UiText.Get("kit.about.open-logs-folder"), AppLogging.OpenLogsFolder)
            .AddSeparator()
            .Add(UiText.Get("tray.exit"), _host.Exit)
            .Show(_host.Hwnd, x, y);
    }

    private static void ToggleDrive(bool driveOn)
    {
        if (driveOn)
        {
            DriveManager.Current.Disconnect();
        }
        else
        {
            _ = DriveManager.Current.ConnectAsync();
        }
    }

    private static void OpenDriveFolder()
    {
        string path = SettingsManager.Current.EffectiveSyncRootPath;
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // Explorer could not open the folder; nothing useful to tell the user from a tray menu.
        }
    }
}
