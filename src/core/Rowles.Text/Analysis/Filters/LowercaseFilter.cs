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
    /// Unicode casing uses the same invariant path as the standard analyser.
    /// </summary>
    /// <param name="buffer">The character buffer to transform.</param>
    public void Apply(Span<char> buffer)
    {
        AsciiCharInspector.AsciiToLowerInPlace(buffer);
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

        if (AsciiCharInspector.IsAscii(text) && text.IndexOfAny(UppercaseLetters) < 0)
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
                AsciiCharInspector.AsciiToLower(text, buf);
                sink.Add(buf, startOffset, endOffset, type, positionIncrement, payload);
            }
            else
            {
                rentedArr = ArrayPool<char>.Shared.Rent(text.Length);
                Span<char> buf = rentedArr.AsSpan(0, text.Length);
                AsciiCharInspector.AsciiToLower(text, buf);
                sink.Add(buf[..text.Length], startOffset, endOffset, type, positionIncrement, payload);
            }
        }
        finally
        {
            if (rentedArr is not null) ArrayPool<char>.Shared.Return(rentedArr);
        }
    }
}
