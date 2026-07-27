using System.Collections.Frozen;

using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Chinese word segmentation tokeniser using greedy longest-match against
/// a user-supplied lexicon. Unknown CJK characters fall back to unigrams.
/// Non-CJK text is tokenised by standard word boundaries.
/// </summary>
/// <remarks>
/// The lexicon must be provided via the constructor, <see cref="FromFile"/>, or
/// <see cref="FromStream"/>. A lexicon file is available as an optional download.
/// The format is one word per line, UTF-8, with <c>#</c> comments.
/// </remarks>
public sealed class ChineseLexiconTokeniser : ISpanTokeniser
{
    /// <summary>Token type emitted for CJK ideograph tokens.</summary>
    public const string CjkType = CJKBigramTokeniser.CjkType;

    private readonly FrozenSet<string> _lexicon;
    private readonly int _maxWordLength;

    /// <summary>
    /// Initialises a new <see cref="ChineseLexiconTokeniser"/> with the supplied lexicon.
    /// </summary>
    /// <param name="lexicon">Chinese words used for longest-match segmentation. Must not be null or empty.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="lexicon"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="lexicon"/> is empty.</exception>
    public ChineseLexiconTokeniser(IEnumerable<string> lexicon)
    {
        ArgumentNullException.ThrowIfNull(lexicon);

        var words = lexicon
            .Where(static word => !string.IsNullOrWhiteSpace(word))
            .Select(static word => word.Trim())
            .ToArray();

        if (words.Length == 0)
            throw new ArgumentException("Lexicon must contain at least one word.", nameof(lexicon));

        _lexicon = words.ToFrozenSet(StringComparer.Ordinal);
        _maxWordLength = words.Max(static word => word.Length);
    }

    /// <summary>
    /// Loads a UTF-8 text lexicon from disk, using one word per line.
    /// Lines starting with <c>#</c> are ignored.
    /// </summary>
    /// <param name="path">Path to the lexicon file.</param>
    /// <returns>A new <see cref="ChineseLexiconTokeniser"/> initialised with the file contents.</returns>
    public static ChineseLexiconTokeniser FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new ChineseLexiconTokeniser(FileOpenRetry.ReadLines(path, System.Text.Encoding.UTF8));
    }

    /// <summary>
    /// Loads a UTF-8 text lexicon from a stream, using one word per line.
    /// Lines starting with <c>#</c> are ignored. The stream is not disposed.
    /// </summary>
    /// <param name="stream">A readable, seekable stream containing the lexicon text.</param>
    /// <returns>A new <see cref="ChineseLexiconTokeniser"/> initialised with the stream contents.</returns>
    public static ChineseLexiconTokeniser FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var words = new List<string>();
        using var reader = FileOpenRetry.OpenTextReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
                words.Add(line);
        }

        return new ChineseLexiconTokeniser(words);
    }

    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        int i = 0;

        while (i < input.Length)
        {
            int codePoint = CjkUnicode.DecodeCodePoint(input, i, out int charsConsumed);
            if (CjkUnicode.IsIdeograph(codePoint))
            {
                int runStart = i;
                i += charsConsumed;
                while (i < input.Length)
                {
                    int nextCodePoint = CjkUnicode.DecodeCodePoint(input, i, out int nextChars);
                    if (!CjkUnicode.IsIdeograph(nextCodePoint))
                        break;
                    i += nextChars;
                }
                TokeniseCjkRun(input, runStart, i, sink);
            }
            else if (char.IsLetterOrDigit(input[i]))
            {
                int start = i;
                while (i < input.Length && char.IsLetterOrDigit(input[i]))
                {
                    int nextCodePoint = CjkUnicode.DecodeCodePoint(input, i, out int nextChars);
                    if (CjkUnicode.IsIdeograph(nextCodePoint))
                        break;
                    i += nextChars;
                }
                sink.Add(
                    input[start..i],
                    start,
                    i,
                    UnicodeTokenisation.ClassifyTokenType(input[start..i]));
            }
            else
            {
                i++; // skip whitespace/punctuation
            }
        }
    }

    private void TokeniseCjkRun(ReadOnlySpan<char> input, int start, int end, ISpanTokenSink sink)
    {
        int i = start;
        while (i < end)
        {
            int matchLength = TryFindLongestLexiconMatch(input, i, end);
            if (matchLength > 0)
            {
                sink.Add(input.Slice(i, matchLength), i, i + matchLength, CjkType);
                i += matchLength;
            }
            else
            {
                CjkUnicode.DecodeCodePoint(input, i, out int charsConsumed);
                sink.Add(input.Slice(i, charsConsumed), i, i + charsConsumed, CjkType);
                i += charsConsumed;
            }
        }
    }

    private int TryFindLongestLexiconMatch(ReadOnlySpan<char> input, int start, int end)
    {
        int maxLength = Math.Min(_maxWordLength, end - start);
        for (int length = maxLength; length > 0; length--)
        {
            var candidate = input.Slice(start, length);
            if (_lexicon.GetAlternateLookup<ReadOnlySpan<char>>().Contains(candidate))
                return length;
        }

        return 0;
    }

}
