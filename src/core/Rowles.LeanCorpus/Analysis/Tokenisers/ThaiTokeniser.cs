using System.Collections.Frozen;
using System.Globalization;

namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Lightweight Thai tokeniser using greedy longest-match segmentation with a
/// user-supplied lexicon and grapheme-cluster fallback for unknown runs.
/// </summary>
/// <remarks>
/// The lexicon must be provided via the constructor, <see cref="FromFile"/>, or
/// <see cref="FromStream"/>. A starter lexicon is available in the repository
/// under <c>lexicons/thai-dict.txt</c>.
/// </remarks>
public sealed class ThaiTokeniser : ISpanTokeniser
{
    /// <summary>Token type emitted for Thai segments.</summary>
    public const string ThaiType = "thai";

    private readonly FrozenSet<string> _lexicon;
    private readonly int _maxWordLength;

    /// <summary>
    /// Initialises a new <see cref="ThaiTokeniser"/> with the supplied lexicon.
    /// </summary>
    /// <param name="lexicon">Thai words used for longest-match segmentation. Must not be null or empty.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="lexicon"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="lexicon"/> is empty.</exception>
    public ThaiTokeniser(IEnumerable<string> lexicon)
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
    /// <returns>A new <see cref="ThaiTokeniser"/> initialised with the file contents.</returns>
    public static ThaiTokeniser FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new ThaiTokeniser(File.ReadLines(path, System.Text.Encoding.UTF8));
    }

    /// <summary>
    /// Loads a UTF-8 text lexicon from a stream, using one word per line.
    /// Lines starting with <c>#</c> are ignored. The stream is not disposed.
    /// </summary>
    /// <param name="stream">A readable, seekable stream containing the lexicon text.</param>
    /// <returns>A new <see cref="ThaiTokeniser"/> initialised with the stream contents.</returns>
    public static ThaiTokeniser FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var words = new List<string>();
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
                words.Add(line);
        }

        return new ThaiTokeniser(words);
    }

    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        int i = 0;

        while (i < input.Length)
        {
            if (UnicodeTokenisation.IsThai(input[i]))
            {
                int runEnd = i + 1;
                while (runEnd < input.Length && UnicodeTokenisation.IsThai(input[runEnd]))
                    runEnd++;

                TokeniseThaiRun(input, i, runEnd, sink);
                i = runEnd;
                continue;
            }

            UnicodeTokenisation.TokeniseNonThaiSpan(input, sink, ref i);
        }
    }


    private void TokeniseThaiRun(ReadOnlySpan<char> input, int start, int end, ISpanTokenSink sink)
    {
        int i = start;
        while (i < end)
        {
            int matchLength = TryFindLongestLexiconMatch(input, i, end);
            if (matchLength > 0)
            {
                sink.Add(input.Slice(i, matchLength), i, i + matchLength, ThaiType);
                i += matchLength;
                continue;
            }

            int clusterEnd = ReadThaiCluster(input, i, end);
            sink.Add(input[i..clusterEnd], i, clusterEnd, ThaiType);
            i = clusterEnd;
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

    private static int ReadThaiCluster(ReadOnlySpan<char> input, int start, int end)
    {
        int i = start + 1;
        while (i < end)
        {
            var category = char.GetUnicodeCategory(input[i]);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }
}
