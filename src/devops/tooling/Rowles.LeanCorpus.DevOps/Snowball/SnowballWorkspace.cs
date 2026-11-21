using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rowles.LeanCorpus.DevOps.Snowball;

/// <summary>
/// The repository files owned by <c>./devops snowball</c>: the pinned fixtures (input), the
/// generated production algorithms and identity table (output) and the downloaded word lists.
/// </summary>
internal sealed class SnowballWorkspace(string repositoryRoot)
{
    public string RepositoryRoot { get; } = Path.GetFullPath(repositoryRoot);

    public string FixtureRoot => Path.Combine(RepositoryRoot, "src", "devops", "tests", "fixtures", "snowball");

    public string SnowballSourceRoot => Path.Combine(RepositoryRoot, "src", "core", "Rowles.Text", "Analysis", "Stemmers", "Snowball");

    public string GeneratedRoot => Path.Combine(SnowballSourceRoot, "Generated");

    public string VersionsPath => Path.Combine(SnowballSourceRoot, "analysis-versions.json");

    public string PendingFingerprintsPath => Path.Combine(RepositoryRoot, "artifacts", "snowball", "pending-fingerprints.json");

    public const string IdentitiesFile = "AnalysisIdentities.g.cs";

    /// <summary>Computes every generated file and the updated identities, without writing anything.</summary>
    public SnowballGeneration Plan(AnalysisVersionRequests requests)
    {
        var manifest = SnowballFixtureManifest.Load(FixtureRoot);
        string licence = ReadVerified(Path.Combine(FixtureRoot, "LICENSE-snowball.txt"), expectedSha256: null);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var codeSha = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var algorithm in manifest.Algorithms)
        {
            string reference = ReadVerified(Path.Combine(FixtureRoot, algorithm.Reference), algorithm.ReferenceSha256);
            ReadVerified(Path.Combine(FixtureRoot, algorithm.Sbl), algorithm.SblSha256);
            var result = SnowballTransform.Transform(algorithm.Name, reference, algorithm.SblSha256, licence);
            files[result.ClassName + ".g.cs"] = result.Source;
            codeSha[algorithm.Name] = result.BodySha256;
        }

