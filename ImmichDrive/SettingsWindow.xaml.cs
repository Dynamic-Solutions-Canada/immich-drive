using ImmichDrive.Classes.Settings;
using ImmichDrive.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using System.Linq;
using WinRT.Interop;
using WinUIAppKit.Theming;
using WinUIAppKit.Windowing;

namespace ImmichDrive;

/// <summary>The on-demand settings window (the app's only visible window).</summary>
public sealed partial class SettingsWindow : Window
{
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

        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Sharp window and taskbar icon at this window's scaling, re-applied on activation (kit).
        WindowIcon.Apply(this, App.IconPath);
        LoadTitleBarIcon();

        // Saved size and position, or centred on the cursor's monitor; placement on a monitor that is
        // gone is ignored.
        WindowChrome.RestorePlacement(this, SettingsManager.Current.SettingsWindowPlacement, 820, 680);

        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
        ContentFrame.Navigate(typeof(HomePage));
        AppTheme.Track(this);

        Closed += SettingsWindow_Closed;
    }

    public static SettingsWindow? GetCurrent() => _instance;

    public void NavigateTo(Type pageType)
    {
        ContentFrame.Navigate(pageType);
        if (pageType == typeof(SettingsPage))
        {
            RootNavigation.SelectedItem = RootNavigation.SettingsItem;
            return;
        }
        foreach (var item in RootNavigation.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is string tag && GetPageTypeFromTag(tag) == pageType) { RootNavigation.SelectedItem = item; return; }
        }

        foreach (var item in RootNavigation.FooterMenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is string tag && GetPageTypeFromTag(tag) == pageType) { RootNavigation.SelectedItem = item; return; }
        }
    }

    private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type? pageType = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : (args.SelectedItem as NavigationViewItem)?.Tag is string tag ? GetPageTypeFromTag(tag) : null;
        if (pageType == null || ContentFrame.Content?.GetType() == pageType)
        {
            return;
        }

        ContentFrame.Navigate(pageType);
    }

    private static Type? GetPageTypeFromTag(string tag) => tag switch
    {
        "HomePage" => typeof(HomePage),
        "ConnectionPage" => typeof(ConnectionPage),
        "SettingsPage" => typeof(SettingsPage),
        "PromotedAppsPage" => typeof(TechnicallyReal.Promo.PromotedAppsPage),
        "AboutPage" => typeof(AboutPage),
        _ => null
    };

    private void LoadTitleBarIcon()
    {
        // Use the high-res PNG (not the .ico) so it scales down crisply to the 16px title bar.
        try { TitleBarIcon.Source = new BitmapImage(new Uri(App.IconImagePath)); }
        catch { /* leave empty */ }
    }

    private void SettingsWindow_Closed(object sender, WindowEventArgs e)
    {
        // Null when minimized or maximized, so an odd normal size is never saved.
        if (WindowChrome.CapturePlacement(this) is { } placement)
        {
            SettingsManager.Current.SettingsWindowPlacement = placement;
        }

        SettingsManager.SaveSettings();
        _instance = null;
    }
}
