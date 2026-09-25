using System.Net;
using System.Text;

namespace Rowles.DataForge.Workloads;

/// <summary>Produces the bounded, deterministic visible-text approximation used by Wikipedia v1.</summary>
public static class WikipediaTextNormaliserV1
{
    public const string Version = "wikipedia-visible-text-v1";
    public const int MaximumRawUtf8Bytes = 4 * 1024 * 1024;
    public const int MaximumOutputUtf8Bytes = 2 * 1024 * 1024;

    public static string Normalise(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (StrictUtf8ByteCount(input) > MaximumRawUtf8Bytes)
            throw new InvalidDataException("Raw wikitext exceeds the 4 MiB normalisation input bound.");

        var text = input.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        text = RemoveDelimitedBlocks(text, "<!--", "-->", 1, "comment", nested: false);
        text = RemoveReferences(text);
        text = RemoveDelimitedBlocks(text, "{|", "|}", 16, "table", nested: true);
        text = RemoveDelimitedBlocks(text, "{{", "}}", 64, "template", nested: true);
        text = ConvertInternalLinks(text);
        text = ConvertExternalLinks(text);
        text = StripTags(text);
        text = RemoveEmphasisMarkup(text);
        text = StripHeadings(text);
        text = WebUtility.HtmlDecode(text);
        text = NormaliseWhitespace(text);

        _ = StrictUtf8ByteCount(text);
        if (Encoding.UTF8.GetByteCount(text) > MaximumOutputUtf8Bytes)
            throw new InvalidDataException("Normalised Wikipedia text exceeds the 2 MiB output bound.");
        if (text.Contains('\r'))
            throw new InvalidDataException("Normalised text contains a carriage return.");
        return text;
    }

    public static bool TryNormalise(string input, out string text)
    {
        try
        {
            text = Normalise(input);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or EncoderFallbackException or ArgumentException)
        {
            text = string.Empty;
            return false;
        }
    }

    public static int StrictUtf8ByteCount(string value)
    {
        try
        {
            return new UTF8Encoding(false, true).GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidDataException("Text contains an unpaired UTF-16 surrogate.", exception);
        }
    }

