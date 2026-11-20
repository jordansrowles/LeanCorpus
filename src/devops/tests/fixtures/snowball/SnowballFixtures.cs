using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Stemming;

/// <summary>
/// Reads the pinned Snowball 3.2.0 test fixtures described by <c>manifest.json</c>. Every file is
/// checked against its recorded SHA-256 before use; a missing or altered file fails, never skips.
/// </summary>
/// <remarks>
/// Shared by source inclusion into the Rowles.Text and Core test projects. Vendored vectors are the
/// BSD-only snowball-data lists. French and Italian include CC BY-SA words, so they are downloaded
/// by <c>./devops snowball fetch-data</c> instead of committed. The official Arabic list is GPLv3 and
/// is not used; Arabic has a table written for LeanCorpus whose expected stems come from Snowball's
/// C implementation.
/// </remarks>
internal static class SnowballFixtures
{
    private static readonly Lazy<Manifest> LazyManifest = new(LoadManifest);
    private static readonly Dictionary<string, IReadOnlyList<(string Input, string Expected)>> Cache = new(StringComparer.Ordinal);
    private static readonly object CacheLock = new();

    /// <summary>The fixture directory in the repository.</summary>
    public static string FixtureRoot => Path.Combine(
        RepositoryTemporaryStorage.RepositoryRoot, "src", "devops", "tests", "fixtures", "snowball");

    /// <summary>Where <c>./devops snowball fetch-data</c> stores downloaded word lists.</summary>
    public static string DownloadRoot => Path.Combine(
        RepositoryTemporaryStorage.RepositoryRoot, "artifacts", "snowball-data", LazyManifest.Value.DataRevision);

    /// <summary>The algorithms with a pinned word list: official (vendored or downloaded) or, for Arabic, written for LeanCorpus.</summary>
    public static IReadOnlyList<string> WordListAlgorithms =>
        LazyManifest.Value.Algorithms.Where(static a => a.Source != "none").Select(static a => a.Name).ToArray();

    /// <summary>The algorithms whose word list is committed to the repository.</summary>
    public static IReadOnlyList<string> VendoredAlgorithms =>
        LazyManifest.Value.Algorithms.Where(static a => a.Source == "vendored").Select(static a => a.Name).ToArray();

    /// <summary>All nine target algorithms.</summary>
    public static IReadOnlyList<string> Algorithms =>
        LazyManifest.Value.Algorithms.Select(static a => a.Name).ToArray();

