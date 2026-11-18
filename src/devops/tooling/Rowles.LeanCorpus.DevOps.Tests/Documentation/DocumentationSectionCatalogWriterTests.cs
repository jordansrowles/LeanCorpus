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
        foreach (var stream in new[] { "engine", "text", "server", "devops" })
            Directory.CreateDirectory(Path.Combine(changelog, stream));
        File.WriteAllText(Path.Combine(changelog, "engine", "unreleased.md"), "# LeanCorpus unreleased\n");
        File.WriteAllText(Path.Combine(changelog, "engine", "3.1.1 - 2026-09-13.md"), "# LeanCorpus 3.1.1\n");
        File.WriteAllText(Path.Combine(changelog, "engine", "2.3.0 - 2026-08-08.md"), "# LeanCorpus 2.3.0\n");
        File.WriteAllText(Path.Combine(changelog, "text", "unreleased.md"), "# Rowles.Text unreleased\n");
        File.WriteAllText(Path.Combine(changelog, "text", "1.0.0 - 2026-08-08.md"), "# Rowles.Text 1.0.0\n");
        File.WriteAllText(Path.Combine(changelog, "server", "0.1.0-alpha.1 - unreleased.md"), "# Community Server alpha\n");
        File.WriteAllText(Path.Combine(changelog, "devops", "unreleased.md"), "# DevOps unreleased\n");

        new DocumentationSectionCatalogWriter().Generate(changelog, "Changelog",
            "Versioned product release notes.", separateServerChangelog: true);

        var toc = File.ReadAllText(Path.Combine(changelog, "toc.yml"));
        // The writer uses platform line endings; compare headings independently of them.
        var index = File.ReadAllText(Path.Combine(changelog, "index.md")).ReplaceLineEndings("\n");
        string[] streams = ["LeanCorpus", "Rowles.Text", "Community Server", "LeanCorpus DevOps"];
        foreach (var stream in streams)
        {
            Assert.Contains($"name: '{stream}'", toc, StringComparison.Ordinal);
            Assert.Contains($"## {stream}\n", index, StringComparison.Ordinal);
        }

        Assert.True(index.IndexOf("## LeanCorpus\n", StringComparison.Ordinal) < index.IndexOf("## Rowles.Text\n", StringComparison.Ordinal));
        Assert.Contains("href: 'engine/unreleased.md'", toc, StringComparison.Ordinal);
        Assert.Contains("href: 'text/1.0.0 - 2026-08-08.md'", toc, StringComparison.Ordinal);
        Assert.Contains("name: 'Unreleased'", toc, StringComparison.Ordinal);
        Assert.Contains("name: '3.x'", toc, StringComparison.Ordinal);
        Assert.Contains("name: '2.x'", toc, StringComparison.Ordinal);
        Assert.Contains("name: '1.x'", toc, StringComparison.Ordinal);
        Assert.Contains("name: '0.x'", toc, StringComparison.Ordinal);
        Assert.True(toc.IndexOf("name: '3.x'", StringComparison.Ordinal) < toc.IndexOf("name: '2.x'", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsChangelogPagesOutsideAReleaseStream()
    {
        using var directory = new TemporaryDirectory();
        var changelog = Path.Combine(directory.Path, "changelog");
        Directory.CreateDirectory(Path.Combine(changelog, "engine"));
        File.WriteAllText(Path.Combine(changelog, "engine", "unreleased.md"), "# LeanCorpus unreleased\n");
        File.WriteAllText(Path.Combine(changelog, "3.1.1 - 2026-09-13.md"), "# Orphaned 3.1.1\n");

        var exception = Assert.Throws<InvalidDataException>(() => new DocumentationSectionCatalogWriter().Generate(
            changelog, "Changelog", "Versioned product release notes.", separateServerChangelog: true));
        Assert.Contains("3.1.1 - 2026-09-13.md", exception.Message, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = RepositoryTemporaryStorage.NewPath("devops-tests", "leancorpus-docs-toc-test-");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
