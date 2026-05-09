namespace Rowles.LeanLucene.Search.Queries;

/// <summary>How to combine the inner query's BM25 score with the numeric field value.</summary>
public enum ScoreMode
{
    /// <summary>Multiplies the query score by the numeric field value.</summary>
    Multiply,

    /// <summary>Replaces the query score entirely with the numeric field value.</summary>
    Replace,

    /// <summary>Adds the numeric field value to the query score.</summary>
    Sum,

    /// <summary>Takes the maximum of the query score and the numeric field value.</summary>
    Max
}
