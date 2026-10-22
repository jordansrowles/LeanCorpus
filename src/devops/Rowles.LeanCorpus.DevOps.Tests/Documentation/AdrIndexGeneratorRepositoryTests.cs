using Rowles.LeanCorpus.DevOps.Documentation;

namespace Rowles.LeanCorpus.DevOps.Tests.Documentation;

public sealed class AdrIndexGeneratorRepositoryTests
{
    [Fact(Explicit = true)]
    public void GeneratesIndexForTheCurrentRepositoryAdrGraph()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = Path.Combine(repositoryRoot, "docs", "content", "articles", "ADRs");
        using var directory = new TemporaryDirectory();
        var staged = Path.Combine(directory.Path, "ADRs");
        new DocumentationContentStager().CopyCanonicalContent(source, staged);

        var count = new AdrIndexGenerator().Generate(source, Path.Combine(staged, "index.md"), staged);

        Assert.True(count >= 40, $"Expected the repository ADR graph, found only {count} records.");
        var index = File.ReadAllText(Path.Combine(staged, "index.md"));
        Assert.Contains("Community Server decisions", index, StringComparison.Ordinal);
        Assert.Contains("ADR041", index, StringComparison.Ordinal);
        Assert.Contains("use the reserved ADRnnn sequence; Server identifiers use ADR-Snnn.", index, StringComparison.Ordinal);
        Assert.DoesNotContain("`ADRnnn`", index, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, index, StringComparison.Ordinal);
        Assert.False(index.EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(Environment.CurrentDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "docs", "site-config", "imports.json"))) return current.FullName;
        }
        throw new DirectoryNotFoundException("Could not find the LeanCorpus repository from the test working directory.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "leancorpus-adr-graph-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
