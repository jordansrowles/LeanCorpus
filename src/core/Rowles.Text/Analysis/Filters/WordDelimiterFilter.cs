namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Splits compound tokens on word delimiters, case transitions, and
/// letter-digit boundaries.
/// </summary>
/// <remarks>
/// <para>For example, <c>"WiFi4Schools_test"</c> splits into
/// <c>"Wi"</c>, <c>"Fi"</c>, <c>"4"</c>, <c>"Schools"</c>, <c>"test"</c>.
/// With <see cref="CatenateWords"/> the filter also emits
/// <c>"WiFi"</c>, <c>"Schools"</c>, and <c>"WiFiSchools"</c>.
/// With <see cref="PreserveOriginal"/> the original token is emitted too.</para>
/// <para>Sub-tokens are emitted at the same position
/// (<c>positionIncrement == 0</c>) so downstream filters see them as
/// alternatives.</para>
/// </remarks>
public sealed class WordDelimiterFilter : ISpanTokenFilter
{
    private static readonly char[] DefaultDelimiters =
        ['-', '_', '.', '/', '\\', '&', '+', '#', ',', ';', ':'];

    private readonly char[] _delimiters;

    /// <summary>
    /// When true, word sub-tokens are emitted.
    /// </summary>
    public bool GenerateWordParts { get; init; } = true;

    /// <summary>
    /// When true, number sub-tokens are emitted.
    /// </summary>
    public bool GenerateNumberParts { get; init; } = true;

    /// <summary>
    /// When true, runs of word parts are concatenated and emitted.
    /// </summary>
    public bool CatenateWords { get; init; }

    /// <summary>
    /// When true, runs of number parts are concatenated and emitted.
    /// </summary>
    public bool CatenateNumbers { get; init; }

    /// <summary>
    /// When true, all word and number parts are concatenated and emitted.
    /// </summary>
    public bool CatenateAll { get; init; }

    /// <summary>
    /// When true, split on lowercase-to-uppercase transitions within a token.
    /// </summary>
    public bool SplitOnCaseChange { get; init; } = true;

    /// <summary>
    /// When true, split between letters and digits.
    /// </summary>
    public bool SplitOnNumerics { get; init; } = true;

    /// <summary>
    /// When true, strip trailing English possessives (<c>'s</c> or <c>'</c>)
    /// before splitting.
    /// </summary>
    public bool StemEnglishPossessive { get; init; } = true;

    /// <summary>
    /// When true, emit the original token text alongside the split parts.
    /// </summary>
    public bool PreserveOriginal { get; init; }

    /// <summary>
    /// Initialises a new <see cref="WordDelimiterFilter"/>.
    /// </summary>
    /// <param name="delimiters">
    /// Characters that act as word delimiters. Defaults to common punctuation.
    /// </param>
    public WordDelimiterFilter(char[]? delimiters = null)
    {
        _delimiters = delimiters ?? DefaultDelimiters;
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

        ReadOnlySpan<char> working = text;
        int workingEndOffset = endOffset;

        // Strip trailing English possessive.
        if (StemEnglishPossessive && working.Length > 1)
        {
            if (working.EndsWith("'s".AsSpan()) || working.EndsWith("'S".AsSpan()))
            {
                working = working[..^2];
                workingEndOffset -= 2;
            }
            else if (working[^1] == '\'')
            {
                working = working[..^1];
                workingEndOffset--;
            }
        }

        if (working.IsEmpty)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        // Build sub-token spans.
        var parts = new List<(int RelStart, int RelEnd, bool IsNumber)>();
        SplitIntoParts(working, parts);

        if (parts.Count == 0)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        bool emittedAny = false;
        var wordRunIndices = new List<int>(parts.Count);
        var numberRunIndices = new List<int>(parts.Count);

        for (int i = 0; i < parts.Count; i++)
        {
            var (relStart, relEnd, isNumber) = parts[i];

            if (isNumber)
            {
                if (GenerateNumberParts)
                {
                    EmitPart(working, relStart, relEnd, startOffset + relStart,
                        startOffset + relEnd,
                        emittedAny ? 0 : positionIncrement, sink);
                    emittedAny = true;
                }

                if (CatenateNumbers)
                    numberRunIndices.Add(i);
            }
            else
            {
                if (GenerateWordParts)
                {
                    EmitPart(working, relStart, relEnd, startOffset + relStart,
                        startOffset + relEnd,
                        emittedAny ? 0 : positionIncrement, sink);
                    emittedAny = true;
                }

                if (CatenateWords)
                    wordRunIndices.Add(i);
            }
        }

        // Flush concatenation runs.
        if (CatenateWords && wordRunIndices.Count > 1)
        {
            EmitCatenatedParts(working, parts, wordRunIndices, startOffset,
                emittedAny ? 0 : positionIncrement, sink);
            emittedAny = true;
        }

        if (CatenateNumbers && numberRunIndices.Count > 1)
        {
            EmitCatenatedParts(working, parts, numberRunIndices, startOffset,
                emittedAny ? 0 : positionIncrement, sink);
            emittedAny = true;
        }

        if (CatenateAll && parts.Count > 0)
        {
            var allIndices = new List<int>(parts.Count);
            for (int i = 0; i < parts.Count; i++)
                allIndices.Add(i);
            EmitCatenatedParts(working, parts, allIndices, startOffset,
                emittedAny ? 0 : positionIncrement, sink);
            emittedAny = true;
        }

        if (PreserveOriginal)
        {
            sink.Add(text, startOffset, endOffset, type,
                emittedAny ? 0 : positionIncrement, payload);
            emittedAny = true;
        }

        // If nothing was emitted (all parts filtered), emit original.
        if (!emittedAny)
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }

