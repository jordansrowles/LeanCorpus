namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Splits input text into letter-only tokens, discarding digits and punctuation.
/// </summary>
public sealed class LetterTokeniser : ISpanTokeniser
{
    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        int i = 0;
        while (i < input.Length)
        {
            if (!char.IsLetter(input[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < input.Length && char.IsLetter(input[i]))
                i++;

            sink.Add(input[start..i], start, i);
        }
    }

    /// <summary>
    /// Emits letter-only token offsets into the supplied list without materialising token text.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    /// <param name="offsets">The list to populate. Cleared before use.</param>
    internal void TokeniseOffsets(ReadOnlySpan<char> input, List<(int Start, int End)> offsets)
    {
        offsets.Clear();
        int i = 0;

        while (i < input.Length)
        {
            if (!char.IsLetter(input[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < input.Length && char.IsLetter(input[i]))
                i++;

            offsets.Add((start, i));
        }
    }
}
