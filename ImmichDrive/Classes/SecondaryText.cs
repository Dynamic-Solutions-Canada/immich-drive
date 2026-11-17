using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ImmichDrive.Classes;

/// <summary>
/// Wrapping text in the secondary text colour, for text built in code (product cards, dialog content).
/// Built from markup so the colour is a <c>{ThemeResource}</c>: it follows the element's own light, dark
/// or high contrast theme, and changes with it. Looking <c>TextFillColorSecondaryBrush</c> up in
/// <c>Application.Current.Resources</c> from code instead returns the app-level theme's colour once,
/// which is unreadable on a dark card. In XAML, set the brush with <c>{ThemeResource}</c> directly.
/// </summary>
internal static class SecondaryText
{
    private const string Markup =
        "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TextWrapping=\"Wrap\" "
        + "Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\" />";

    private const string CaptionMarkup =
        "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TextWrapping=\"Wrap\" "
        + "Style=\"{StaticResource CaptionTextBlockStyle}\" Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\" />";

    /// <summary>A wrapping block of secondary text in the body size.</summary>
    /// <param name="text">The text, already in the current language.</param>
    public static TextBlock Create(string text) => Load(Markup, text);

    /// <summary>A wrapping block of secondary text in the caption size, such as a path under a label.</summary>
    /// <param name="text">The text, already in the current language.</param>
    public static TextBlock Caption(string text) => Load(CaptionMarkup, text);

    private static TextBlock Load(string markup, string text)
    {
        var block = (TextBlock)XamlReader.Load(markup);
        block.Text = text;
        return block;
    }
}
