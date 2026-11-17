using ImmichDrive.Classes.Settings;
using Microsoft.UI.Xaml.Controls;

namespace ImmichDrive.Pages;

/// <summary>
/// App-wide settings (the shell's built-in Settings item), from the kit's shared rows (UI-4): start
/// with Windows, language and theme. Every edit applies and saves at once. Start with Windows is
/// Windows' own state (TRAY-10), so the row reads and changes it through the app's
/// <c>StartupRegistration</c> and nothing is saved here for it.
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();

        StartupRow.Registration = MainWindow.Startup;
        LanguageRow.ChoiceChanged += identity =>
        {
            SettingsManager.Current.Language = identity;
            SettingsManager.SaveSettings();
        };
        ThemeRow.ChoiceChanged += choice =>
        {
            SettingsManager.Current.AppTheme = (int)choice;
            SettingsManager.SaveSettings();
        };

        // Shown once, on the launch that found the settings file unreadable (CONF-2, UI-15).
        NoticeHost.Content = App.TakeResetNotice();
    }
}
