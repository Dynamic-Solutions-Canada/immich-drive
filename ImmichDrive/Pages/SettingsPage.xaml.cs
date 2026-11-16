using ImmichDrive.Classes.Settings;
using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Startup;

namespace ImmichDrive.Pages;

/// <summary>
/// App theme and start at sign-in. Windows owns the startup state, so the switch shows what Windows
/// reports and never overrides a user's or a policy's decision (TRAY-10). Package 12 replaces these
/// rows with the kit's shared ones.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        ThemeCombo.SelectedIndex = Math.Clamp(SettingsManager.Current.AppTheme, 0, 2);
        _loading = false;

        _ = LoadStartupAsync();
    }

    /// <summary>Reads the real startup state, which can take a moment, and shows it.</summary>
    private async Task LoadStartupAsync()
    {
        var status = await MainWindow.Startup.GetStatusAsync();
        ShowStartupStatus(status);
    }

    /// <summary>
    /// Sets the switch without raising a change, and disables it with an explanation when Windows
    /// Settings or a policy decides instead of the app.
    /// </summary>
    private void ShowStartupStatus(StartupStatus status)
    {
        _loading = true;
        StartupToggle.IsOn = status.IsOn();
        StartupToggle.IsEnabled = status.CanChange();
        _loading = false;

        string? reason = status switch
        {
            StartupStatus.DisabledByUser => "Turned off in Windows Settings (Apps > Startup). Turn it on there.",
            StartupStatus.DisabledByPolicy => "Turned off by your organization's policy.",
            StartupStatus.EnabledByPolicy => "Turned on by your organization's policy.",
            _ => null,
        };
        ToolTipService.SetToolTip(StartupToggle, reason);
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        // The setting's side-effect applies the theme to every open window.
        SettingsManager.Current.AppTheme = ThemeCombo.SelectedIndex;
        SettingsManager.SaveSettings();
    }

    private async void StartupToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        // Windows can refuse; show what it decided rather than what was asked for.
        var status = await MainWindow.Startup.SetEnabledAsync(StartupToggle.IsOn);
        ShowStartupStatus(status);
    }
}
