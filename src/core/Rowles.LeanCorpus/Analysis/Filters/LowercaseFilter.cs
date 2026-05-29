using System.Buffers;
using Rowles.LeanCorpus.Analysis;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Performs an in-place lowercase transformation on tokens or a character buffer.
/// </summary>
public sealed class LowercaseFilter : ITokenFilter, ISpanTokenFilter
{

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            var lower = t.Text.ToLowerInvariant();
            if (!ReferenceEquals(lower, t.Text))
                tokens[i] = t.WithText(lower);
        }
    }

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

        int uppercaseIndex = IndexOfUppercase(text);
        if (uppercaseIndex < 0)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        char[] buf = ArrayPool<char>.Shared.Rent(text.Length);
        try
        {
            text.CopyTo(buf);
            for (int i = uppercaseIndex; i < text.Length; i++)
                buf[i] = char.ToLowerInvariant(buf[i]);

            sink.Add(buf.AsSpan(0, text.Length), startOffset, endOffset, type, positionIncrement, payload);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buf);
        }
    }

    private static int IndexOfUppercase(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsUpper(text[i]))
                return i;
        }

        return -1;
    }
}
