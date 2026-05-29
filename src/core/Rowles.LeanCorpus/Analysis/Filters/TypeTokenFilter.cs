using System.Collections.Frozen;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Filters tokens by extensible token type.
/// </summary>
public sealed class TypeTokenFilter : ITokenFilter
{
    private readonly FrozenSet<string> _types;
    private readonly bool _keepMatching;

    /// <summary>
    /// Initialises a new <see cref="TypeTokenFilter"/>.
    /// </summary>
    /// <param name="types">Token types to keep or remove.</param>
    /// <param name="keepMatching">When true, keeps matching types. When false, removes matching types.</param>
    public TypeTokenFilter(IEnumerable<string> types, bool keepMatching = true)
    {
        ArgumentNullException.ThrowIfNull(types);
        _types = types.Select(Token.ValidateType).ToFrozenSet(StringComparer.Ordinal);
        _keepMatching = keepMatching;
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        tokens.RemoveAll(token => _keepMatching ? !_types.Contains(token.Type) : _types.Contains(token.Type));
    }
}
