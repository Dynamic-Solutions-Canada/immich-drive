using ImmichDrive.Classes.Settings;
using ImmichDrive.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.IO;
using static ImmichDrive.Classes.NativeMethods;

namespace ImmichDrive;

/// <summary>
/// Application entry point (WinUI 3). Unlike Repilot, this process is RESIDENT: it owns
/// the tray icon and hosts the Cloud Files sync provider for the lifetime of the session.
/// The visible <see cref="SettingsWindow"/> is shown on demand (tray menu / first run).
/// </summary>
public partial class App : Application
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

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

    // Single resident instance — a second launch just surfaces settings on the first.
    private static readonly Mutex Singleton = new(true, "ImmichDrive.Instance");

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainDispatcherQueue = DispatcherQueue.GetForCurrentThread();

        if (!Singleton.WaitOne(TimeSpan.Zero, true))
        {
            // Already running — tell the existing instance to open settings, then exit.
            IntPtr existing = FindWindow(null, MainWindow.HostWindowTitle);
            if (existing != IntPtr.Zero)
            {
                PostMessage(existing, MainWindow.WmShowSettings, IntPtr.Zero, IntPtr.Zero);
            }

            Environment.Exit(0);
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (s, a) =>
        {
            Logger.Error(a.ExceptionObject as Exception, "Unhandled exception");
            NLog.LogManager.Flush();
        };
        UnhandledException += (s, e) =>
        {
            Logger.Error(e.Exception, "Unhandled UI exception");
            NLog.LogManager.Flush();
            e.Handled = true;
        };

        SettingsManager.RestoreSettings();

        InitializeApiKey();

        // The invisible host owns the tray icon and the sync provider. It shows the
        // settings window itself on first run (when no server is configured yet).
        _host = new MainWindow();
        _host.Activate();
        _host.HideHost(); // Activate() shows the window; immediately re-hide the invisible host.
    }

    /// <summary>
    /// Loads the API key, moving it out of settings.json on the first launch of a version that keeps
    /// it in the protected store. Must run after settings are restored and before anything saves them.
    /// The key services are NLog-free (they are linked into the thumbnail extension and the test
    /// harness), so their log hooks are wired to NLog here.
    /// </summary>
    private static void InitializeApiKey()
    {
        var keyLogger = NLog.LogManager.GetLogger(nameof(ApiKeyStore));
        ProtectedStore.LogError = (message, ex) => keyLogger.Warn(ex, message);
        ApiKeyStore.LogInfo = message => keyLogger.Info(message);
        ApiKeyStore.LogWarning = message => keyLogger.Warn(message);

        ApiKeyStore.Initialize(SharedPaths.SettingsFilePath);
    }

    private MainWindow? _host;
}
