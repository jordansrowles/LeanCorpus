namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Finds documents similar to a source document by extracting significant terms
/// from term vectors and constructing a weighted BooleanQuery.
/// </summary>
public sealed class MoreLikeThisQuery : Query
{
    /// <summary>Global document ID of the source document.</summary>
    public int DocId { get; }

    /// <summary>Fields to extract terms from.</summary>
    public string[] Fields { get; }

    /// <summary>Configuration parameters.</summary>
    public MoreLikeThisParameters Parameters { get; }

    /// <inheritdoc/>
    public override string Field => Fields.Length > 0 ? Fields[0] : string.Empty;

    /// <summary>Initialises a new <see cref="MoreLikeThisQuery"/> for the given source document.</summary>
    /// <param name="docId">The global document ID of the source document to find similar documents for.</param>
    /// <param name="fields">The fields from which to extract representative terms.</param>
    /// <param name="parameters">Optional configuration for term extraction. Defaults to <see cref="MoreLikeThisParameters"/> defaults.</param>
    public MoreLikeThisQuery(int docId, string[] fields, MoreLikeThisParameters? parameters = null)
    {
        DocId = docId;
        Fields = fields;
        Parameters = parameters ?? new MoreLikeThisParameters();
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is MoreLikeThisQuery other &&
        DocId == other.DocId &&
        Boost == other.Boost &&
        Fields.AsSpan().SequenceEqual(other.Fields);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(nameof(MoreLikeThisQuery));
        h.Add(DocId);
        foreach (var f in Fields) h.Add(f);
        return CombineBoost(h.ToHashCode());
    }
}
