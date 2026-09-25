using System.Globalization;
using Rowles.DataForge;

namespace Rowles.DataForge.Workloads;

public sealed record VectorRecord(
    long Ordinal,
    string Id,
    int ClusterId,
    string Category,
    string AccessGroup,
    float[] Vector);
