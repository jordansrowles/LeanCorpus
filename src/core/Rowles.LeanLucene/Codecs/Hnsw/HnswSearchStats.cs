namespace Rowles.LeanLucene.Codecs.Hnsw;

/// <summary>
/// Per-call HNSW search statistics. <see cref="NodesVisited"/> is the primary recall-vs-cost
/// signal: it counts distinct nodes visited during the layer-zero traversal.
/// </summary>
internal readonly record struct HnswSearchStats(int NodesVisited, int LayersDescended);
