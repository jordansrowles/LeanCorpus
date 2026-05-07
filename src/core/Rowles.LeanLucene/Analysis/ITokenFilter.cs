namespace Rowles.LeanLucene.Analysis;

/// <summary>
/// Transforms a token list in place (e.g. lowercase, stop-word removal, stemming).
/// </summary>
public interface ITokenFilter
{
    /// <summary>
    /// Applies the filter to the token list, modifying it in place.
    /// </summary>
    /// <param name="tokens">The token list to transform.</param>
    void Apply(List<Token> tokens);
}
