using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rowles.LeanCorpus.Tests.Core;

/// <summary>Copies pinned pre-retirement bytes. No encoding or fallback writer exists in this helper.</summary>
internal static class HistoricalBbqVectorFixtures
{
    internal static void CopyOsq(string path, int count, int dimension, [CallerMemberName] string caseName = "")
        => Copy(path, "osq-source", count, dimension, caseName);

    internal static void CopyLegacyDictionary(string path, int count, int dimension, [CallerMemberName] string caseName = "")
        => Copy(path, "legacy-dictionary", count, dimension, caseName);

    internal static void CopyLegacySource(string path, int count, int dimension, [CallerMemberName] string caseName = "")
        => Copy(path, "legacy-source", count, dimension, caseName);

    private static void Copy(string destination, string kind, int count, int dimension, string caseName)
    {
        using var fixture = new TestDirectoryFixture();
        string root = HistoricalIndexFixtures.Extract(HistoricalIndexFixture.ExperimentalBbqCodecBodies, fixture.Path);
        string name = Key(kind, count, dimension, caseName) + ".vq";
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        string expected = manifest.RootElement.GetProperty("files").EnumerateArray()
            .Single(value => value.GetProperty("path").GetString() == name).GetProperty("sha256").GetString()!;
        string source = Path.Combine(root, name);
        Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
        File.Copy(source, destination, overwrite: true);
    }

    private static string Key(string kind, int count, int dimension, string caseName)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"{kind}:{count}:{dimension}:{caseName}"))));
}
