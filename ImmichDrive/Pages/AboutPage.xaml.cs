using System;
using ImmichDrive.Services;
using WinUIAppKit.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using System.Reflection;

namespace ImmichDrive.Pages;

public sealed partial class AboutPage : Page
{
    private UpdateCheckResult? _update;

    public AboutPage()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v != null ? $"Version {v.Major}.{v.Minor}.{v.Build}" : "";
        SourceLink.NavigateUri = SourceRepository.WebUri;
        if (File.Exists(App.IconImagePath))
        {
            try { AppIcon.Source = new BitmapImage(new Uri(App.IconImagePath)); } catch { }
        }
    }

    /// <summary>
    /// "Check for updates" (DIST-6): the Microsoft Store's updates page for packaged copies, GitHub
    /// Releases for development builds. Never runs in the background.
    /// </summary>
    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = "Checking...";
        UpdateStatusText.Text = "Checking for updates...";

        var result = await SourceRepository.Updates.CheckForUpdatesAsync();
        switch (result.Outcome)
        {
            case UpdateCheckOutcome.OpenedStore:
                UpdateStatusText.Text = "The Microsoft Store is open to Updates & downloads. Check there for available updates.";
                break;
            case UpdateCheckOutcome.UpdateAvailable:
                _update = result;
                UpdateStatusText.Text = $"Version {result.LatestVersion} is available (you have {result.CurrentVersion}).";
                CheckUpdateButton.Content = "View Release";
                CheckUpdateButton.Click -= CheckForUpdates_Click;
                CheckUpdateButton.Click += ViewRelease_Click;
                CheckUpdateButton.IsEnabled = true;
                return;
            case UpdateCheckOutcome.UpToDate:
                UpdateStatusText.Text = $"You're up to date ({result.CurrentVersion}).";
                break;
            default:
                UpdateStatusText.Text = "Unable to check for updates. Check your internet connection.";
                break;
        }

        CheckUpdateButton.Content = "Check for Updates";
        CheckUpdateButton.IsEnabled = true;
    }

    private void ViewRelease_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not null)
        {
            UpdateService.OpenReleasePage(_update);
        }
    }
}
