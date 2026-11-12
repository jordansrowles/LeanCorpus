namespace Rowles.DataForge.Tests;

/// <summary>
/// Tests that set or read the process-wide <c>LEANCORPUS_ARTIFACT_DIR</c> variable. Benchmark
/// dataset helpers write sidecars wherever it points, so a parallel test could write into another
/// test's directory. This collection runs on its own, after the parallel collections.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BenchmarkArtifactDirectoryCollection
{
    public const string Name = "Benchmark artifact directory";
}
