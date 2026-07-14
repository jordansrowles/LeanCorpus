using System.Buffers;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Strips HTML/XML tags and HTML entities from input text, leaving only text content.
/// Uses a span-based scanner to avoid regex allocations.
/// </summary>
public sealed class HtmlStripCharFilter : ICharFilter
{
    /// <inheritdoc/>
    public string Filter(ReadOnlySpan<char> input)
    {
        if (input.IsEmpty)
            return string.Empty;

        const int StackThreshold = 256;
        char[]? rented = null;
        try
        {
            Span<char> buf = input.Length <= StackThreshold
                ? stackalloc char[input.Length]
                : (rented = ArrayPool<char>.Shared.Rent(input.Length));

            int len = StripHtml(input, buf);

            if (len == 0)
                return string.Empty;
            return new string(buf[..len]);
        }
        finally
        {
            if (rented is not null) ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static int StripHtml(ReadOnlySpan<char> input, Span<char> output)
    {
        int outPos = 0;
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            if (c == '<')
            {
                while (i < input.Length && input[i] != '>')
                    i++;
                if (i < input.Length) i++;
                if (outPos < output.Length)
                    output[outPos++] = ' ';
            }
            else if (c == '&')
            {
                int start = i;
                i++;
                while (i < input.Length && IsWordChar(input[i]))
                    i++;
                if (i < input.Length && input[i] == ';')
                {
                    i++;
                    if (outPos < output.Length)
                        output[outPos++] = ' ';
                }
                else
                {
                    i = start + 1;
                    output[outPos++] = '&';
                }
            }
            else
            {
                output[outPos++] = c;
                i++;
            }
        }
        return outPos;
    }

    private static bool IsWordChar(char c)
        => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';
}
