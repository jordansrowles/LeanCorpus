using System.Buffers;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Removes possessive endings and periods from acronyms, replicating Lucene's ClassicFilter.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Strips trailing <c>'s</c> or <c>'S</c> from tokens (English possessive).</item>
///   <item>Removes periods from tokens that consist entirely of uppercase letters and periods
///         (e.g. <c>"U.S.A."</c> becomes <c>"USA"</c>).</item>
///   <item>Tokens that require neither transformation are forwarded unchanged with zero allocation.</item>
/// </list>
/// </remarks>
public sealed class ClassicFilter : ISpanTokenFilter
{
    // SearchValues for fast-path detection on the common '.' and '\'' characters.
    private static readonly SearchValues<char> ClassicChars =
        SearchValues.Create(".'\u2019");

    /// <inheritdoc/>
    public void Apply(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        byte[]? payload,
        ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        // Fast path: if the token contains neither apostrophe nor period, forward unchanged.
        if (text.IndexOfAny(ClassicChars) < 0)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        // Determine whether we need to strip a possessive suffix.
        // Handles both singular ("dog's" → "dog") and plural ("dogs'" → "dogs").
        bool hasPossessive = false;
        ReadOnlySpan<char> possessiveStripped = text;

        if (text.Length >= 2)
        {
            char last = text[^1];
            char secondLast = text[^2];

            // Singular possessive: ends with 's or 'S
            if ((last == 's' || last == 'S')
                && (secondLast == '\'' || secondLast == '\u2019'))
            {
                hasPossessive = true;
                possessiveStripped = text[..^2];
            }
            // Plural possessive: ends with ' or right-single-quote
            else if (last == '\'' || last == '\u2019')
            {
                hasPossessive = true;
                possessiveStripped = text[..^1];
            }
        }

        // The span we'll check for acronym-ness (after stripping possessive if present).
        ReadOnlySpan<char> checkSpan = possessiveStripped;

        // Determine whether to strip periods from an acronym.
        bool isAcronym = checkSpan.Length >= 2 && IsAcronym(checkSpan);

        // If neither transformation applies, forward unchanged (period might be from
        // something like "v1.2" which is not an acronym).
        if (!hasPossessive && !isAcronym)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        // We need a buffer. Compute output length.
        int outputLength = checkSpan.Length;
        if (isAcronym)
        {
            foreach (char c in checkSpan)
            {
                if (c == '.')
                    outputLength--;
            }
        }

        // Sink the transformed text.
        const int StackThreshold = 128;
        char[]? rented = null;
        try
        {
            Span<char> buffer = outputLength <= StackThreshold
                ? stackalloc char[outputLength]
                : (rented = ArrayPool<char>.Shared.Rent(outputLength)).AsSpan(0, outputLength);

            CopyWithoutPeriods(checkSpan, buffer);
            sink.Add(buffer, startOffset, endOffset, type, positionIncrement, payload);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/> into <paramref name="dest"/> omitting <c>'.'</c> characters.
    /// The caller guarantees <paramref name="dest"/> has exactly the right capacity for
    /// non-period characters.
    /// </summary>
    private static void CopyWithoutPeriods(ReadOnlySpan<char> source, Span<char> dest)
    {
        int d = 0;
        foreach (char c in source)
        {
            if (c != '.')
                dest[d++] = c;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="text"/> consists solely of uppercase
    /// ASCII letters and <c>'.'</c>, with at least one period.
    /// </summary>
    private static bool IsAcronym(ReadOnlySpan<char> text)
    {
        bool hasPeriod = false;

        foreach (char c in text)
        {
            if (c == '.')
            {
                hasPeriod = true;
            }
            else if (c is < 'A' or > 'Z')
            {
                return false;
            }
        }

        return hasPeriod;
    }
}
