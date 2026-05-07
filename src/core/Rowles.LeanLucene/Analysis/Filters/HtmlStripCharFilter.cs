using System.Text.RegularExpressions;

namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Strips HTML/XML tags from input text, leaving only the text content.
/// </summary>
public sealed partial class HtmlStripCharFilter : ICharFilter
{
    /// <inheritdoc/>
    public string Filter(ReadOnlySpan<char> input)
    {
        var text = input.ToString();
        text = TagPattern().Replace(text, " ");
        text = EntityPattern().Replace(text, " ");
        return text;
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"&\w+;")]
    private static partial Regex EntityPattern();
}
