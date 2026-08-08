using System.Buffers;
using Rowles.LeanCorpus.Analysis;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Performs an in-place lowercase transformation on tokens or a character buffer.
/// </summary>
public sealed class LowercaseFilter : ISpanTokenFilter
{
    // SIMD-accelerated search values for uppercase ASCII letters A-Z.
    private static readonly System.Buffers.SearchValues<char> UppercaseLetters =
        System.Buffers.SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZ");

    /// <summary>
    /// Lowercases all characters in the provided character buffer in place.
    /// Handles both ASCII and non-ASCII uppercase (diacritic capitals like Č, Š, Ž).
    /// </summary>
    /// <param name="buffer">The character buffer to transform.</param>
    public void Apply(Span<char> buffer)
    {
        AsciiCharInspector.AsciiToLowerInPlace(buffer);
        // Second pass: catch non-ASCII uppercase missed by the SIMD A-Z path.
        for (int i = 0; i < buffer.Length; i++)
        {
            if (char.IsUpper(buffer[i]))
                buffer[i] = char.ToLowerInvariant(buffer[i]);
        }
    }

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

        int upperIndex = text.IndexOfAny(UppercaseLetters);
        ReadOnlySpan<char> prefix = upperIndex < 0 ? text : text[..upperIndex];
        int nonAsciiUpperIndex = IndexOfNonAsciiUpper(prefix);
        if (nonAsciiUpperIndex >= 0)
            upperIndex = nonAsciiUpperIndex;

        if (upperIndex < 0)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        const int StackThreshold = 128;
        char[]? rentedArr = null;
        try
        {
            if (text.Length <= StackThreshold)
            {
                Span<char> buf = stackalloc char[text.Length];
                text.CopyTo(buf);
                for (int i = upperIndex; i < text.Length; i++)
                    buf[i] = char.ToLowerInvariant(buf[i]);
                sink.Add(buf[..text.Length], startOffset, endOffset, type, positionIncrement, payload);
            }
            else
            {
                rentedArr = ArrayPool<char>.Shared.Rent(text.Length);
                Span<char> buf = rentedArr;
                text.CopyTo(buf);
                for (int i = upperIndex; i < text.Length; i++)
                    buf[i] = char.ToLowerInvariant(buf[i]);
                sink.Add(buf[..text.Length], startOffset, endOffset, type, positionIncrement, payload);
            }
        }
        finally
        {
            if (rentedArr is not null) ArrayPool<char>.Shared.Return(rentedArr);
        }
    }

    /// <summary>
    /// Returns the index of the first non-ASCII character classified as Unicode
    /// uppercase, or -1 if none are found.
    /// </summary>
    private static int IndexOfNonAsciiUpper(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
            if (text[i] > 0x7F && char.IsUpper(text[i]))
                return i;
        return -1;
    }
}
