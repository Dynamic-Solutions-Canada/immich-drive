using WinUIAppKit.Localization;

namespace ImmichDrive.Classes;

/// <summary>
/// The interface languages ImmichDrive ships, read by <see cref="UiText"/>. English only for now: the
/// app's own text moves into <c>Localization/Catalogs/</c> and the family's nine-language set
/// (<c>external/winui-app-kit/build/languages.json</c>) arrives with the localization package. Keep
/// this list, the catalog files and the manifest's <c>&lt;Resource Language&gt;</c> entries in step.
/// </summary>
public static class AppLanguages
{
    /// <summary>The resource name prefix of the embedded catalogs (see the csproj's EmbeddedResource).</summary>
    public const string CatalogPrefix = "ImmichDrive.Localization.";

    /// <summary>The supported set, in picker order.</summary>
    public static SupportedLanguages Supported { get; } = new("en-US",
    [
        ("en-US", "en-US", "English (United States)"),
    ]);
}
