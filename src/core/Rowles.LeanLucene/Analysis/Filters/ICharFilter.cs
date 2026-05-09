namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Interface for character-level filters that transform raw text before tokenisation.
/// Char filters run before the tokeniser, operating on the entire input string.
/// </summary>
public interface ICharFilter
{
    /// <summary>
    /// Transforms the input text, returning filtered text ready for tokenisation.
    /// </summary>
    string Filter(ReadOnlySpan<char> input);
}
