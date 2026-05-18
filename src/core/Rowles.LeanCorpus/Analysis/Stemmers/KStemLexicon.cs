using System.Collections.Frozen;
using System.Reflection;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Immutable <see cref="IKStemLexicon"/> backed by a frozen set.
/// </summary>
public sealed class KStemLexicon : IKStemLexicon
{
    private const string DefaultResourceName = "Rowles.LeanCorpus.Analysis.Stemmers.kstem-dict.txt";
    private static readonly Lazy<KStemLexicon> s_default = new(
        () => FromEmbeddedResource(typeof(KStemLexicon).Assembly, DefaultResourceName));

    private readonly FrozenSet<string> _words;

    private KStemLexicon(FrozenSet<string> words) => _words = words;

    /// <summary>
    /// Gets the default embedded KStem lexicon used by the parameterless <see cref="KStemmer"/> constructor.
    /// </summary>
    public static KStemLexicon Default => s_default.Value;

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
    /// Loads a UTF-8 text lexicon from an embedded resource, using one base form per line.
    /// Lines starting with <c>#</c> are ignored.
    /// </summary>
    public static KStemLexicon FromEmbeddedResource(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resourceName);

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found in {assembly.FullName}.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

        var words = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
                words.Add(line);
        }

        return From(words);
    }

    /// <summary>
    /// Loads a UTF-8 text lexicon from disk, using one base form per line.
    /// Lines starting with <c>#</c> are ignored.
    /// </summary>
    public static KStemLexicon FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return From(File.ReadLines(path));
    }
}
