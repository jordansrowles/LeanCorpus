using Rowles.LeanCorpus.Analysis.Tokenisers.Japanese;

#if !ROWLES_TEXT
using Rowles.LeanCorpus.Store;
#endif

namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Japanese morphological tokeniser using a dictionary-backed least-cost
/// Viterbi search.
/// </summary>
/// <remarks>
/// Dictionary data is loaded lazily from a LeanCorpus <c>.jlc</c> file. The
/// default dictionary is shared for the process lifetime. For a custom
/// dictionary, pass a <see cref="JapaneseDictionary"/> and keep its
/// owner alive until all tokenisation has finished.
/// </remarks>
public sealed class JapaneseTokeniser : IThreadLocalSpanTokeniser
{
    /// <summary>Token type emitted for Japanese dictionary tokens.</summary>
    public const string JapaneseType = "japanese";

    private static readonly Lazy<JapaneseDictionary> SharedDictionary = new(
        static () => new JapaneseDictionary(DefaultDictionaryPath),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly JapaneseDictionary? _dictionary;

    internal JapaneseDictionary Dictionary => _dictionary ?? SharedDictionary.Value;

    /// <summary>Default path for the Japanese language codec.</summary>
    public static string DefaultDictionaryPath => FindDictionaryPath();

    /// <summary>
    /// Initialises a tokeniser using the shared default Japanese dictionary.
    /// </summary>
    public JapaneseTokeniser()
    {
        string path = DefaultDictionaryPath;
        if (!FileExists(path))
            throw new FileNotFoundException($"Japanese language codec not found at '{path}'.", path);

        _dictionary = null;
    }

    /// <summary>
    /// Initialises a non-owning tokeniser over an application-owned dictionary.
    /// </summary>
    /// <param name="dictionary">The dictionary resource used for tokenisation.</param>
    /// <remarks>
    /// This tokeniser and its thread-local copies do not dispose
    /// <paramref name="dictionary"/>. The caller retains ownership.
    /// </remarks>
    public JapaneseTokeniser(JapaneseDictionary dictionary)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
    }

    /// <inheritdoc/>
    public ISpanTokeniser CreateThreadLocalTokeniser()
        => _dictionary is null ? new JapaneseTokeniser() : new JapaneseTokeniser(_dictionary);

    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_dictionary is null)
        {
            if (input.IsEmpty)
                return;

            JapaneseViterbi.Tokenise(input, SharedDictionary.Value, sink);
            return;
        }

        using var use = _dictionary.EnterUse();
        if (input.IsEmpty)
            return;

        JapaneseViterbi.Tokenise(input, _dictionary, sink);
    }

    private static string FindDictionaryPath()
    {
        string? current = AppContext.BaseDirectory;
        while (current is not null)
        {
            string candidate = Path.Combine(current, "lexicons", "japanese.jlc");
            if (FileExists(candidate))
                return candidate;
            current = Path.GetDirectoryName(current);
        }

        return Path.Combine(AppContext.BaseDirectory, "lexicons", "japanese.jlc");
    }

#if ROWLES_TEXT
    private static bool FileExists(string path) => File.Exists(path);
#else
    private static bool FileExists(string path) => FileOpenRetry.FileExists(path);
#endif
}
