using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.LeanCorpus.HistoricalFixtures;

internal static class FixtureOutput
{
    public static string Sha256(ReadOnlySpan<byte> bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static void WriteInventoryManifest(string root, string manifestPath)
    {
        string fullRoot = Path.GetFullPath(root);
        string fullManifest = Path.GetFullPath(manifestPath);
        var files = Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), fullManifest, StringComparison.Ordinal))
            .Select(path =>
            {
                string name = Path.GetRelativePath(fullRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                byte[] bytes = File.ReadAllBytes(path);
                return new { Name = name, Length = bytes.LongLength, Sha256 = Sha256(bytes) };
            })
            .OrderBy(static file => file.Name, StringComparer.Ordinal)
            .ToArray();

        var manifest = new
        {
            FixtureFormatVersion = 1,
            LeanCorpusPackageVersion = "3.1.1",
            GeneratorSdk = "10.0.103",
            Files = files,
        };
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
