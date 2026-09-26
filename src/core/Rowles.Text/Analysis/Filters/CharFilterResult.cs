using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Holds transformed text and the map from its UTF-16 offsets back to the source text.
/// </summary>
public sealed class CharFilterResult
{
    /// <summary>Creates an unchanged result whose UTF-16 offsets map to themselves.</summary>
    /// <param name="text">The source text.</param>
    public CharFilterResult(string text)
        : this(text, OffsetCorrectionMap.Identity(RequireText(text).Length))
    {
    }

    /// <summary>Creates a transformed result with an explicit map to its source text.</summary>
    /// <param name="text">The transformed text.</param>
    /// <param name="offsetCorrections">The map from this text's offsets to its input offsets.</param>
    /// <exception cref="ArgumentException">The map output length does not match the transformed text length.</exception>
    public CharFilterResult(string text, OffsetCorrectionMap offsetCorrections)
    {
        Text = RequireText(text);
        OffsetCorrections = offsetCorrections ?? throw new ArgumentNullException(nameof(offsetCorrections));
        if (OffsetCorrections.OutputLength != Text.Length)
            throw new ArgumentException("The offset map output length must match the transformed text length.", nameof(offsetCorrections));
    }

    /// <summary>Gets the transformed text.</summary>
    public string Text { get; }

    /// <summary>
    /// Gets the map from offsets in <see cref="Text"/> to UTF-16 offsets in the original source text.
    /// </summary>
    public OffsetCorrectionMap OffsetCorrections { get; }

    /// <summary>Applies another character filter and composes its offset map with this result.</summary>
    /// <param name="filter">The next filter in the pipeline.</param>
    /// <returns>The next transformed text mapped directly to the original source.</returns>
    public CharFilterResult Apply(ICharFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        CharFilterResult next = filter.Filter(Text.AsSpan());
        if (next.OffsetCorrections.InputLength != Text.Length)
            throw new InvalidOperationException("The char filter returned an offset map for a different input length.");
        return new CharFilterResult(next.Text, next.OffsetCorrections.Compose(OffsetCorrections));
    }

    /// <summary>
    /// Analyses this transformed text and sends tokens with offsets corrected to the original source.
    /// </summary>
    /// <param name="analyser">The analyser to run over the transformed text.</param>
    /// <param name="sink">The sink that receives tokens and original-input UTF-16 offsets.</param>
    public void Analyse(IAnalyser analyser, ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(analyser);
        ArgumentNullException.ThrowIfNull(sink);
        if (OffsetCorrections.IsIdentity)
        {
            analyser.Analyse(Text.AsSpan(), sink);
            return;
        }

        analyser.Analyse(Text.AsSpan(), new OffsetCorrectingTokenSink(sink, OffsetCorrections));
    }

    internal CharFilterResult ReplaceOrdinal(string oldValue, string? newValue)
    {
        CharFilterResult next = ReplaceOrdinalText(Text, oldValue, newValue);
        return new CharFilterResult(next.Text, next.OffsetCorrections.Compose(OffsetCorrections));
    }

    internal static CharFilterResult ReplaceOrdinalText(string input, string oldValue, string? newValue)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(oldValue);
        if (oldValue.Length == 0)
            throw new ArgumentException("The value cannot be an empty string.", nameof(oldValue));

        newValue ??= string.Empty;
        int matchIndex = input.IndexOf(oldValue, 0, StringComparison.Ordinal);
        if (matchIndex < 0)
            return new CharFilterResult(input);

        var builder = new CharFilterResultBuilder(input.Length);
        int sourceOffset = 0;
        while (matchIndex >= 0)
        {
            builder.AppendUnchanged(input.AsSpan(sourceOffset, matchIndex - sourceOffset), sourceOffset);
            builder.AppendReplacement(newValue.AsSpan(), matchIndex, oldValue.Length);
            sourceOffset = matchIndex + oldValue.Length;
            matchIndex = input.IndexOf(oldValue, sourceOffset, StringComparison.Ordinal);
        }
        builder.AppendUnchanged(input.AsSpan(sourceOffset), sourceOffset);
        return builder.Build();
    }

    private static string RequireText(string text)
        => text ?? throw new ArgumentNullException(nameof(text));

    private sealed class OffsetCorrectingTokenSink(ISpanTokenSink destination, OffsetCorrectionMap corrections) : ISpanTokenSink
    {
        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            destination.Add(text,
                corrections.CorrectStartOffset(startOffset),
                corrections.CorrectEndOffset(endOffset),
                type,
                positionIncrement,
                payload);
        }

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type,
            int positionIncrement,
            int positionLength,
            byte[]? payload)
        {
            destination.Add(text,
                corrections.CorrectStartOffset(startOffset),
                corrections.CorrectEndOffset(endOffset),
                type,
                positionIncrement,
                positionLength,
                payload);
        }
    }
}
