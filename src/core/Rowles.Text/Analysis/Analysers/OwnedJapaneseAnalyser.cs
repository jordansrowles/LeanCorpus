using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Analysis.Tokenisers.Japanese;

namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// A Japanese analyser composition that owns a custom dictionary resource.
/// </summary>
/// <remarks>
/// Keep this owner alive until calls on this analyser and any thread-local
/// analysers created from it have finished. Disposal waits for active calls and
/// prevents later calls from using the dictionary.
/// </remarks>
public sealed class OwnedJapaneseAnalyser : IThreadLocalAnalyser, IDisposable
{
    private readonly JapaneseDictionary _dictionary;
    private readonly LanguageAnalyser _inner;
    private int _disposed;

    internal OwnedJapaneseAnalyser(string dictionaryPath)
    {
        _dictionary = new JapaneseDictionary(dictionaryPath);
        try
        {
            _inner = new LanguageAnalyser(
                new JapaneseTokeniser(_dictionary),
                StopWords.Japanese,
                stemmer: null);
        }
        catch
        {
            _dictionary.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        ThrowIfDisposed();
        _inner.Analyse(input, sink);
    }

    /// <inheritdoc/>
    public IAnalyser CreateThreadLocalAnalyser()
    {
        ThrowIfDisposed();
        return _inner.CreateThreadLocalAnalyser();
    }

    /// <summary>
    /// Disposes the owned dictionary after active analysis calls complete.
    /// </summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _dictionary.Dispose();
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
