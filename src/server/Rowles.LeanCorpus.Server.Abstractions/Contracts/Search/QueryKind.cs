namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Identifies the supported transport-neutral query shape.</summary>
public enum QueryKind
{
    /// <summary>Parsed query string.</summary>
    QueryString,
    /// <summary>Exact term.</summary>
    Term,
    /// <summary>Boolean clauses.</summary>
    Boolean,
    /// <summary>Phrase terms.</summary>
    Phrase,
    /// <summary>Prefix.</summary>
    Prefix,
    /// <summary>Wildcard pattern.</summary>
    Wildcard,
    /// <summary>Regular expression.</summary>
    Regexp,
    /// <summary>Proximity clauses.</summary>
    SpanNear,
    /// <summary>Nearest-neighbour vector.</summary>
    Vector
}
