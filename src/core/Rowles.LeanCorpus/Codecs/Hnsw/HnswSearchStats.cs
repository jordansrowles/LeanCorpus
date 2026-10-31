namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>
/// Per-call HNSW search statistics. <see cref="NodesVisited"/> is the primary recall-vs-cost
/// signal: it counts graph nodes examined during layer-zero search and shortlist completion.
/// </summary>
internal readonly record struct HnswSearchStats(int NodesVisited, int LayersDescended);
