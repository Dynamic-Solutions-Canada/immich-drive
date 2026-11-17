using ImmichDrive.Classes;
using ImmichDrive.Classes.Settings;
using ImmichDrive.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Localization;
using WinUIAppKit.Pages;
using WinUIAppKit.Windowing;

namespace ImmichDrive;

/// <summary>
/// The on-demand settings window (the app's only visible window), hosted in the kit's
/// <see cref="SettingsShell"/> (UI-1, UI-2): Home, Connection, Settings, Our other apps and About. One
/// instance at a time.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>The Home page's navigation tag.</summary>
    public const string HomeTag = "home";

    /// <summary>The Connection page's navigation tag.</summary>
    public const string ConnectionTag = "connection";

    private static SettingsWindow? _instance;

    public SettingsWindow()
    {
        if (_instance != null)
        {
            WindowChrome.BringToFront(_instance);
            Close();
            return;
        }

        InitializeComponent();
        _instance = this;

        // Mica, the title bar, the window icon, the saved placement and the app's theme (kit).
        Shell.Caption = "ImmichDrive";
        Shell.IconPngPath = App.IconImagePath;
        WindowChrome.SetUpSettingsWindow(this, Shell.TitleBar, App.IconPath, SettingsManager.Current.SettingsWindowPlacement);

        BuildMenu();
        Shell.PageSelected += OnPageSelected;
        Shell.Select(HomeTag);

        UiText.Changed += OnLanguageChanged;
        Closed += SettingsWindow_Closed;
    }

    /// <summary>The open settings window, or null when it is closed.</summary>
    public static SettingsWindow? GetCurrent() => _instance;

    /// <summary>
    /// Shows the page with <paramref name="tag"/>: <see cref="HomeTag"/>, <see cref="ConnectionTag"/>
    /// or the shell's <see cref="SettingsShell.SettingsTag"/>. Safe before the window has loaded.
    /// </summary>
    public void Select(string tag) => Shell.Select(tag);

    /// <summary>The app's own items, main content first (UI-2); the shell owns Settings and the footer.</summary>
    private void BuildMenu()
    {
        Shell.MenuItems.Clear();
        Shell.MenuItems.Add(new NavigationViewItem
        {
            Content = UiText.Get("nav.home"),
            Tag = HomeTag,
            Icon = new FontIcon { Glyph = "" },
        });
        Shell.MenuItems.Add(new NavigationViewItem
        {
            Content = UiText.Get("nav.connection"),
            Tag = ConnectionTag,
            Icon = new FontIcon { Glyph = "" },
        });
    }

    /// <summary>
    /// The language changed: the shell relabels its own items, but these are built in code, so relabel
    /// them in place (rebuilding would drop the selection).
    /// </summary>
    private void OnLanguageChanged()
    {
        foreach (var item in Shell.MenuItems.OfType<NavigationViewItem>())
        {
            item.Content = item.Tag switch
            {
                HomeTag => UiText.Get("nav.home"),
                ConnectionTag => UiText.Get("nav.connection"),
                _ => item.Content,
            };
        }
    }

    /// <summary>Shows the page for the selected item, unless it is already showing.</summary>
    private void OnPageSelected(string tag)
    {
        var (page, parameter) = PageFor(tag);
        if (Shell.ContentFrame.Content?.GetType() == page)
        {
            return;
        }

        Shell.ContentFrame.Navigate(page, parameter);
    }

    /// <summary>
    /// The page for a navigation tag, and its parameter: the kit's About page takes the app's
    /// <see cref="AboutInfo"/>, built fresh so it reads the current language.
    /// </summary>
    private static (Type Page, object? Parameter) PageFor(string tag) => tag switch
    {
        SettingsShell.SettingsTag => (typeof(SettingsPage), null),
        SettingsShell.AboutTag => (typeof(AboutPage), AboutContent.Create()),
        SettingsShell.OtherAppsTag => (typeof(TechnicallyReal.Promo.PromotedAppsPage), null),
        ConnectionTag => (typeof(ConnectionPage), null),
        _ => (typeof(HomePage), null),
    };

    private void SettingsWindow_Closed(object sender, WindowEventArgs e)
    {
        UiText.Changed -= OnLanguageChanged;

        // Null when minimized or maximized, so an odd normal size is never saved.
        if (WindowChrome.CapturePlacement(this) is { } placement)
        {
            SettingsManager.Current.SettingsWindowPlacement = placement;
        }

        SettingsManager.SaveSettings();
        _instance = null;
    }
}
