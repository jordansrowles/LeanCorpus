namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Splits compound tokens on delimiters, case changes, and letter-number boundaries.
/// </summary>
public sealed class WordDelimiterFilter : ITokenFilter
{
    private readonly bool _splitOnCaseChange;
    private readonly bool _splitOnNumerics;
    private readonly bool _preserveOriginal;
    private readonly bool _concatenateWords;
    private readonly bool _concatenateNumbers;

    /// <summary>
    /// Initialises a new <see cref="WordDelimiterFilter"/>.
    /// </summary>
    /// <param name="splitOnCaseChange">Whether lower-to-upper and acronym-word boundaries should split.</param>
    /// <param name="splitOnNumerics">Whether letter-number boundaries should split.</param>
    /// <param name="preserveOriginal">Whether the original token should be retained when it is split.</param>
    /// <param name="concatenateWords">Whether letter parts should be concatenated into an additional token.</param>
    /// <param name="concatenateNumbers">Whether numeric parts should be concatenated into an additional token.</param>
    public WordDelimiterFilter(
        bool splitOnCaseChange = true,
        bool splitOnNumerics = true,
        bool preserveOriginal = false,
        bool concatenateWords = false,
        bool concatenateNumbers = false)
    {
        _splitOnCaseChange = splitOnCaseChange;
        _splitOnNumerics = splitOnNumerics;
        _preserveOriginal = preserveOriginal;
        _concatenateWords = concatenateWords;
        _concatenateNumbers = concatenateNumbers;
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        if (tokens.Count == 0)
            return;

        var result = new List<Token>(tokens.Count);
        var parts = new List<Part>(4);

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            parts.Clear();
            Split(token.Text, parts);

            if (parts.Count == 0)
                continue;

            bool unchanged = parts.Count == 1 && parts[0].Start == 0 && parts[0].End == token.Text.Length;
            if (unchanged)
            {
                result.Add(token);
                continue;
            }

            if (_preserveOriginal)
                result.Add(token);

            for (int j = 0; j < parts.Count; j++)
                result.Add(CreatePartToken(token, parts[j]));

            if (_concatenateWords)
                AddConcatenation(result, token, parts, PartKind.Word);

            if (_concatenateNumbers)
                AddConcatenation(result, token, parts, PartKind.Number);
        }

        tokens.Clear();
        tokens.AddRange(result);
    }

    private void Split(string text, List<Part> parts)
    {
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && IsDelimiter(text[i]))
                i++;

            if (i >= text.Length)
                break;

            int start = i;
            PartKind kind = char.IsDigit(text[i]) ? PartKind.Number : PartKind.Word;
            i++;

            while (i < text.Length && !IsDelimiter(text[i]) && !IsBoundary(text, i, kind))
                i++;

            parts.Add(new Part(start, i, kind));
        }
    }

    private bool IsBoundary(string text, int index, PartKind currentKind)
    {
        char previous = text[index - 1];
        char current = text[index];

        if (_splitOnNumerics && char.IsDigit(previous) != char.IsDigit(current))
            return true;

        if (!_splitOnCaseChange || currentKind == PartKind.Number)
            return false;

        if (char.IsLower(previous) && char.IsUpper(current))
            return true;

        return char.IsUpper(previous)
            && char.IsUpper(current)
            && index + 1 < text.Length
            && char.IsLower(text[index + 1]);
    }

    private static bool IsDelimiter(char c) => !char.IsLetterOrDigit(c);

    private static Token CreatePartToken(Token source, Part part)
    {
        string text = source.Text[part.Start..part.End];
        return new Token(text, source.StartOffset + part.Start, source.StartOffset + part.End);
    }

    private static void AddConcatenation(List<Token> result, Token source, List<Part> parts, PartKind kind)
    {
        int first = -1;
        int last = -1;
        int count = 0;
        int length = 0;

        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Kind != kind)
                continue;

            if (first < 0)
                first = i;
            last = i;
            count++;
            length += parts[i].End - parts[i].Start;
        }

        if (count < 2 || first < 0 || last < 0)
            return;

        string text = string.Create(length, (source.Text, parts, kind), static (buffer, state) =>
        {
            int offset = 0;
            for (int i = 0; i < state.parts.Count; i++)
            {
                var part = state.parts[i];
                if (part.Kind != state.kind)
                    continue;

                state.Text.AsSpan(part.Start, part.End - part.Start).CopyTo(buffer[offset..]);
                offset += part.End - part.Start;
            }
        });

        result.Add(new Token(text, source.StartOffset + parts[first].Start, source.StartOffset + parts[last].End));
    }

    private enum PartKind
    {
        Word,
        Number
    }

    private readonly struct Part(int start, int end, PartKind kind)
    {
        public int Start { get; } = start;
        public int End { get; } = end;
        public PartKind Kind { get; } = kind;
    }
}
