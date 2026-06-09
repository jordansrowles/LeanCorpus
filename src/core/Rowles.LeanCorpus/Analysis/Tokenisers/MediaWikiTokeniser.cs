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
        var tokens = new List<Token>();
        int i = 0;

        while (i < text.Length)
        {
            if (TryReadHeading(text, ref i, tokens)
                || TryReadTag(text, ref i, "<ref>", "</ref>", CitationType, tokens)
                || TryReadQuoted(text, ref i, "'''", BoldType, tokens)
                || TryReadQuoted(text, ref i, "''", ItalicType, tokens)
                || TryReadCategory(text, ref i, tokens)
                || TryReadInternalLink(text, ref i, tokens)
                || TryReadExternalLink(text, ref i, tokens))
            {
                continue;
            }

            int next = FindNextSpecial(text, i);
            if (next == i)
            {
                i++;
                continue;
            }

            var collector = new ListSink();
            _plainTokeniser.Tokenise(text.AsSpan(i, next - i), collector);
            UnicodeTokenisation.AddShiftedTokens(tokens, collector.Tokens, i, null);
            i = next;
        }

        for (int ti = 0; ti < tokens.Count; ti++)
        {
            var t = tokens[ti];
            sink.Add(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload);
        }
    }


    private bool TryReadHeading(string text, ref int index, List<Token> tokens)
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

            var collector = new ListSink();
            _icuTokeniser.Tokenise(text.AsSpan(contentStart, contentEnd - contentStart), collector);
            UnicodeTokenisation.AddShiftedTokens(tokens, collector.Tokens, contentStart, HeadingType);
        index = lineEnd;
        return true;
    }

    private bool TryReadQuoted(string text, ref int index, string delimiter, string type, List<Token> tokens)
    {
        if (!text.AsSpan(index).StartsWith(delimiter, StringComparison.Ordinal))
            return false;

        int contentStart = index + delimiter.Length;
        int end = text.IndexOf(delimiter, contentStart, StringComparison.Ordinal);
        if (end < 0)
            return false;

            var collector = new ListSink();
            _icuTokeniser.Tokenise(text.AsSpan(contentStart, end - contentStart), collector);
            UnicodeTokenisation.AddShiftedTokens(tokens, collector.Tokens, contentStart, type);
        index = end + delimiter.Length;
        return true;
    }

    private bool TryReadTag(string text, ref int index, string startTag, string endTag, string type, List<Token> tokens)
    {
        if (!text.AsSpan(index).StartsWith(startTag, StringComparison.OrdinalIgnoreCase))
            return false;

        int contentStart = index + startTag.Length;
        int end = text.IndexOf(endTag, contentStart, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
            return false;

            var collector = new ListSink();
            _icuTokeniser.Tokenise(text.AsSpan(contentStart, end - contentStart), collector);
            UnicodeTokenisation.AddShiftedTokens(tokens, collector.Tokens, contentStart, type);
        index = end + endTag.Length;
        return true;
    }

    private bool TryReadCategory(string text, ref int index, List<Token> tokens)
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
            var collector = new ListSink();
            _icuTokeniser.Tokenise(text.AsSpan(labelStart, end - labelStart), collector);
            UnicodeTokenisation.AddShiftedTokens(tokens, collector.Tokens, labelStart, CategoryType);
        index = end + 2;
        return true;
    }

    private bool TryReadInternalLink(string text, ref int index, List<Token> tokens)
    {
        if (!text.AsSpan(index).StartsWith("[[", StringComparison.Ordinal))
            return false;

        int contentStart = index + 2;
        int end = text.IndexOf("]]", contentStart, StringComparison.Ordinal);
        if (end < 0)
            return false;

        int pipe = text.LastIndexOf('|', end - 1, end - contentStart);
        int labelStart = pipe >= contentStart ? pipe + 1 : contentStart;
            var collector = new ListSink();
            _icuTokeniser.Tokenise(text.AsSpan(labelStart, end - labelStart), collector);
            UnicodeTokenisation.AddShiftedTokens(tokens, collector.Tokens, labelStart, InternalLinkType);
        index = end + 2;
        return true;
    }

    private bool TryReadExternalLink(string text, ref int index, List<Token> tokens)
    {
        if (text[index] != '[' || index + 1 >= text.Length || text[index + 1] == '[')
            return false;

        int contentStart = index + 1;
        if (!UnicodeTokenisation.TryReadUrl(text.AsSpan(), contentStart, out int urlEnd))
            return false;

        int closing = text.IndexOf(']', urlEnd);
        if (closing < 0)
            return false;

        tokens.Add(new Token(text[contentStart..urlEnd], contentStart, urlEnd, ExternalLinkType));
        if (urlEnd < closing && char.IsWhiteSpace(text[urlEnd]))
        {
                var collector = new ListSink();
                _plainTokeniser.Tokenise(text.AsSpan(urlEnd + 1, closing - urlEnd - 1), collector);
                UnicodeTokenisation.AddShiftedTokens(tokens, collector.Tokens, urlEnd + 1, ExternalLinkType);
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

    private sealed class ListSink : ISpanTokenSink
    {
        public readonly List<Token> Tokens = new();
        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type, int positionIncrement, byte[]? payload)
        {
            Tokens.Add(new Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
        }
    }
}

