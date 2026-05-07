namespace Rowles.LeanLucene.Analysis.Analysers;

/// <summary>
/// Analyses input text into a list of tokens for indexing or querying.
/// </summary>
public interface IAnalyser
{
    /// <summary>
    /// Analyses the input text and returns a list of tokens.
    /// </summary>
    /// <param name="input">The raw text to analyse.</param>
    /// <returns>A list of tokens produced by the analysis pipeline.</returns>
    List<Token> Analyse(ReadOnlySpan<char> input);
}
