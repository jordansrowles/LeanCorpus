namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Normalises an input to one term without removing it, splitting it, or expanding it into a token graph.
/// </summary>
/// <remarks>
/// Implementations return <see langword="false"/> when the input cannot be represented as one non-empty term.
/// This contract is intended for wildcard and range literals, where a complete token stream is not valid output.
/// </remarks>
public interface ITermNormaliser
{
    /// <summary>Attempts to normalise <paramref name="input"/> as one term.</summary>
    /// <param name="input">The literal text to normalise.</param>
    /// <param name="normalised">The normalised term when the input maps to exactly one term.</param>
    /// <returns><see langword="true"/> when one non-empty term was produced; otherwise <see langword="false"/>.</returns>
    bool TryNormalise(ReadOnlySpan<char> input, out string normalised);
}
