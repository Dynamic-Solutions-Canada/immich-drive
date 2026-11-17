using ImmichDrive.Classes.Settings;
using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Localization;

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

        // Reset settings (CONF-4): the kit's row asks first and runs this app's reset, which keeps the
        // drive and its connection (SettingsManager.ResetSettings); reopening the page shows the defaults.
        ResetRow.Window = SettingsWindow.GetCurrent();
        ResetRow.ResetAsync = () =>
        {
            SettingsManager.ResetSettings();
            return Task.CompletedTask;
        };
        ResetRow.KeptNote = () => UiText.Get("settings.reset-kept");
        ResetRow.ResetCompleted += () => Frame?.Navigate(typeof(SettingsPage));

        // Shown once, on the launch that found the settings file unreadable (CONF-2, UI-15).
        NoticeHost.Content = App.TakeResetNotice();
    }
}
