namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>
/// A breakdown of how a document's score was computed, useful for debugging
/// relevance tuning and understanding BM25 behaviour.
/// </summary>
public sealed class Explanation
{
    /// <summary>Final computed score for this document.</summary>
    public float Score { get; init; }

    /// <summary>Human-readable description of this score component.</summary>
    public required string Description { get; init; }

    /// <summary>Child explanations that compose this score.</summary>
    public Explanation[] Details { get; init; } = [];

    /// <inheritdoc/>
    public override string ToString() => Format(indent: 0);

    private string Format(int indent)
    {
        var prefix = new string(' ', indent * 2);
        var sb = new System.Text.StringBuilder();
        sb.Append(prefix).Append(Score.ToString("F4")).Append(" = ").AppendLine(Description);
        foreach (var detail in Details)
            sb.Append(detail.Format(indent + 1));
        return sb.ToString();
    }
}
