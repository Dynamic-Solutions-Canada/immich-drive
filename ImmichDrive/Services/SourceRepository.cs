namespace ImmichDrive.Services;

/// <summary>
/// The app's GitHub repository, defined once so the About page link and the update checker cannot
/// drift apart again (FIX-016: both still named the owner from before the repository moved).
/// </summary>
public static class SourceRepository
{
    /// <summary>GitHub organisation that owns the repository.</summary>
    public const string Owner = "Dynamic-Solutions-Canada";

    /// <summary>Repository name under <see cref="Owner"/>.</summary>
    public const string Name = "immich-drive";

    /// <summary>The repository's web page, for "view source" links.</summary>
    public static Uri WebUri { get; } = new($"https://github.com/{Owner}/{Name}");

    /// <summary>GitHub API endpoint for the newest published release.</summary>
    public static Uri LatestReleaseApiUri { get; } = new($"https://api.github.com/repos/{Owner}/{Name}/releases/latest");
}
