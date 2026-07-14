using System.Buffers;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Reverses the characters in each token.
/// </summary>
public sealed class ReverseStringFilter : ISpanTokenFilter
{
    private const int StackallocThreshold = 128;

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
        if (text.Length <= StackallocThreshold)
        {
            Span<char> reversed = stackalloc char[text.Length];
            text.CopyTo(reversed);
            reversed.Reverse();
            sink.Add(reversed, startOffset, endOffset, type, positionIncrement, payload);
        }
        else
        {
            char[] rented = ArrayPool<char>.Shared.Rent(text.Length);
            try
            {
                text.CopyTo(rented);
                rented.AsSpan(0, text.Length).Reverse();
                sink.Add(rented.AsSpan(0, text.Length), startOffset, endOffset, type, positionIncrement, payload);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }
}