    private static string RemoveDelimitedBlocks(string text, string open, string close, int maximumDepth, string name, bool nested)
    {
        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length;)
        {
            if (!Matches(text, index, open))
            {
                result.Append(text[index++]);
                continue;
            }

            var depth = 1;
            index += open.Length;
            while (index < text.Length && depth != 0)
            {
                if (nested && Matches(text, index, open))
                {
                    depth++;
                    if (depth > maximumDepth)
                        throw new InvalidDataException($"Wikipedia {name} nesting exceeds {maximumDepth} levels.");
                    index += open.Length;
                }
                else if (Matches(text, index, close))
                {
                    depth--;
                    index += close.Length;
                }
                else
                {
                    index++;
                }
            }
            if (depth != 0)
                throw new InvalidDataException($"Unclosed Wikipedia {name} block.");
        }
        return result.ToString();
    }

    private static string RemoveReferences(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length;)
        {
            if (!TryReadTag(text, index, out var tag) || !tag.Name.Equals("ref", StringComparison.OrdinalIgnoreCase) || tag.IsClosing)
            {
                result.Append(text[index++]);
                continue;
            }

            index = tag.EndExclusive;
            if (tag.IsSelfClosing)
                continue;

            var closeStart = -1;
            var cursor = index;
            while (cursor < text.Length)
            {
                var next = text.IndexOf('<', cursor);
                if (next < 0 || !TryReadTag(text, next, out var nestedTag))
                    break;
                if (nestedTag.Name.Equals("ref", StringComparison.OrdinalIgnoreCase))
                {
                    if (!nestedTag.IsClosing)
                        throw new InvalidDataException("Nested Wikipedia ref elements are not supported.");
                    closeStart = next;
                    index = nestedTag.EndExclusive;
                    break;
                }
                cursor = nestedTag.EndExclusive;
            }
            if (closeStart < 0)
                throw new InvalidDataException("Unclosed Wikipedia ref element.");
        }
        return result.ToString();
    }

    private static string ConvertInternalLinks(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length;)
        {
            if (!Matches(text, index, "[["))
            {
                result.Append(text[index++]);
                continue;
            }

            var end = text.IndexOf("]]", index + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                result.Append("[[");
                index += 2;
                continue;
            }

            var body = text.AsSpan(index + 2, end - index - 2);
            var separator = body.LastIndexOf('|');
            var target = separator < 0 ? body.ToString() : body[..separator].ToString();
            var label = separator < 0 ? null : body[(separator + 1)..].ToString();
            var isMediaTarget = target.StartsWith("File:", StringComparison.OrdinalIgnoreCase) ||
                                target.StartsWith("Image:", StringComparison.OrdinalIgnoreCase) ||
                                target.StartsWith("Category:", StringComparison.OrdinalIgnoreCase);
            if (!isMediaTarget || label is not null)
                result.Append(label ?? target);
            index = end + 2;
        }
        return result.ToString();
    }

    private static string ConvertExternalLinks(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length;)
        {
            if (text[index] != '[' || !(StartsWith(text, index + 1, "http://") || StartsWith(text, index + 1, "https://")))
            {
                result.Append(text[index++]);
                continue;
            }

            var end = text.IndexOf(']', index + 1);
            if (end < 0)
            {
                result.Append(text[index++]);
                continue;
            }
            var bodyStart = index + 1;
            var labelStart = text.IndexOfAny([' ', '\t', '\n'], bodyStart, end - bodyStart);
            if (labelStart >= 0)
                result.Append(text.AsSpan(labelStart + 1, end - labelStart - 1));
            index = end + 1;
        }
        return result.ToString();
    }

    private static string StripTags(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length;)
        {
            if (text[index] != '<' || !TryReadTag(text, index, out var tag))
            {
                result.Append(text[index++]);
                continue;
            }

            if (tag.Name.Equals("br", StringComparison.OrdinalIgnoreCase) ||
                tag.Name.Equals("p", StringComparison.OrdinalIgnoreCase) ||
                tag.Name.Equals("li", StringComparison.OrdinalIgnoreCase))
                result.Append('\n');
            index = tag.EndExclusive;
        }
        return result.ToString();
    }

    private static string RemoveEmphasisMarkup(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length;)
        {
            if (text[index] != '\'')
            {
                result.Append(text[index++]);
                continue;
            }
            var end = index + 1;
            while (end < text.Length && text[end] == '\'')
                end++;
            var count = end - index;
            if (count is not (2 or 3 or 5))
                result.Append(text.AsSpan(index, count));
            index = end;
        }
        return result.ToString();
    }

    private static string StripHeadings(string text)
    {
        var result = new StringBuilder(text.Length);
        var start = 0;
        while (start <= text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
                end = text.Length;
            var line = text.AsSpan(start, end - start);
            var left = 0;
            while (left < line.Length && char.IsWhiteSpace(line[left]) && line[left] != '\n') left++;
            var right = line.Length;
            while (right > left && char.IsWhiteSpace(line[right - 1]) && line[right - 1] != '\n') right--;
            var trimmed = line[left..right];
            var opening = 0;
            while (opening < trimmed.Length && trimmed[opening] == '=') opening++;
            var closing = 0;
            while (closing < trimmed.Length - opening && trimmed[trimmed.Length - closing - 1] == '=') closing++;
            if (opening >= 2 && opening == closing)
            {
                result.Append(line[..left]);
                result.Append(trimmed[opening..^closing]);
                result.Append(line[right..]);
            }
            else
            {
                result.Append(line);
            }
            if (end == text.Length)
                break;
            result.Append('\n');
            start = end + 1;
        }
        return result.ToString();
    }

    private static string NormaliseWhitespace(string text)
    {
        var cleaned = new StringBuilder(text.Length);
        var horizontalSpace = false;
        var newlines = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\n')
            {
                horizontalSpace = false;
                newlines++;
                if (newlines <= 2)
                    cleaned.Append('\n');
                continue;
            }
            newlines = 0;
            if (character == '\t' || (char.IsWhiteSpace(character) && character != '\r') || char.IsControl(character))
            {
                if (!horizontalSpace)
                    cleaned.Append(' ');
                horizontalSpace = true;
                continue;
            }
            cleaned.Append(character);
            horizontalSpace = false;
        }

        var output = new StringBuilder(cleaned.Length);
        var start = 0;
        while (start <= cleaned.Length)
        {
            var end = IndexOf(cleaned, '\n', start);
            if (end < 0)
                end = cleaned.Length;
            var line = cleaned.ToString(start, end - start).Trim(' ');
            output.Append(line);
            if (end == cleaned.Length)
                break;
            output.Append('\n');
            start = end + 1;
        }
        return output.ToString().Trim();
    }

    private static bool TryReadTag(string text, int start, out ParsedTag tag)
    {
        tag = default;
        if (start >= text.Length || text[start] != '<' || start + 1 >= text.Length)
            return false;
        var cursor = start + 1;
        var closing = cursor < text.Length && text[cursor] == '/';
        if (closing) cursor++;
        if (cursor >= text.Length || !IsTagNameStart(text[cursor]))
            return false;
        var nameStart = cursor++;
        while (cursor < text.Length && IsTagNameCharacter(text[cursor])) cursor++;
        var name = text[nameStart..cursor];
        char quote = '\0';
        for (; cursor < text.Length; cursor++)
        {
            var current = text[cursor];
            if (quote != '\0')
            {
                if (current == quote) quote = '\0';
            }
            else if (current is '\'' or '"')
            {
                quote = current;
            }
            else if (current == '>')
            {
                var beforeEnd = cursor - 1;
                while (beforeEnd >= nameStart && char.IsWhiteSpace(text[beforeEnd])) beforeEnd--;
                tag = new ParsedTag(name, closing, beforeEnd >= 0 && text[beforeEnd] == '/', cursor + 1);
                return true;
            }
        }
        return false;
    }

    private static bool IsTagNameStart(char value) => char.IsAsciiLetter(value);
    private static bool IsTagNameCharacter(char value) => char.IsAsciiLetterOrDigit(value) || value is ':' or '-';
    private static bool Matches(string text, int index, string value) => index + value.Length <= text.Length && text.AsSpan(index, value.Length).SequenceEqual(value);
    private static bool StartsWith(string text, int index, string value) => index + value.Length <= text.Length && text.AsSpan(index, value.Length).Equals(value, StringComparison.OrdinalIgnoreCase);
    private static int IndexOf(StringBuilder builder, char value, int start)
    {
        for (var index = start; index < builder.Length; index++)
            if (builder[index] == value)
                return index;
        return -1;
    }

    private readonly record struct ParsedTag(string Name, bool IsClosing, bool IsSelfClosing, int EndExclusive);
}

