namespace ImmichDrive.Services;

/// <summary>
/// The app's GitHub repository, defined once so the About page link and the update checker cannot
/// drift apart again (FIX-016, BRAND-5).
/// </summary>
public static class SourceRepository
{
    /// <summary>GitHub organisation that owns the repository.</summary>
    public const string Owner = "Dynamic-Solutions-Canada";

    /// <summary>Repository name under <see cref="Owner"/>.</summary>
    public const string Name = "immich-drive";

    /// <summary>The repository's web page, for "view source" links.</summary>
    public static Uri WebUri { get; } = new($"https://github.com/{Owner}/{Name}");

    /// <summary>The About page's update check (kit): the Store when packaged, this repo's releases when not.</summary>
    public static WinUIAppKit.Updates.UpdateService Updates { get; } = new(Owner, Name);
}
