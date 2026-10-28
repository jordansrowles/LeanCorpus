using Rowles.DataForge;
using Rowles.LeanCorpus.Benchmarks;

namespace Rowles.DataForge.Tests.Core;

public sealed class DatasetIdentityTests
{
    private const string ContentHash = "0000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    public void Dependencies_are_canonical_and_change_dataset_key_and_fingerprint()
    {
        var first = CreateIdentity(
        [
            new DataForgeDependencyVersion("Bogus", "35.6.5"),
            new DataForgeDependencyVersion("SharpCompress", "0.50.4")
        ]);
        var sameDependenciesInAnotherOrder = CreateIdentity(
        [
            new DataForgeDependencyVersion("SharpCompress", "0.50.4"),
            new DataForgeDependencyVersion("Bogus", "35.6.5")
        ]);
        var changedDependency = CreateIdentity(
        [
            new DataForgeDependencyVersion("Bogus", "35.6.6"),
            new DataForgeDependencyVersion("SharpCompress", "0.50.4")
        ]);

        Assert.Equal(first.GetCanonicalBytes(), sameDependenciesInAnotherOrder.GetCanonicalBytes());
        Assert.Equal(first.GetShortKey(), sameDependenciesInAnotherOrder.GetShortKey());
        Assert.NotEqual(first.GetCanonicalBytes(), changedDependency.GetCanonicalBytes());
        Assert.NotEqual(first.GetShortKey(), changedDependency.GetShortKey());
        Assert.Equal(ContentHash, changedDependency.ContentSha256);

        var before = BenchmarkProvenanceBuilder.BuildCombinedFingerprint([ToEvidence(first)]);
        var after = BenchmarkProvenanceBuilder.BuildCombinedFingerprint([ToEvidence(changedDependency)]);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Duplicate_dependency_names_are_rejected_in_dataset_identity()
    {
        var identity = CreateIdentity(
        [
            new DataForgeDependencyVersion("Bogus", "35.6.5"),
            new DataForgeDependencyVersion("Bogus", "35.6.6")
        ]);

        Assert.Throws<InvalidDataException>(() => identity.GetCanonicalBytes());
    }

    [Fact]
    public void From_manifest_copies_dependencies_and_reads_source_identity_only_for_imported_data()
    {
        var dependencies = new[] { new DataForgeDependencyVersion("Bogus", "35.6.5") };
        var source = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["datasetId"] = "imported-data",
            ["datasetVersion"] = "7"
        };
        var generated = new DataForgeManifest(
            DataForgeVersions.ManifestSchemaVersion,
            DataForgeVersions.CanonicalFormatVersion,
            DataForgeVersions.DataForgeVersion,
            DataForgeSourceKind.Generated,
            "profile",
            1,
            42,
            3,
            [],
            dependencies,
            3,
            ContentHash,
            ContentHash,
            [],
            source);
        var imported = generated with
        {
            SourceKind = DataForgeSourceKind.Imported,
            ProfileId = null,
            ProfileVersion = null,
            Seed = null,
            Source = source
        };

        var generatedIdentity = DataForgeDatasetIdentity.FromManifest(generated);
        var importedIdentity = DataForgeDatasetIdentity.FromManifest(imported);

        Assert.Null(generatedIdentity.DatasetId);
        Assert.Null(generatedIdentity.DatasetVersion);
        Assert.Equal(dependencies, generatedIdentity.Dependencies);
        Assert.Equal("imported-data", importedIdentity.DatasetId);
        Assert.Equal(7, importedIdentity.DatasetVersion);
        Assert.Equal(dependencies, importedIdentity.Dependencies);
    }

    [Fact]
    public void Benchmark_sidecar_round_trip_preserves_dependencies_in_identity_and_report()
    {
        var previousArtifactDirectory = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        var artifactDirectory = Path.Combine(Path.GetTempPath(), "dataforge-sidecar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactDirectory);
        try
        {
            Environment.SetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR", artifactDirectory);
            var identity = CreateIdentity(
            [
                new DataForgeDependencyVersion("Bogus", "35.6.5"),
                new DataForgeDependencyVersion("SharpCompress", "0.50.4")
            ]);

            BenchmarkDatasetSidecars.Write(identity);
            var evidence = Assert.Single(BenchmarkDatasetSidecars.ReadAll(artifactDirectory));

            Assert.Equal(identity.Dependencies, evidence.Identity.Dependencies);
            Assert.Equal(identity.Dependencies, evidence.Report.Dependencies);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR", previousArtifactDirectory);
            Directory.Delete(artifactDirectory, recursive: true);
        }
    }

    private static DataForgeDatasetIdentity CreateIdentity(IReadOnlyList<DataForgeDependencyVersion> dependencies) =>
        new(
            DataForgeSourceKind.Generated,
            DataForgeVersions.DataForgeVersion,
            "profile",
            1,
            DatasetId: null,
            DatasetVersion: null,
            Seed: 42,
            RecordCount: 3,
            Parameters: new Dictionary<string, string>(StringComparer.Ordinal) { ["count"] = "3" },
            Dependencies: dependencies,
            ContentSha256: ContentHash);

    private static BenchmarkDatasetEvidence ToEvidence(DataForgeDatasetIdentity identity) =>
        new(identity, new BenchmarkDatasetReport { Dependencies = identity.Dependencies.ToArray() }, identity.GetCanonicalBytes());
}
