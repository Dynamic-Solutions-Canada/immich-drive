using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ImmichDrive.Services;

/// <summary>
/// Opens the Microsoft Store's updates page for packaged copies and checks GitHub Releases
/// for unpackaged development builds.
/// </summary>
public static class UpdateService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private const string StoreUpdatesUri = "ms-windows-store://downloadsandupdates";

    private const string Owner = "RyanEwen";
    private const string Repo = "ImmichDrive";

    private static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"ImmichDrive/{CurrentVersion()}");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public sealed class UpdateCheckResult
    {
        public bool UpdateAvailable { get; init; }
        public string CurrentVersion { get; init; } = "";
        public string LatestVersion { get; init; } = "";
        public string? ReleaseUrl { get; init; }
    }

    public static string CurrentVersion()
    {
        var v = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
        return $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>Opens the Store's Updates and downloads page for packaged copies.</summary>
    public static bool OpenStoreUpdates()
    {
        try
        {
            Process.Start(new ProcessStartInfo(StoreUpdatesUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to open Microsoft Store updates");
            return false;
        }
    }

    /// <summary>Checks GitHub releases for an unpackaged development build.</summary>
    public static Task<UpdateCheckResult?> CheckForUpdateAsync() =>
        CheckGitHubForUpdateAsync();

    private static async Task<UpdateCheckResult?> CheckGitHubForUpdateAsync()
    {
        try
        {
            using var resp = await Http.GetAsync(LatestReleaseUri);
            // No releases published yet → you already have the latest.
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return new UpdateCheckResult { CurrentVersion = CurrentVersion(), LatestVersion = CurrentVersion() };

            resp.EnsureSuccessStatusCode();
            var release = await resp.Content.ReadFromJsonAsync(GitHubJsonContext.Default.GitHubRelease);
            if (release == null || string.IsNullOrWhiteSpace(release.TagName)) return null;

            var current = ParseVersion(CurrentVersion());
            var latest = ParseVersion(release.TagName);
            if (current == null || latest == null) return null;

            return new UpdateCheckResult
            {
                UpdateAvailable = latest > current,
                CurrentVersion = current.ToString(3),
                LatestVersion = latest.ToString(3),
                ReleaseUrl = release.HtmlUrl,
            };
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Update check failed");
            return null;
        }
    }

    /// <summary>
    /// True when running from an MSIX package, so the Microsoft Store owns updating.
    /// </summary>
    /// <remarks>
    /// <c>Package.Current</c> throws rather than returning null when the process is unpackaged,
    /// which is the documented way to tell the two apart. Cached because the answer cannot
    /// change while the process lives.
    /// </remarks>
    public static bool IsPackaged { get; } = DetectPackaged();

    private static bool DetectPackaged()
    {
        try
        {
            return global::Windows.ApplicationModel.Package.Current is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Warn(ex, "Failed to open {Url}", url); }
    }

    private static Version? ParseVersion(string s)
    {
        s = s.Trim().TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : null;
    }
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
internal partial class GitHubJsonContext : JsonSerializerContext { }