public enum WikipediaEligibilityRejection
{
    None,
    NonMainNamespace,
    Redirect,
    MissingRevision,
    MissingText,
    RawTooSmall,
    RawTooLarge,
    NormalisationFailed,
    NormalisedTooSmall,
    NormalisedTooLarge,
    TooFewTokens
}

public sealed record WikipediaEligibilityResult(WikipediaEligibilityRejection Rejection, string? Text, int RawUtf8Bytes, int TextUtf8Bytes, int TokenCount)
{
    public bool IsEligible => Rejection == WikipediaEligibilityRejection.None;
}

/// <summary>Applies the immutable eligibility thresholds for Wikipedia reference v1.</summary>
public static class WikipediaEligibility
{
    public static WikipediaEligibilityResult Evaluate(int namespaceId, ulong pageId, bool hasRedirect, ulong revisionId,
        DateTimeOffset? revisionTimestamp, string? rawText)
    {
        if (namespaceId != 0) return Rejected(WikipediaEligibilityRejection.NonMainNamespace);
        if (pageId == 0) return Rejected(WikipediaEligibilityRejection.MissingRevision);
        if (hasRedirect) return Rejected(WikipediaEligibilityRejection.Redirect);
        if (revisionId == 0 || revisionTimestamp is null || revisionTimestamp.Value.Offset != TimeSpan.Zero)
            return Rejected(WikipediaEligibilityRejection.MissingRevision);
        if (rawText is null) return Rejected(WikipediaEligibilityRejection.MissingText);

        int rawBytes;
        try { rawBytes = WikipediaTextNormaliserV1.StrictUtf8ByteCount(rawText); }
        catch (InvalidDataException) { return Rejected(WikipediaEligibilityRejection.NormalisationFailed); }
        if (rawBytes < 256) return Rejected(WikipediaEligibilityRejection.RawTooSmall, rawBytes);
        if (rawBytes > WikipediaTextNormaliserV1.MaximumRawUtf8Bytes) return Rejected(WikipediaEligibilityRejection.RawTooLarge, rawBytes);
        if (!WikipediaTextNormaliserV1.TryNormalise(rawText, out var text))
            return Rejected(WikipediaEligibilityRejection.NormalisationFailed, rawBytes);
        var textBytes = Encoding.UTF8.GetByteCount(text);
        if (textBytes < 256) return new(WikipediaEligibilityRejection.NormalisedTooSmall, text, rawBytes, textBytes, 0);
        if (textBytes > WikipediaTextNormaliserV1.MaximumOutputUtf8Bytes) return new(WikipediaEligibilityRejection.NormalisedTooLarge, text, rawBytes, textBytes, 0);
        var tokenCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (tokenCount < 40) return new(WikipediaEligibilityRejection.TooFewTokens, text, rawBytes, textBytes, tokenCount);
        return new(WikipediaEligibilityRejection.None, text, rawBytes, textBytes, tokenCount);
    }

    private static WikipediaEligibilityResult Rejected(WikipediaEligibilityRejection reason, int rawBytes = 0) =>
        new(reason, null, rawBytes, 0, 0);
}
