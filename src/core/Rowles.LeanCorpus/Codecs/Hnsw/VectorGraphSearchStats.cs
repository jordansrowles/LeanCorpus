namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>Work done by one Float32 vector graph traversal (ADR052).</summary>
/// <param name="NodesVisited">Level-zero nodes whose distance was evaluated.</param>
/// <param name="DistanceEvaluations">All query distance evaluations, upper levels included.</param>
/// <param name="LayersDescended">Upper levels traversed before the level-zero beam.</param>
/// <param name="BudgetExceeded">Whether traversal stopped at its distance-evaluation budget.</param>
internal readonly record struct VectorGraphSearchStats(
    int NodesVisited,
    int DistanceEvaluations,
    int LayersDescended,
    bool BudgetExceeded);

/// <summary>A graph candidate: a segment document and its traversal distance (negative cosine).</summary>
internal readonly record struct VectorGraphHit(int DocId, float Distance);
