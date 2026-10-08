using System;
using ImmichDrive.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using System.Reflection;

namespace ImmichDrive.Pages;

public sealed partial class AboutPage : Page
{
    private UpdateService.UpdateCheckResult? _update;

    public AboutPage()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v != null ? $"Version {v.Major}.{v.Minor}.{v.Build}" : "";
        if (File.Exists(App.IconImagePath))
        {
            try { AppIcon.Source = new BitmapImage(new Uri(App.IconImagePath)); } catch { }
        }
    }

    /// <summary>Opens Store updates for packaged copies or checks GitHub for development builds.</summary>
    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (UpdateService.IsPackaged)
        {
            UpdateStatusText.Text = UpdateService.OpenStoreUpdates()
                ? "The Microsoft Store is open to Updates & downloads. Check there for available updates."
                : "Unable to open the Microsoft Store.";
            return;
        }

        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = "Checking...";
        UpdateStatusText.Text = "Checking for updates...";

        var result = await UpdateService.CheckForUpdateAsync();
        if (result == null)
        {
            UpdateStatusText.Text = "Unable to check for updates. Check your internet connection.";
        }
        else if (result.UpdateAvailable)
        {
            _update = result;
            UpdateStatusText.Text = $"Version {result.LatestVersion} is available (you have {result.CurrentVersion}).";
            CheckUpdateButton.Content = "View Release";
            CheckUpdateButton.Click -= CheckForUpdates_Click;
            CheckUpdateButton.Click += ViewRelease_Click;
            CheckUpdateButton.IsEnabled = true;
            return;
        }
        else
        {
            UpdateStatusText.Text = $"You're up to date ({result.CurrentVersion}).";
        }

        CheckUpdateButton.Content = "Check for Updates";
        CheckUpdateButton.IsEnabled = true;
    }

    private void ViewRelease_Click(object sender, RoutedEventArgs e)
    {
        if (_update?.ReleaseUrl is { Length: > 0 } url)
            UpdateService.OpenUrl(url);
    }
}
