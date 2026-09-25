using Rowles.DataForge;

namespace Rowles.LeanCorpus.Benchmarks;

internal static class BenchmarkDeterministicRandom
{
    public static DataForgePrng Create(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return new DataForgePrng(DataForgeSeedDerivation.Derive(42, "benchmark-fixture", 1, 0, label));
    }
}