    private static void EmitPart(
        ReadOnlySpan<char> source, int relStart, int relEnd,
        int absStart, int absEnd,
        int posInc, ISpanTokenSink sink)
    {
        sink.Add(source[relStart..relEnd], absStart, absEnd,
            Token.DefaultType, posInc, null);
    }

    private static void EmitCatenatedParts(
        ReadOnlySpan<char> source,
        List<(int RelStart, int RelEnd, bool IsNumber)> parts,
        List<int> indices,
        int startOffset,
        int posInc,
        ISpanTokenSink sink)
    {
        if (indices.Count == 0) return;

        if (indices.Count == 1)
        {
            var (relStart, relEnd, _) = parts[indices[0]];
            sink.Add(source[relStart..relEnd], startOffset + relStart,
                startOffset + relEnd, Token.DefaultType, posInc, null);
            return;
        }

        // Calculate total character length.
        int totalLen = 0;
        for (int j = 0; j < indices.Count; j++)
        {
            int i = indices[j];
            totalLen += parts[i].RelEnd - parts[i].RelStart;
        }

        char[]? rented = null;
        Span<char> buf = totalLen <= 256
            ? stackalloc char[totalLen]
            : (rented = System.Buffers.ArrayPool<char>.Shared.Rent(totalLen)).AsSpan(0, totalLen);

        int pos = 0;
        for (int j = 0; j < indices.Count; j++)
        {
            int i = indices[j];
            var (relStart, relEnd, _) = parts[i];
            var span = source[relStart..relEnd];
            span.CopyTo(buf[pos..]);
            pos += span.Length;
        }

        int absStart = startOffset + parts[indices[0]].RelStart;
        int absEnd = startOffset + parts[indices[^1]].RelEnd;
        sink.Add(buf, absStart, absEnd, Token.DefaultType, posInc, null);

        if (rented is not null)
            System.Buffers.ArrayPool<char>.Shared.Return(rented);
    }

    private void SplitIntoParts(
        ReadOnlySpan<char> text,
        List<(int RelStart, int RelEnd, bool IsNumber)> parts)
    {
        parts.Clear();

        if (text.IsEmpty) return;

        int runStart = 0;
        CharKind prevKind = Classify(text[0]);

        for (int i = 1; i < text.Length; i++)
        {
            CharKind kind = Classify(text[i]);

            if (kind == CharKind.Delimiter)
            {
                if (prevKind != CharKind.Delimiter)
                    AddPart(parts, runStart, i, prevKind == CharKind.Digit);
                runStart = i + 1;
                prevKind = kind;
                continue;
            }

            if (prevKind == CharKind.Delimiter)
            {
                runStart = i;
                prevKind = kind;
                continue;
            }

            bool shouldSplit = false;

            if (kind != prevKind)
            {
                if (SplitOnNumerics &&
                    (prevKind == CharKind.Digit || kind == CharKind.Digit))
                {
                    shouldSplit = true;
                }
                else if (SplitOnCaseChange &&
                         prevKind == CharKind.Lower && kind == CharKind.Upper)
                {
                    shouldSplit = true;
                }
                else if (SplitOnCaseChange &&
                         prevKind == CharKind.Upper && kind == CharKind.Lower)
                {
                    // UPPER → lower: split before the last uppercase if the
                    // uppercase run is longer than one character.
                    // "POWERShot" → "POWER", "Shot"
                    if (i - runStart > 1)
                    {
                        AddPart(parts, runStart, i - 1, false);
                        runStart = i - 1;
                    }
                }
            }

            if (shouldSplit)
            {
                AddPart(parts, runStart, i, prevKind == CharKind.Digit);
                runStart = i;
            }

            prevKind = kind;
        }

        // Emit the final run.
        if (prevKind != CharKind.Delimiter && runStart < text.Length)
            AddPart(parts, runStart, text.Length, prevKind == CharKind.Digit);
    }

    private static void AddPart(
        List<(int, int, bool)> parts, int start, int end, bool isNumber)
    {
        if (end > start)
            parts.Add((start, end, isNumber));
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private CharKind Classify(char c)
    {
        if (IsDelimiter(c)) return CharKind.Delimiter;
        if (char.IsDigit(c)) return CharKind.Digit;
        if (char.IsLower(c)) return CharKind.Lower;
        if (char.IsUpper(c)) return CharKind.Upper;
        return CharKind.Lower;
    }

    private bool IsDelimiter(char c)
    {
        foreach (char d in _delimiters)
            if (c == d) return true;
        return false;
    }

    private enum CharKind : byte
    {
        Delimiter,
        Digit,
        Lower,
        Upper
    }
}