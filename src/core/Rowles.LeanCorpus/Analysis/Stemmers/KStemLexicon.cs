using System.Collections.Frozen;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Immutable <see cref="IKStemLexicon"/> backed by a frozen set.
/// </summary>
/// <remarks>
/// The lexicon must be provided via <see cref="From(IEnumerable{string})"/>,
/// <see cref="FromFile"/>, or <see cref="FromStream"/>. A lexicon file is
/// available in the repository under <c>lexicons/kstem-dict.txt</c>.
/// </remarks>
public sealed class KStemLexicon : IKStemLexicon
{
    private readonly FrozenSet<string> _words;

    private KStemLexicon(FrozenSet<string> words) => _words = words;

    /// <inheritdoc/>
    public bool Contains(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return _words.Contains(word.ToLowerInvariant());
    }

    /// <summary>
    /// Builds a lexicon from base-form words. Empty lines and duplicate entries are ignored.
    /// </summary>
    public static KStemLexicon From(IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        var set = words
            .Where(static word => !string.IsNullOrWhiteSpace(word))
            .Select(static word => word.Trim().ToLowerInvariant())
            .ToFrozenSet(StringComparer.Ordinal);

        return new KStemLexicon(set);
    }

    /// <summary>
    /// Loads a UTF-8 text lexicon from disk, using one base form per line.
    /// Lines starting with <c>#</c> are ignored.
    /// </summary>
    public static KStemLexicon FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return From(File.ReadLines(path, System.Text.Encoding.UTF8));
    }

    /// <summary>
    /// Loads a UTF-8 text lexicon from a stream, using one base form per line.
    /// Lines starting with <c>#</c> are ignored. The stream is not disposed.
    /// </summary>
    public static KStemLexicon FromStream(Stream stream)
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

        return From(words);
    }
}
