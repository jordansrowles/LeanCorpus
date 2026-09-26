namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Strips HTML/XML tags and collapses HTML entities to whitespace while preserving source offset corrections.
/// </summary>
public sealed class HtmlStripCharFilter : ICharFilter
{
    /// <inheritdoc/>
    public CharFilterResult Filter(ReadOnlySpan<char> input)
    {
        if (input.IndexOfAny('<', '&') < 0)
            return new CharFilterResult(input.ToString());

        var output = new CharFilterResultBuilder(input.Length);
        int position = 0;
        while (position < input.Length)
        {
            int unchangedStart = position;
            while (position < input.Length && input[position] is not '<' and not '&')
                position++;
            if (position > unchangedStart)
                output.AppendUnchanged(input[unchangedStart..position], unchangedStart);
            if (position >= input.Length)
                break;

            if (input[position] == '<')
            {
                int sourceStart = position++;
                while (position < input.Length && input[position] != '>')
                    position++;
                if (position < input.Length)
                    position++;
                output.AppendReplacement(" ", sourceStart, position - sourceStart);
                continue;
            }

            int entityStart = position++;
            while (position < input.Length && IsWordChar(input[position]))
                position++;
            if (position < input.Length && input[position] == ';')
            {
                position++;
                output.AppendReplacement(" ", entityStart, position - entityStart);
            }
            else
            {
                output.AppendUnchanged(input[entityStart..(entityStart + 1)], entityStart);
                position = entityStart + 1;
            }
        }

        return output.Build();
    }

    private static bool IsWordChar(char c)
        => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';
}
