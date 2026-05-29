using Rowles.LeanCorpus.Analysis;

namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Provides an allocation-aware analysis path that emits span-backed tokens.
/// </summary>
public interface ISpanAnalyser
{
    /// <summary>
    /// Attempts to analyse the input into the supplied span token sink.
    /// </summary>
    /// <param name="input">The raw text to analyse.</param>
    /// <param name="sink">The sink that receives tokens synchronously.</param>
    /// <returns>
    /// <see langword="true"/> when the analyser emitted tokens through <paramref name="sink"/>;
    /// <see langword="false"/> when the caller should use the legacy <see cref="IAnalyser.Analyse"/> path.
    /// </returns>
    bool TryAnalyse(ReadOnlySpan<char> input, ISpanTokenSink sink);
}
