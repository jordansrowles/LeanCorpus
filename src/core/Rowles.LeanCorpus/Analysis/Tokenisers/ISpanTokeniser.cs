using Rowles.LeanCorpus.Analysis;

namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Splits input text into span-backed raw tokens without materialising token text strings.
/// </summary>
public interface ISpanTokeniser
{
    /// <summary>
    /// Splits the input text and emits tokens into the supplied sink.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    /// <param name="sink">The sink that receives tokens synchronously.</param>
    void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink);
}
