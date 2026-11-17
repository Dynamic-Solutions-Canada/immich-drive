using WinUIAppKit.Localization;

namespace ImmichDrive.Classes;

/// <summary>
/// The interface languages ImmichDrive ships (LOC-1, LOC-2): the family's nine identities in picker
/// order, from <c>external/winui-app-kit/build/languages.json</c>. Canadian English reuses the US
/// wording and Canadian French the French wording (LOC-5); they stay separate choices so the picker and
/// Windows' preferences match them exactly. Keep this list, <c>Localization/Catalogs/*.json</c> and the
/// manifest's <c>&lt;Resource Language&gt;</c> entries in step; <c>npm run check:l10n</c> checks them.
/// </summary>
public static class AppLanguages
{
    /// <summary>The resource name prefix of the embedded catalogs (see the csproj's EmbeddedResource).</summary>
    public const string CatalogPrefix = "ImmichDrive.Localization.";

    /// <summary>The supported set, in picker order.</summary>
    public static SupportedLanguages Supported { get; } = new("en-US",
    [
        ("en-US", "en-US", "English (United States)"),
        ("en-CA", "en-US", "English (Canada)"),
        ("es-ES", "es-ES", "Español (España)"),
        ("fr-FR", "fr-FR", "Français (France)"),
        ("fr-CA", "fr-FR", "Français (Canada)"),
        ("de-DE", "de-DE", "Deutsch (Deutschland)"),
        ("zh-CN", "zh-CN", "简体中文（中国）"),
        ("pt-BR", "pt-BR", "Português (Brasil)"),
        ("ja-JP", "ja-JP", "日本語（日本）"),
    ]);
}
