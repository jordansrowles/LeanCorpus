namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Tokeniser for common MediaWiki markup including headings, links, categories,
/// emphasis markup, citations, URLs, and plain body text.
/// </summary>
public sealed class MediaWikiTokeniser : ISpanTokeniser
{
    /// <summary>Token type emitted for MediaWiki categories.</summary>
    public const string CategoryType = "mediawiki.category";
    /// <summary>Token type emitted for MediaWiki internal links.</summary>
    public const string InternalLinkType = "mediawiki.internal-link";
    /// <summary>Token type emitted for MediaWiki external links.</summary>
    public const string ExternalLinkType = "mediawiki.external-link";
    /// <summary>Token type emitted for MediaWiki headings.</summary>
    public const string HeadingType = "mediawiki.heading";
    /// <summary>Token type emitted for MediaWiki bold markup.</summary>
    public const string BoldType = "mediawiki.bold";
    /// <summary>Token type emitted for MediaWiki italic markup.</summary>
    public const string ItalicType = "mediawiki.italic";
    /// <summary>Token type emitted for MediaWiki citations.</summary>
    public const string CitationType = "mediawiki.citation";

    private readonly Uax29UrlEmailTokeniser _plainTokeniser;
    private readonly IcuTokeniser _icuTokeniser;

    /// <summary>
    /// Initialises a new <see cref="MediaWikiTokeniser"/>.
    /// </summary>
    /// <param name="icuTokeniser">
    /// Optional ICU tokeniser used for tokenising content within markup blocks.
    /// When null, a default parameterless <see cref="IcuTokeniser"/> is used.
    /// </param>
    /// <param name="plainTokeniser">
    /// Optional tokeniser used for body text between markup blocks.
    /// When null, a default parameterless <see cref="Uax29UrlEmailTokeniser"/> is used.
    /// </param>
    public MediaWikiTokeniser(
        IcuTokeniser? icuTokeniser = null,
        Uax29UrlEmailTokeniser? plainTokeniser = null)
    {
        _plainTokeniser = plainTokeniser ?? new Uax29UrlEmailTokeniser();
        _icuTokeniser = icuTokeniser ?? new IcuTokeniser();
    }

    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        string text = input.ToString();
        int i = 0;

