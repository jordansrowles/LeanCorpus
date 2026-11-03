using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.LeanCorpus.Tests.Core;

/// <summary>Extracts pre-retirement query inputs without invoking a current product writer.</summary>
internal static class HistoricalBbqIndexFixtures
{
    internal static string CopyLegacySignV3To(string destination)
    {
        string root = HistoricalIndexFixtures.Extract(
            HistoricalIndexFixture.ExperimentalBbqV3LegacySign, destination);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        string indexRoot = Path.Combine(root, "index");
        foreach (JsonElement file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            string relative = file.GetProperty("path").GetString()!;
            string source = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(file.GetProperty("length").GetInt64(), new FileInfo(source).Length);
            Assert.Equal(file.GetProperty("sha256").GetString(),
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
        }
        return indexRoot;
    }

    internal static void CopyTo(string destination, string caseName)
    {
        using var fixture = new TestDirectoryFixture();
        string root = HistoricalIndexFixtures.Extract(HistoricalIndexFixture.ExperimentalBbqQueryIndexes, fixture.Path);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        string prefix = "indexes/" + caseName + "/";
        var files = manifest.RootElement.GetProperty("files").EnumerateArray()
            .Where(file => file.GetProperty("path").GetString()!.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(files);
        Directory.CreateDirectory(destination);
        foreach (var file in files)
        {
            string relative = file.GetProperty("path").GetString()!;
            string source = Path.Combine(root, relative);
            Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
            string target = Path.Combine(destination, relative[prefix.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false);
        }
    }
}