        var current = AnalysisVersions.Parse(File.ReadAllText(VersionsPath, Encoding.UTF8));
        var (versions, changes) = AnalysisVersionPlanner.Apply(current, manifest.SnowballVersion, codeSha, requests);
        files[IdentitiesFile] = AnalysisVersionPlanner.RenderIdentities(versions);
        return new SnowballGeneration(files, versions, changes);
    }

    /// <summary>Writes a planned generation to the repository.</summary>
    public void Write(SnowballGeneration generation)
    {
        Directory.CreateDirectory(GeneratedRoot);
        foreach (var (name, content) in generation.Files)
            WriteIfChanged(Path.Combine(GeneratedRoot, name), content);
        WriteIfChanged(VersionsPath, generation.Versions.ToJson());
    }

    /// <summary>Returns every difference between a fresh generation and the committed files.</summary>
    public IReadOnlyList<string> Verify()
    {
        var problems = new List<string>();
        SnowballGeneration generation;
        try
        {
            generation = Plan(AnalysisVersionRequests.None);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or SnowballTransformException or IOException)
        {
            problems.Add(exception.Message);
            return problems;
        }

        foreach (var change in generation.Changes)
            problems.Add("analysis-versions.json is not current: " + change);
        foreach (var (name, content) in generation.Files)
        {
            string path = Path.Combine(GeneratedRoot, name);
            if (!File.Exists(path))
                problems.Add($"{name} is missing.");
            else if (!string.Equals(Normalise(File.ReadAllText(path, Encoding.UTF8)), content, StringComparison.Ordinal))
                problems.Add($"{name} differs from a fresh generation. Do not edit generated files; run './devops snowball generate'.");
        }

        if (Directory.Exists(GeneratedRoot))
        {
            foreach (var path in Directory.EnumerateFiles(GeneratedRoot, "*.cs"))
            {
                if (!generation.Files.ContainsKey(Path.GetFileName(path)))
                    problems.Add($"{Path.GetFileName(path)} is not produced by the generator.");
            }
        }

        return problems;
    }

    /// <summary>Reads the pending fingerprints written by the analysis identity tests.</summary>
    public IReadOnlyDictionary<string, string> ReadPendingFingerprints()
    {
        if (!File.Exists(PendingFingerprintsPath))
        {
            throw new InvalidOperationException(
                $"No pending fingerprints at '{PendingFingerprintsPath}'. Run the analysis identity tests with " +
                "LEANCORPUS_UPDATE_ANALYSIS_FINGERPRINTS=1 first.");
        }

        var node = JsonNode.Parse(File.ReadAllText(PendingFingerprintsPath, Encoding.UTF8)) as JsonObject
            ?? throw new InvalidDataException($"'{PendingFingerprintsPath}' is not a JSON object.");
        return node.ToDictionary(static pair => pair.Key, static pair => pair.Value!.GetValue<string>(), StringComparer.Ordinal);
    }

    /// <summary>Downloads the pinned word lists that are not committed, verifying every file's SHA-256.</summary>
    public async Task<IReadOnlyList<string>> FetchDataAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var manifest = SnowballFixtureManifest.Load(FixtureRoot);
        string root = Path.Combine(RepositoryRoot, "artifacts", "snowball-data", manifest.DataRevision);
        var written = new List<string>();
        foreach (var download in manifest.Downloads)
        {
            string target = Path.Combine(root, download.Path);
            if (File.Exists(target) && Sha256(await File.ReadAllBytesAsync(target, cancellationToken)) == download.Sha256)
            {
                written.Add($"{download.Path}: already present and verified.");
                continue;
            }

            var url = new Uri($"https://raw.githubusercontent.com/snowballstem/snowball-data/{manifest.DataRevision}/{download.Path}");
            byte[] content = await client.GetByteArrayAsync(url, cancellationToken);
            string actual = Sha256(content);
            if (actual != download.Sha256)
            {
                throw new InvalidDataException(
                    $"{url} has SHA-256 {actual}, not the pinned {download.Sha256}. Nothing was written for this file.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temporary = target + ".partial";
            await File.WriteAllBytesAsync(temporary, content, cancellationToken);
            File.Move(temporary, target, overwrite: true);
            written.Add($"{download.Path}: downloaded and verified ({content.Length} bytes).");
        }
        return written;
    }

    private static string ReadVerified(string path, string? expectedSha256)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (expectedSha256 is not null && Sha256(bytes) != expectedSha256)
            throw new InvalidDataException($"'{path}' does not match its pinned SHA-256 {expectedSha256}.");
        return Normalise(new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'));
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && Normalise(File.ReadAllText(path, Encoding.UTF8)) == content)
            return;
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

/// <summary>The planned output of one generation.</summary>
internal sealed record SnowballGeneration(
    IReadOnlyDictionary<string, string> Files, AnalysisVersions Versions, IReadOnlyList<string> Changes);

/// <summary>The parts of the fixture <c>manifest.json</c> the generator needs.</summary>
internal sealed record SnowballFixtureManifest(
    string SnowballVersion,
    string DataRevision,
    IReadOnlyList<SnowballFixtureAlgorithm> Algorithms,
    IReadOnlyList<SnowballDownload> Downloads)
{
    public static SnowballFixtureManifest Load(string fixtureRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, "manifest.json")));
        var root = document.RootElement;
        string tag = root.GetProperty("snowballTag").GetString()!;
        var algorithms = new List<SnowballFixtureAlgorithm>();
        var downloads = new List<SnowballDownload>();
        foreach (var element in root.GetProperty("algorithms").EnumerateArray())
        {
            algorithms.Add(new SnowballFixtureAlgorithm(
                element.GetProperty("name").GetString()!,
                element.GetProperty("sbl").GetString()!,
                element.GetProperty("sblSha256").GetString()!,
                element.GetProperty("reference").GetString()!,
                element.GetProperty("referenceSha256").GetString()!));
            var vectors = element.GetProperty("vectors");
            if (vectors.GetProperty("source").GetString() == "download")
            {
                downloads.Add(new SnowballDownload(vectors.GetProperty("vocPath").GetString()!, vectors.GetProperty("vocSha256").GetString()!));
                downloads.Add(new SnowballDownload(vectors.GetProperty("outputPath").GetString()!, vectors.GetProperty("outputSha256").GetString()!));
            }
        }

        return new SnowballFixtureManifest(
            tag.TrimStart('v'), root.GetProperty("snowballDataRevision").GetString()!, algorithms, downloads);
    }
}

/// <summary>One pinned algorithm's sources.</summary>
internal sealed record SnowballFixtureAlgorithm(string Name, string Sbl, string SblSha256, string Reference, string ReferenceSha256);

/// <summary>One word-list file downloaded from snowball-data at the pinned revision.</summary>
internal sealed record SnowballDownload(string Path, string Sha256);
