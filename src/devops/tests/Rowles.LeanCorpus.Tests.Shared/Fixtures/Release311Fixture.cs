using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.LeanCorpus.Tests.Shared.Fixtures;

/// <summary>Loads immutable package-generated release data and verifies every physical file offline.</summary>
public static class Release311Fixture
{
    /// <summary>Extracts a release layout after verifying its pinned manifest and archive.</summary>
    public static JsonElement Extract(bool compound, string path)
    {
        string layout = compound ? "compound" : "loose";
        string expectedHash = compound
            ? "1705cdef8fe294abe63cdfe4d6fc83516e430d698c6ea3252543f523d978e055"
            : "793ee7380213b809767a5f918ecb11ae34ffa9834a213f0015102c79bfddc2a9";
        using Stream resource = typeof(Release311Fixture).Assembly.GetManifestResourceStream(
            $"Rowles.LeanCorpus.Tests.Shared.Fixtures.Indexes.3.1.1-release-{layout}.manifest.json")
            ?? throw new InvalidDataException("Release fixture manifest is missing.");
        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        byte[] data = bytes.ToArray();
        if (Convert.ToHexStringLower(SHA256.HashData(data)) != expectedHash)
            throw new InvalidDataException("Release fixture manifest hash differs from the frozen manifest.");
        using JsonDocument document = JsonDocument.Parse(data);
        JsonElement manifest = document.RootElement.Clone();
        if (manifest.GetProperty("FixtureFormatVersion").GetInt32() != 1 ||
            manifest.GetProperty("LeanCorpusPackageVersion").GetString() != "3.1.1" ||
            manifest.GetProperty("GenerationSeed").GetInt32() != 311400 ||
            manifest.GetProperty("IsCompound").GetBoolean() != compound)
            throw new InvalidDataException("Release fixture provenance is invalid.");
        HistoricalIndexFixtures.Extract(compound
            ? HistoricalIndexFixture.Version311ReleaseCompound
            : HistoricalIndexFixture.Version311ReleaseLoose, path);
        VerifyFiles(manifest, path);
        return manifest;
    }

    /// <summary>Rejects changed, missing or additional files before a reader can open the fixture.</summary>
    public static void VerifyFiles(JsonElement manifest, string path)
    {
        JsonElement.ArrayEnumerator files = manifest.GetProperty("Files").EnumerateArray();
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement file in files)
        {
            string name = file.GetProperty("Name").GetString()!;
            if (Path.GetFileName(name) != name || !expected.Add(name))
                throw new InvalidDataException("Release manifest contains an invalid file name.");
            string filePath = Path.Combine(path, name);
            if (!File.Exists(filePath) || new FileInfo(filePath).Length != file.GetProperty("Length").GetInt64())
                throw new InvalidDataException($"Release fixture file '{name}' is missing or has changed length.");
            using Stream stream = File.OpenRead(filePath);
            if (Convert.ToHexStringLower(SHA256.HashData(stream)) != file.GetProperty("Sha256").GetString())
                throw new InvalidDataException($"Release fixture file '{name}' has changed content.");
        }
        string[] actual = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(path, file)).ToArray();
        if (actual.Length != expected.Count || actual.Any(file => !expected.Contains(file)))
            throw new InvalidDataException("Release fixture contains unexpected files.");
    }
}
