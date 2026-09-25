using System.Globalization;
using Rowles.DataForge;

namespace Rowles.DataForge.Workloads;

public enum VectorDistribution
{
    Uniform,
    Clustered,
    DenseNeighbourhood,
    NearDuplicate,
    QuantisationFriendly,
    QuantisationHostile
}