    /// <summary>Returns the verified (input, expected stem) pairs for <paramref name="algorithm"/>.</summary>
    public static IReadOnlyList<(string Input, string Expected)> Vectors(string algorithm)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(algorithm, out var cached))
                return cached;
            var entry = LazyManifest.Value.Algorithms.SingleOrDefault(a => a.Name == algorithm)
                ?? throw new ArgumentException($"'{algorithm}' is not a pinned Snowball algorithm.", nameof(algorithm));
            var pairs = entry.Source switch
            {
                "vendored" => ReadVendored(entry),
                "pinned" => ReadPinned(entry),
                "download" => ReadDownloaded(entry),
                _ => throw new InvalidOperationException(
                    $"Snowball '{algorithm}' has no official word list in this repository: {entry.Reason}"),
            };
            Cache[algorithm] = pairs;
            return pairs;
        }
    }

    private static IReadOnlyList<(string Input, string Expected)> ReadVendored(Entry entry)
    {
        string path = Path.Combine(FixtureRoot, entry.File!);
        byte[] tsv;
        using (var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress))
        using (var buffer = new MemoryStream())
        {
            gzip.CopyTo(buffer);
            tsv = buffer.ToArray();
        }
        return ReadTsv(tsv, entry, path);
    }

    private static IReadOnlyList<(string Input, string Expected)> ReadPinned(Entry entry)
    {
        string path = Path.Combine(FixtureRoot, entry.File!);
        return ReadTsv(File.ReadAllBytes(path), entry, path);
    }

    private static IReadOnlyList<(string Input, string Expected)> ReadTsv(byte[] tsv, Entry entry, string path)
    {
        Verify(tsv, entry.TsvSha256!, path);
        var pairs = new List<(string, string)>(entry.Rows);
        foreach (var line in Lines(tsv))
        {
            int tab = line.IndexOf('\t', StringComparison.Ordinal);
            if (tab < 0)
                throw new InvalidDataException($"{path}: line without a tab.");
            pairs.Add((line[..tab], line[(tab + 1)..]));
        }
        return CheckRows(pairs, entry, path);
    }

    private static IReadOnlyList<(string Input, string Expected)> ReadDownloaded(Entry entry)
    {
        string voc = Path.Combine(DownloadRoot, entry.VocPath!);
        string output = Path.Combine(DownloadRoot, entry.OutputPath!);
        if (!File.Exists(voc) || !File.Exists(output))
        {
            throw new InvalidOperationException(
                $"The Snowball '{entry.Name}' word list is downloaded, not committed, and is missing from '{DownloadRoot}'. " +
                "Run './devops snowball fetch-data' first.");
        }

        byte[] vocBytes = File.ReadAllBytes(voc);
        byte[] outputBytes = File.ReadAllBytes(output);
        Verify(vocBytes, entry.VocSha256!, voc);
        Verify(outputBytes, entry.OutputSha256!, output);
        var inputs = Lines(vocBytes).ToArray();
        var expected = Lines(outputBytes).ToArray();
        if (inputs.Length != expected.Length)
            throw new InvalidDataException($"'{entry.Name}': {inputs.Length} inputs but {expected.Length} expected stems.");
        return CheckRows(inputs.Zip(expected).Select(static p => (p.First, p.Second)).ToList(), entry, voc);
    }

    private static IReadOnlyList<(string Input, string Expected)> CheckRows(
        List<(string Input, string Expected)> pairs, Entry entry, string path)
    {
        if (pairs.Count != entry.Rows)
            throw new InvalidDataException($"{path}: {pairs.Count} rows, but the manifest records {entry.Rows}.");
        return pairs;
    }

    private static IEnumerable<string> Lines(byte[] utf8)
    {
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(utf8);
        var lines = text.Split('\n');
        // Every file ends with a newline, so the final element is empty.
        return lines.Take(lines.Length - 1);
    }

    private static void Verify(byte[] content, string expectedSha256, string path)
    {
        string actual = Convert.ToHexStringLower(SHA256.HashData(content));
        if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{path}: SHA-256 {actual} does not match the pinned {expectedSha256}. Restore the pinned file.");
        }
    }

    private static Manifest LoadManifest()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureRoot, "manifest.json")));
        var root = document.RootElement;
        var algorithms = new List<Entry>();
        foreach (var element in root.GetProperty("algorithms").EnumerateArray())
        {
            var vectors = element.GetProperty("vectors");
            algorithms.Add(new Entry(
                element.GetProperty("name").GetString()!,
                vectors.GetProperty("source").GetString()!,
                Optional(vectors, "file"),
                Optional(vectors, "tsvSha256"),
                Optional(vectors, "vocPath"),
                Optional(vectors, "vocSha256"),
                Optional(vectors, "outputPath"),
                Optional(vectors, "outputSha256"),
                vectors.TryGetProperty("rows", out var rows) ? rows.GetInt32() : 0,
                Optional(vectors, "reason")));
        }
        return new Manifest(root.GetProperty("snowballDataRevision").GetString()!, algorithms);
    }

    private static string? Optional(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? value.GetString() : null;

    private sealed record Manifest(string DataRevision, IReadOnlyList<Entry> Algorithms);

    private sealed record Entry(
        string Name, string Source, string? File, string? TsvSha256, string? VocPath, string? VocSha256,
        string? OutputPath, string? OutputSha256, int Rows, string? Reason);
}
