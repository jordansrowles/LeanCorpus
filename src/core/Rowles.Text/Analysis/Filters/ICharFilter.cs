namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Transforms the complete input before tokenisation and reports how UTF-16 offsets in the transformed text
/// map back to offsets in the input.
/// </summary>
/// <remarks>
/// Return a <see cref="CharFilterResult"/> whose <see cref="CharFilterResult.OffsetCorrections"/> maps every
/// transformed UTF-16 character range to its source range. Chain filters with <see cref="CharFilterResult.Apply"/>
/// and analyse with <see cref="CharFilterResult.Analyse"/> so emitted token offsets refer to the original input.
/// </remarks>
public interface ICharFilter
{
    /// <summary>
    /// Transforms the input text and returns offset corrections from the transformed text to this input.
    /// </summary>
    /// <param name="input">The source text, with offsets measured in UTF-16 code units.</param>
    /// <returns>The transformed text and its offset-correction map.</returns>
    CharFilterResult Filter(ReadOnlySpan<char> input);
}