        while (i < text.Length)
        {
            if (TryReadHeading(text, ref i, sink)
                || TryReadTag(text, ref i, "<ref>", "</ref>", CitationType, sink)
                || TryReadQuoted(text, ref i, "'''", BoldType, sink)
                || TryReadQuoted(text, ref i, "''", ItalicType, sink)
                || TryReadCategory(text, ref i, sink)
                || TryReadInternalLink(text, ref i, sink)
                || TryReadExternalLink(text, ref i, sink))
            {
                continue;
            }

            int next = FindNextSpecial(text, i);
            if (next == i)
            {
                i++;
                continue;
            }

            _plainTokeniser.Tokenise(text.AsSpan(i, next - i), new UnicodeTokenisation.OffsetShiftingSink(sink, i));
            i = next;
        }
    }


    private bool TryReadHeading(string text, ref int index, ISpanTokenSink sink)
    {
        if (text[index] != '=' || (index > 0 && text[index - 1] != '\n'))
            return false;

        int markerLength = 0;
        while (index + markerLength < text.Length && text[index + markerLength] == '=' && markerLength < 6)
            markerLength++;
        if (markerLength < 2)
            return false;

        int lineEnd = text.IndexOf('\n', index);
        if (lineEnd < 0)
            lineEnd = text.Length;

        string closing = new('=', markerLength);
        int closingIndex = text.LastIndexOf(closing, lineEnd - 1, lineEnd - index, StringComparison.Ordinal);
        if (closingIndex <= index + markerLength)
            return false;

        int contentStart = index + markerLength;
        int contentEnd = closingIndex;
        while (contentStart < contentEnd && char.IsWhiteSpace(text[contentStart]))
            contentStart++;
        while (contentEnd > contentStart && char.IsWhiteSpace(text[contentEnd - 1]))
            contentEnd--;

        _icuTokeniser.Tokenise(text.AsSpan(contentStart, contentEnd - contentStart),
            new UnicodeTokenisation.OffsetShiftingSink(sink, contentStart, HeadingType));
        index = lineEnd;
        return true;
    }

    private bool TryReadQuoted(string text, ref int index, string delimiter, string type, ISpanTokenSink sink)
    {
        if (!text.AsSpan(index).StartsWith(delimiter, StringComparison.Ordinal))
            return false;

        int contentStart = index + delimiter.Length;
        int end = text.IndexOf(delimiter, contentStart, StringComparison.Ordinal);
        if (end < 0)
            return false;

        _icuTokeniser.Tokenise(text.AsSpan(contentStart, end - contentStart),
            new UnicodeTokenisation.OffsetShiftingSink(sink, contentStart, type));
        index = end + delimiter.Length;
        return true;
    }

    private bool TryReadTag(string text, ref int index, string startTag, string endTag, string type, ISpanTokenSink sink)
    {
        if (!text.AsSpan(index).StartsWith(startTag, StringComparison.OrdinalIgnoreCase))
            return false;

        int contentStart = index + startTag.Length;
        int end = text.IndexOf(endTag, contentStart, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
            return false;

        _icuTokeniser.Tokenise(text.AsSpan(contentStart, end - contentStart),
            new UnicodeTokenisation.OffsetShiftingSink(sink, contentStart, type));
        index = end + endTag.Length;
        return true;
    }

    private bool TryReadCategory(string text, ref int index, ISpanTokenSink sink)
    {
        const string prefix = "[[Category:";
        if (!text.AsSpan(index).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        int contentStart = index + prefix.Length;
        int end = text.IndexOf("]]", contentStart, StringComparison.Ordinal);
        if (end < 0)
            return false;

        int pipe = text.IndexOf('|', contentStart, end - contentStart);
        int labelStart = pipe >= 0 ? pipe + 1 : contentStart;
        _icuTokeniser.Tokenise(text.AsSpan(labelStart, end - labelStart),
            new UnicodeTokenisation.OffsetShiftingSink(sink, labelStart, CategoryType));
        index = end + 2;
        return true;
    }

    private bool TryReadInternalLink(string text, ref int index, ISpanTokenSink sink)
    {
        if (!text.AsSpan(index).StartsWith("[[", StringComparison.Ordinal))
            return false;

        int contentStart = index + 2;
        int end = text.IndexOf("]]", contentStart, StringComparison.Ordinal);
        if (end < 0)
            return false;

        int pipe = text.LastIndexOf('|', end - 1, end - contentStart);
        int labelStart = pipe >= contentStart ? pipe + 1 : contentStart;
        _icuTokeniser.Tokenise(text.AsSpan(labelStart, end - labelStart),
            new UnicodeTokenisation.OffsetShiftingSink(sink, labelStart, InternalLinkType));
        index = end + 2;
        return true;
    }

    private bool TryReadExternalLink(string text, ref int index, ISpanTokenSink sink)
    {
        if (text[index] != '[' || index + 1 >= text.Length || text[index + 1] == '[')
            return false;

        int contentStart = index + 1;
        if (!UnicodeTokenisation.TryReadUrl(text.AsSpan(), contentStart, out int urlEnd))
            return false;

        int closing = text.IndexOf(']', urlEnd);
        if (closing < 0)
            return false;

        sink.Add(text.AsSpan(contentStart, urlEnd - contentStart), contentStart, urlEnd, ExternalLinkType);
        if (urlEnd < closing && char.IsWhiteSpace(text[urlEnd]))
        {
            _plainTokeniser.Tokenise(text.AsSpan(urlEnd + 1, closing - urlEnd - 1),
                new UnicodeTokenisation.OffsetShiftingSink(sink, urlEnd + 1, ExternalLinkType));
        }

        index = closing + 1;
        return true;
    }

    private static int FindNextSpecial(string text, int start)
    {
        int next = text.Length;
        foreach (char marker in new[] { '[', '<', '\'', '=' })
        {
            int hit = text.IndexOf(marker, start);
            if (hit >= 0 && hit < next)
                next = hit;
        }

        return next;
    }
}

