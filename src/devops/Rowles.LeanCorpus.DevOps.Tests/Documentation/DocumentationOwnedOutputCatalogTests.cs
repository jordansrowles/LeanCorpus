using Rowles.LeanCorpus.DevOps.Documentation;

namespace Rowles.LeanCorpus.DevOps.Tests.Documentation;

public sealed class DocumentationOwnedOutputCatalogTests
{
    [Fact]
    public void EnumeratesOnlyOwnedOutputsInStableManifestOrder()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Write(root, "content/z.md");
            Write(root, "content/a.md");
            Write(root, "content/nested/page.md");
            Write(root, "api/reference.yml");
            Write(root, "generated/site-metadata.json");
            Write(root, "unowned/ignored.txt");

            var paths = DocumentationOwnedOutputCatalog.EnumerateFiles(root, TestContext.Current.CancellationToken)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'));

            Assert.Equal(
                ["content/a.md", "content/nested/page.md", "content/z.md", "api/reference.yml", "generated/site-metadata.json"],
                paths);
            Assert.Equal(["content", "api", "site", "coverage", "generated"],
                DocumentationOwnedOutputCatalog.All.Select(static output => output.RelativeRoot));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsLinkedDirectoriesBeforeEnumeratingTheirContents()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTemporaryDirectory();
        var outside = CreateTemporaryDirectory();
        try
        {
            Write(outside, "secret.txt");
            Directory.CreateDirectory(Path.Combine(root, "content"));
            Directory.CreateSymbolicLink(Path.Combine(root, "content", "linked"), outside);

            var exception = Assert.Throws<InvalidDataException>(() =>
                DocumentationOwnedOutputCatalog.EnumerateFiles(root, TestContext.Current.CancellationToken));

            Assert.Contains("linked path", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(outside, "secret.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "LeanCorpus docs output " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Write(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relativePath);
    }
}
