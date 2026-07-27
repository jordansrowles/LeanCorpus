using Rowles.LeanCorpus.Analysis.Tokenisers.Japanese;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Japanese morphological tokeniser using a dictionary-backed least-cost
/// Viterbi search.
/// </summary>
/// <remarks>
/// Dictionary data is loaded lazily from a LeanCorpus <c>.jlc</c> file. The
/// default dictionary is shared for the process lifetime. Instances created
/// with a custom dictionary path own that mapping and should be disposed.
/// </remarks>
public sealed class JapaneseTokeniser : ISpanTokeniser, IDisposable
{
    /// <summary>Token type emitted for Japanese dictionary tokens.</summary>
    public const string JapaneseType = "japanese";

    private static readonly Lazy<JapaneseDictionary> SharedDictionary = new(
        static () => new JapaneseDictionary(DefaultDictionaryPath),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Lazy<JapaneseDictionary> _dictionary;
    private readonly bool _ownsDictionary;
    private bool _disposed;

    /// <summary>Default path for the Japanese language codec.</summary>
    public static string DefaultDictionaryPath => FindDictionaryPath();

    /// <summary>
    /// Initialises a tokeniser using the shared default Japanese dictionary.
    /// </summary>
    public JapaneseTokeniser()
    {
        string path = DefaultDictionaryPath;
        if (!FileOpenRetry.FileExists(path))
            throw new FileNotFoundException($"Japanese language codec not found at '{path}'.", path);

        _dictionary = SharedDictionary;
    }

    /// <summary>
    /// Initialises a tokeniser using a custom Japanese language codec.
    /// </summary>
    /// <param name="dictionaryPath">Path to a versioned <c>.jlc</c> file.</param>
    public JapaneseTokeniser(string dictionaryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dictionaryPath);
        string fullPath = Path.GetFullPath(dictionaryPath);
        if (!FileOpenRetry.FileExists(fullPath))
            throw new FileNotFoundException($"Japanese language codec not found at '{fullPath}'.", fullPath);

        _dictionary = new Lazy<JapaneseDictionary>(
            () => new JapaneseDictionary(fullPath),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _ownsDictionary = true;
    }

    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(sink);
        if (input.IsEmpty)
            return;

        JapaneseViterbi.Tokenise(input, _dictionary.Value, sink);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_ownsDictionary && _dictionary.IsValueCreated)
            _dictionary.Value.Dispose();
    }

    private static string FindDictionaryPath()
    {
        string? current = AppContext.BaseDirectory;
        while (current is not null)
        {
            string candidate = Path.Combine(current, "lexicons", "japanese.jlc");
            if (FileOpenRetry.FileExists(candidate))
                return candidate;
            current = Path.GetDirectoryName(current);
        }

        return Path.Combine(AppContext.BaseDirectory, "lexicons", "japanese.jlc");
    }
}
