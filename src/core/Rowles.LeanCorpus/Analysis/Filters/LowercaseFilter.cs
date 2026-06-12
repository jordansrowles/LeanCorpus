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

    /// <inheritdoc/>

    /// <summary>
    /// Lowercases all characters in the provided character buffer in place.
    /// </summary>
    /// <param name="buffer">The character buffer to transform.</param>
    public void Apply(Span<char> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = char.ToLowerInvariant(buffer[i]);
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

        int uppercaseIndex = text.IndexOfAny(UppercaseLetters);
        if (uppercaseIndex < 0)
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
                for (int i = uppercaseIndex; i < text.Length; i++)
                    buf[i] = char.ToLowerInvariant(buf[i]);
                sink.Add(buf[..text.Length], startOffset, endOffset, type, positionIncrement, payload);
            }
            else
            {
                rentedArr = ArrayPool<char>.Shared.Rent(text.Length);
                Span<char> buf = rentedArr;
                text.CopyTo(buf);
                for (int i = uppercaseIndex; i < text.Length; i++)
                    buf[i] = char.ToLowerInvariant(buf[i]);
                sink.Add(buf[..text.Length], startOffset, endOffset, type, positionIncrement, payload);
            }
        }
        finally
        {
            if (rentedArr is not null) ArrayPool<char>.Shared.Return(rentedArr);
        }
    }
}
