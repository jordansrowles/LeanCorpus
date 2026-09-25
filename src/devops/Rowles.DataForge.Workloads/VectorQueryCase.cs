using System.Globalization;
using Rowles.DataForge;

namespace Rowles.DataForge.Workloads;

public sealed record VectorQueryCase(string Id, int TargetClusterId, float[] Vector);
