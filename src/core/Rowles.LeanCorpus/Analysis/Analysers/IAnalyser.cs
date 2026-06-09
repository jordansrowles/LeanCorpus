using Rowles.LeanCorpus.Analysis;

namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Analyses input text and emits span-backed tokens into the supplied sink.
/// </summary>
public interface IAnalyser
{
    /// <summary>
    /// Analyses the input text and emits tokens synchronously into <paramref name="sink"/>.
    /// Callers that require a materialised <see cref="List{Token}"/> should use
    /// <see cref="MaterialisingTokenSink"/> as the sink argument.
    /// </summary>
    /// <param name="input">The raw text to analyse.</param>
    /// <param name="sink">The sink that receives tokens synchronously.</param>
    void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink);
}
