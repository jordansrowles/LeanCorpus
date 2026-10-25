using System.Globalization;
using Rowles.DataForge;

namespace Rowles.DataForge.Workloads;

public sealed record VectorGroundTruthResult(
    string QueryId,
    int TopK,
    string Metric,
    long[] NeighbourOrdinals,
    double[] CosineValues);
