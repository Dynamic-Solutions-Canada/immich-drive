using ImmichDrive.Classes.Settings;
using ImmichDrive.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.IO;
using ImmichDrive.ViewModels;
using WinUIAppKit.Hosting;
using WinUIAppKit.Logging;
using WinUIAppKit.Settings;
using WinUIAppKit.Theming;

namespace ImmichDrive;

/// <summary>
/// Application entry point (WinUI 3). Unlike Repilot, this process is RESIDENT: it owns
/// the tray icon and hosts the Cloud Files sync provider for the lifetime of the session.
/// The visible <see cref="SettingsWindow"/> is shown on demand (tray menu / first run).
/// </summary>
public partial class App : Application
{

    public static DispatcherQueue MainDispatcherQueue { get; private set; } = null!;

    /// <summary>Path to the multi-size app icon (.ico) for Win32 window/tray/taskbar surfaces.</summary>
    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "Resources", "ImmichDrive.ico");

    /// <summary>Path to the muted variant of the app icon (greyscale mark + an amber pip), shown in the
    /// tray while the drive isn't live. Same sizes as <see cref="IconPath"/> so the tray stays crisp.</summary>
    public static string OfflineIconPath => Path.Combine(AppContext.BaseDirectory, "Resources", "ImmichDrive-Offline.ico");

    /// <summary>Path to a high-res PNG of the app icon. XAML <c>Image</c> elements decode this and
    /// scale it down crisply for whatever DPI they render at — a <c>BitmapImage</c> over the .ico
    /// would grab the tiny 16px frame and upscale it (blurry).</summary>
    public static string IconImagePath => Path.Combine(AppContext.BaseDirectory, "Resources", "ImmichDrive.png");

    /// <summary>The data folder name under %AppData%: settings, index, logs, the protected key file.
    /// Permanent: installed copies keep their data there.</summary>
    public const string DataFolderName = "ImmichDrive";

    /// <summary>What loading settings found at this launch, for the first-run and reset notices.</summary>
    internal static SettingsLoadResult<UserSettings>? SettingsLoad { get; private set; }

    public App()
    {
        // First, before anything logs: the shared NLog config writes beside settings.json.
        AppLogging.UseDataFolder(DataFolderName);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainDispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Crash logging, then settings, then the hidden host that owns the tray and the sync provider.
        _host = AppBootstrap.Launch(this, RestoreSettings, () => new MainWindow());
    }

    /// <summary>
    /// The settings phase of startup, in an order that matters:
    /// 1. claim the single instance, so a second launch exits without touching any file;
    /// 2. load the API key, migrating it out of settings.json, before the settings store rewrites
    ///    that file as it loads;
    /// 3. load settings and apply the saved theme.
    /// </summary>
    private static void RestoreSettings()
    {
        if (!MainWindow.ClaimSingleInstance())
        {
            Environment.Exit(0);
        }

        InitializeApiKey();
        SettingsLoad = SettingsManager.RestoreSettings();

        // The app is English only, so the shared "Our other apps" page is too (PROMO-7), rather than
        // following a Windows language the rest of the app does not speak. Package 10 points this at
        // the app's own language once the app is localized.
        TechnicallyReal.Promo.PromoLocalization.LocaleProvider = static () => "en-US";
        AppTheme.Set((AppThemeChoice)SettingsManager.Current.AppTheme);
    }

    /// <summary>
    /// Loads the API key, moving it out of settings.json on the first launch of a version that keeps
    /// it in the protected store. Must run before settings are loaded (see <see cref="RestoreSettings"/>).
    /// The key services are NLog-free (they are linked into the thumbnail extension and the test
    /// harness), so their log hooks are wired to NLog here.
    /// </summary>
    private static void InitializeApiKey()
    {
        var keyLogger = NLog.LogManager.GetLogger(nameof(ApiKeyStore));
        ApiKeyStore.LogInfo = message => keyLogger.Info(message);
        ApiKeyStore.LogWarning = message => keyLogger.Warn(message);

        var store = CredentialStore.Create(logError: (message, ex) => keyLogger.Warn(ex, message));
        ApiKeyStore.Initialize(SharedPaths.SettingsFilePath, store);
    }

    private Window? _host;
}
