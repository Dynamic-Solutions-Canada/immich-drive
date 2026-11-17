using ImmichDrive.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Localization;
using WinUIAppKit.Pages;

namespace ImmichDrive.Classes;

/// <summary>
/// What the kit's shared About page shows for ImmichDrive (UI-5, BRAND-8): the standard cards from the
/// repository named in <see cref="SourceRepository"/>, then two product cards.
/// </summary>
internal static class AboutContent
{
    /// <summary>
    /// Where the generated notices live in the repo, relative to its root. `npm run check:notices`
    /// fails if this stops matching the notices output (`notices.about_source` in windows-build.json).
    /// </summary>
    public const string NoticesPath = "ImmichDrive/THIRD-PARTY-NOTICES.txt";

    /// <summary>The product's registered privacy policy (DOCS-8). Permanent once the app ships (BRAND-11).</summary>
    public static readonly Uri PrivacyPolicy = new("https://www.dynamic-solutions.ca/privacy/immichdrive");

    /// <summary>Immich's own site, linked from the product card.</summary>
    private static readonly Uri ImmichSite = new("https://immich.app");

    /// <summary>Builds the page's content; called each time the About page opens, so text is current.</summary>
    public static AboutInfo Create()
    {
        var (source, support, license) = AboutInfo.GitHubLinks(SourceRepository.Owner, SourceRepository.Name);
        return new AboutInfo
        {
            ProductName = "ImmichDrive",
            DescriptionKey = "AppDescription",
            IconPngPath = App.IconImagePath,
            PrivacyPolicy = PrivacyPolicy,
            Updates = SourceRepository.Updates,
            SourceCode = source,
            Support = support,
            License = license,

            // The generated notices ship in the package (HYG-3); the link opens the same file in the
            // repo, since a packaged app cannot hand a file inside its install folder to the browser.
            ThirdPartyNotices = new Uri(source + "/blob/main/" + NoticesPath),

            ExtraCards =
            [
                () => ProductCard("\uE753", "about.how-it-works", "about.how-it-works-desc", null, null),
                () => ProductCard("\uE968", "about.immich", "about.immich-desc", ImmichSite, "about.immich-link"),
            ],
        };
    }

    /// <summary>
    /// One product card in the kit's card style: a glyph, a title and a description, with an optional
    /// link on the right.
    /// </summary>
    /// <param name="glyph">A Segoe Fluent Icons glyph.</param>
    /// <param name="titleKey">Catalog key of the card's title.</param>
    /// <param name="descriptionKey">Catalog key of the card's description.</param>
    /// <param name="link">Where the link goes, or null for a card without one.</param>
    /// <param name="linkKey">Catalog key of the link's label; used only with <paramref name="link"/>.</param>
    private static UIElement ProductCard(string glyph, string titleKey, string descriptionKey, Uri? link, string? linkKey)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Decorative: the title beside it says what the card is.
        grid.Children.Add(new FontIcon { Glyph = glyph, FontSize = 24, VerticalAlignment = VerticalAlignment.Center });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = UiText.Get(titleKey),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        text.Children.Add(SecondaryText.Caption(UiText.Get(descriptionKey)));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (link is not null && linkKey is not null)
        {
            var button = new HyperlinkButton
            {
                Content = UiText.Get(linkKey),
                NavigateUri = link,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(button, 2);
            grid.Children.Add(button);
        }

        return new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Child = grid,
        };
    }
}
