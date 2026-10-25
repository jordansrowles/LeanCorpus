using Rowles.LeanCorpus.DevOps.Documentation;

namespace Rowles.LeanCorpus.DevOps.Tests.Documentation;

public sealed class DocumentationSectionCatalogWriterTests
{
    [Fact]
    public void GeneratesOrderedNestedTocsFromStagedPublicContent()
    {
        using var directory = new TemporaryDirectory();
        var searching = Path.Combine(directory.Path, "searching");
        var examples = Path.Combine(searching, "examples");
        var adrs = Path.Combine(directory.Path, "articles", "ADRs");
        Directory.CreateDirectory(examples);
        Directory.CreateDirectory(adrs);
        File.WriteAllText(Path.Combine(searching, "index.md"), "# Searching\n");
        File.WriteAllText(Path.Combine(searching, "02-query-parser.md"), "# The query parser\n");
        File.WriteAllText(Path.Combine(searching, "01-query-types.md"), "# Query types\n");
        File.WriteAllText(Path.Combine(examples, "index.md"), "# Search examples\n");
        File.WriteAllText(Path.Combine(examples, "01-phrases.md"), "# Phrase examples\n");
        File.WriteAllText(Path.Combine(adrs, "toc.yml"), "custom ADR catalogue\n");
        File.WriteAllText(Path.Combine(adrs, "ADR001-example.md"), "# Example ADR\n");

        new DocumentationSectionCatalogWriter().GenerateSectionTocs(directory.Path);

        var toc = File.ReadAllText(Path.Combine(searching, "toc.yml"));
        Assert.Contains("href: index.md", toc, StringComparison.Ordinal);
        Assert.True(toc.IndexOf("Query types", StringComparison.Ordinal) < toc.IndexOf("The query parser", StringComparison.Ordinal));
        Assert.Contains("href: 'examples/index.md'", toc, StringComparison.Ordinal);
        Assert.Contains("href: 'examples/01-phrases.md'", toc, StringComparison.Ordinal);
        Assert.Equal("custom ADR catalogue\n", File.ReadAllText(Path.Combine(adrs, "toc.yml")));
    }

    [Fact]
    public void GroupsChangelogsByProductAndMajorRelease()
    {
        using var directory = new TemporaryDirectory();
        var changelog = Path.Combine(directory.Path, "changelog");
        Directory.CreateDirectory(Path.Combine(changelog, "server"));
        Directory.CreateDirectory(Path.Combine(changelog, "devops"));
        File.WriteAllText(Path.Combine(changelog, "unreleased.md"), "# LeanCorpus unreleased\n");
        File.WriteAllText(Path.Combine(changelog, "3.1.1 - 2026-09-13.md"), "# LeanCorpus 3.1.1\n");
        File.WriteAllText(Path.Combine(changelog, "2.3.0 - 2026-08-08.md"), "# LeanCorpus 2.3.0\n");
        File.WriteAllText(Path.Combine(changelog, "server", "0.1.0-alpha.1 - unreleased.md"), "# Community Server alpha\n");
        File.WriteAllText(Path.Combine(changelog, "devops", "unreleased.md"), "# DevOps unreleased\n");

        new DocumentationSectionCatalogWriter().Generate(changelog, "Changelog",
            "Versioned product release notes.", separateServerChangelog: true);

        var toc = File.ReadAllText(Path.Combine(changelog, "toc.yml"));
        Assert.Contains("name: 'LeanCorpus and Rowles.Text'", toc, StringComparison.Ordinal);
        Assert.Contains("name: 'Community Server'", toc, StringComparison.Ordinal);
        Assert.Contains("name: 'LeanCorpus DevOps'", toc, StringComparison.Ordinal);
        Assert.Contains("name: 'Unreleased'", toc, StringComparison.Ordinal);
        Assert.Contains("name: '3.x'", toc, StringComparison.Ordinal);
        Assert.Contains("name: '2.x'", toc, StringComparison.Ordinal);
        Assert.Contains("name: '0.x'", toc, StringComparison.Ordinal);
        Assert.True(toc.IndexOf("name: '3.x'", StringComparison.Ordinal) < toc.IndexOf("name: '2.x'", StringComparison.Ordinal));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "leancorpus-docs-toc-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
