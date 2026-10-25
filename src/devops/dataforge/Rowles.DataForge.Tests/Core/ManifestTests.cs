using System.Text;
using Rowles.DataForge;

namespace Rowles.DataForge.Tests.Core;

public sealed class ManifestTests
{
    private const string Hash = "0000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    public void Manifest_fields_and_parameter_order_are_canonical()
    {
        var manifest = CreateManifest(
            parameters: [new DataForgeParameter("zeta", "last"), new DataForgeParameter("alpha", "first")],
            dependencies: [new DataForgeDependencyVersion("z", "2"), new DataForgeDependencyVersion("a", "1")]);

        var bytes = DataForgeManifestCodec.ToCanonicalBytes(manifest);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.StartsWith("{\"schemaVersion\":1,\"canonicalFormatVersion\":1,\"dataForgeVersion\":1,\"sourceKind\":\"Generated\"", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("\"alpha\":\"first\"", StringComparison.Ordinal) < text.IndexOf("\"zeta\":\"last\"", StringComparison.Ordinal));
        Assert.True(text.IndexOf("\"name\":\"a\"", StringComparison.Ordinal) < text.IndexOf("\"name\":\"z\"", StringComparison.Ordinal));
        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("generatedAt", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_manifest_round_trips_without_changing_bytes()
    {
        var path = Path.Combine(Path.GetTempPath(), "dataforge-manifest-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var bytes = DataForgeManifestCodec.ToCanonicalBytes(CreateManifest());
            File.WriteAllBytes(path, bytes);
            var manifest = DataForgeManifestCodec.Read(path);
            Assert.Equal(bytes, DataForgeManifestCodec.ToCanonicalBytes(manifest));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Unsupported_versions_invalid_counts_missing_hashes_and_malformed_hashes_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(CreateManifest() with { SchemaVersion = 2 }));
        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(CreateManifest() with { CanonicalFormatVersion = 2 }));
        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(CreateManifest() with { DataForgeVersion = 2 }));
        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(CreateManifest() with { RecordCount = 0 }));
        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(CreateManifest() with { RecordCount = 10_000_001 }));
        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(CreateManifest() with { ContentSha256 = string.Empty }));
        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(CreateManifest() with { ArtefactSha256 = new string('A', 64) }));
    }

    [Fact]
    public void Duplicate_parameter_keys_are_rejected()
    {
        var manifest = CreateManifest(parameters:
        [
            new DataForgeParameter("repeat", "one"),
            new DataForgeParameter("repeat", "two")
        ]);
        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(manifest));
    }

    [Fact]
    public void Duplicate_dependency_names_are_rejected()
    {
        var manifest = CreateManifest(dependencies:
        [
            new DataForgeDependencyVersion("Bogus", "35.6.5"),
            new DataForgeDependencyVersion("Bogus", "35.6.6")
        ]);

        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(manifest));
    }

    [Fact]
    public void Generated_manifests_reject_imported_source_metadata()
    {
        var manifest = CreateManifest() with
        {
            Source = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["datasetId"] = "foreign-dataset",
                ["datasetVersion"] = "1"
            }
        };

        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(manifest));
    }

    [Fact]
    public void Imported_manifests_reject_generated_fields_and_require_dataset_identity()
    {
        var imported = CreateImportedManifest();

        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(imported with { ProfileId = "profile" }));
        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(imported with { ProfileVersion = 1 }));
        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(imported with { Seed = 42 }));
        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(imported with
        {
            Parameters = [new DataForgeParameter("unexpected", "value")]
        }));
        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(imported with
        {
            Source = new Dictionary<string, string>(StringComparer.Ordinal) { ["datasetVersion"] = "1" }
        }));
        Assert.Throws<InvalidDataException>(() => DataForgeManifestCodec.Validate(imported with
        {
            Source = new Dictionary<string, string>(StringComparer.Ordinal) { ["datasetId"] = "dataset" }
        }));
    }

    [Fact]
    public void Strictly_validated_imported_manifest_round_trips_canonically()
    {
        var path = Path.Combine(Path.GetTempPath(), "dataforge-imported-manifest-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var bytes = DataForgeManifestCodec.ToCanonicalBytes(CreateImportedManifest());
            File.WriteAllBytes(path, bytes);
            var manifest = DataForgeManifestCodec.Read(path);

            Assert.Equal(DataForgeSourceKind.Imported, manifest.SourceKind);
            Assert.Equal(bytes, DataForgeManifestCodec.ToCanonicalBytes(manifest));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Generation_options_enforce_count_and_parameter_utf8_bounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataForgeGenerationOptions(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataForgeGenerationOptions(1, 10_000_001));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataForgeGenerationOptions(1, 1, new Dictionary<string, string> { [new string('k', 65)] = "v" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataForgeGenerationOptions(1, 1, new Dictionary<string, string> { ["key"] = new string('v', 1_025) }));
    }

    private static DataForgeManifest CreateManifest(
        IReadOnlyList<DataForgeParameter>? parameters = null,
        IReadOnlyList<DataForgeDependencyVersion>? dependencies = null) =>
        new(
            DataForgeVersions.ManifestSchemaVersion,
            DataForgeVersions.CanonicalFormatVersion,
            DataForgeVersions.DataForgeVersion,
            DataForgeSourceKind.Generated,
            "profile",
            1,
            42,
            1,
            parameters ?? [],
            dependencies ?? [],
            1,
            Hash,
            Hash,
            [],
            Source: null);

    private static DataForgeManifest CreateImportedManifest() => CreateManifest() with
    {
        SourceKind = DataForgeSourceKind.Imported,
        ProfileId = null,
        ProfileVersion = null,
        Seed = null,
        Parameters = [],
        Source = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["datasetId"] = "dataset",
            ["datasetVersion"] = "1"
        }
    };
}
