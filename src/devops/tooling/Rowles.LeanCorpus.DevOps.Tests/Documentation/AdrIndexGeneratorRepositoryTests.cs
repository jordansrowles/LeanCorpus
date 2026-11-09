using Rowles.LeanCorpus.DevOps.Documentation;

namespace Rowles.LeanCorpus.DevOps.Tests.Documentation;

public sealed class AdrIndexGeneratorRepositoryTests
{
    [Fact]
    public void GeneratesIndexForAProposedAdr()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "ADRs");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "ADR001-proposed.md"), """
            ---
            adr: ADR001
            title: "Proposed decision"
            date: 2026-01-01
            status: Proposed
            version-added: 4.0.0
            summary: "Decision awaiting qualification."
            areas: [vectors]
            ---

            # ADR001: Proposed decision

            ## Changes

            | Date | Type | Change | Reference |
            |---|---|---|---|
            | 2026-01-01 | Proposed | Record the decision for review | [Plan](https://example.com/plan) |
            """);
        var outputPath = Path.Combine(directory.Path, "generated", "index.md");

        var count = new AdrIndexGenerator().Generate(source, outputPath);

        Assert.Equal(1, count);
        Assert.Contains("<td>Proposed</td>", File.ReadAllText(outputPath), StringComparison.Ordinal);
        Assert.Contains("ADR001: Proposed decision (Proposed)",
            File.ReadAllText(Path.Combine(directory.Path, "generated", "toc.yml")), StringComparison.Ordinal);
    }

    [Fact]
    public void ProposedSupersessionDoesNotRequireRewritingAcceptedHistoricalAdr()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "ADRs");
        Directory.CreateDirectory(source);
        var historicalPath = Path.Combine(source, "ADR001-historical.md");
        var historical = """
            ---
            adr: ADR001
            title: "Historical decision"
            date: 2026-01-01
            status: Accepted
            version-added: 3.1.0
            summary: "Previously accepted decision."
            areas: [vectors]
            ---

            # ADR001: Historical decision

            ## Changes

            | Date | Type | Change | Reference |
            |---|---|---|---|
            | 2026-01-01 | Accepted | Original decision accepted. | `abc1234` |
            """;
        File.WriteAllText(historicalPath, historical);
        File.WriteAllText(Path.Combine(source, "ADR002-proposed.md"), """
            ---
            adr: ADR002
            title: "Proposed replacement"
            date: 2026-02-01
            status: Proposed
            version-added: 4.0.0
            summary: "Replacement awaiting qualification."
            areas: [vectors]
            supersedes: [ADR001]
            ---

            # ADR002: Proposed replacement

            ## Changes

            | Date | Type | Change | Reference |
            |---|---|---|---|
            | 2026-02-01 | Proposed | Record the replacement for review. | [Plan](https://example.com/plan) |
            """);

        var outputPath = Path.Combine(directory.Path, "generated", "index.md");
        var count = new AdrIndexGenerator().Generate(source, outputPath);

        Assert.Equal(2, count);
        Assert.Contains("ADR002: Proposed replacement (Proposed)",
            File.ReadAllText(Path.Combine(directory.Path, "generated", "toc.yml")), StringComparison.Ordinal);
        Assert.Equal(historical, File.ReadAllText(historicalPath));
    }

    [Fact]
    public void AcceptedSupersessionDerivesHistoricalStatusWithoutRewritingSourceAdr()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "ADRs");
        var staged = Path.Combine(directory.Path, "staged");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(staged);
        var historicalName = "ADR001-historical.md";
        var acceptedName = "ADR002-replacement.md";
        var historical = """
            ---
            adr: ADR001
            title: "Historical decision"
            date: 2026-01-01
            status: Accepted
            version-added: 3.1.0
            summary: "Previously accepted decision."
            areas: [vectors]
            ---

            # ADR001: Historical decision

            ## Context

            Historical context.

            ## Changes

            | Date | Type | Change | Reference |
            |---|---|---|---|
            | 2026-01-01 | Accepted | Original decision accepted. | `abc1234` |
            """;
        var accepted = """
            ---
            adr: ADR002
            title: "Accepted replacement"
            date: 2026-02-01
            last-updated: 2026-02-10
            status: Accepted
            version-added: 4.0.0
            summary: "Replacement accepted after review."
            areas: [vectors]
            supersedes: [ADR001]
            ---

            # ADR002: Accepted replacement

            ## Context

            Replacement context.

            ## Changes

            | Date | Type | Change | Reference |
            |---|---|---|---|
            | 2026-02-01 | Proposed | Original proposal recorded. | [Plan](https://example.com/plan) |
            | 2026-02-10 | Accepted | Replacement decision accepted. | `def5678` |
            """;
        File.WriteAllText(Path.Combine(source, historicalName), historical);
        File.WriteAllText(Path.Combine(source, acceptedName), accepted);
        File.WriteAllText(Path.Combine(staged, historicalName), historical);
        File.WriteAllText(Path.Combine(staged, acceptedName), accepted);
        var outputPath = Path.Combine(staged, "index.md");

        var count = new AdrIndexGenerator().Generate(source, outputPath, staged);

        Assert.Equal(2, count);
        Assert.Contains("ADR001: Historical decision (Superseded)",
            File.ReadAllText(Path.Combine(staged, "toc.yml")), StringComparison.Ordinal);
        Assert.Contains("<td>Superseded</td>", File.ReadAllText(outputPath), StringComparison.Ordinal);
        Assert.Contains("- **Superseded by:** <a href=\"ADR002-replacement.md\">ADR002</a>",
            File.ReadAllText(Path.Combine(staged, historicalName)), StringComparison.Ordinal);
        Assert.Equal(historical, File.ReadAllText(Path.Combine(source, historicalName)));
    }

    [Fact(Explicit = true)]
    public void GeneratesIndexForTheCurrentRepositoryAdrGraph()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = Path.Combine(repositoryRoot, "docs", "content", "articles", "ADRs");
        using var directory = new TemporaryDirectory();
        var staged = Path.Combine(directory.Path, "ADRs");
        new DocumentationContentStager().CopyCanonicalContent(source, staged);

        var count = new AdrIndexGenerator().Generate(source, Path.Combine(staged, "index.md"), staged);
        var toc = File.ReadAllText(Path.Combine(staged, "toc.yml"));

        Assert.True(count >= 40, $"Expected the repository ADR graph, found only {count} records.");
        var index = File.ReadAllText(Path.Combine(staged, "index.md"));
        Assert.Contains("Community Server decisions", index, StringComparison.Ordinal);
        Assert.Contains("ADR041", index, StringComparison.Ordinal);
        Assert.Contains("use the reserved ADRnnn sequence; Server identifiers use ADR-Snnn.", index, StringComparison.Ordinal);
        Assert.DoesNotContain("`ADRnnn`", index, StringComparison.Ordinal);
        Assert.DoesNotContain("_template.md", index, StringComparison.Ordinal);
        Assert.Contains("ADR041-050", toc, StringComparison.Ordinal);
        Assert.Contains("ADR041: Physical indexes have one composed runtime owner (Accepted)", toc, StringComparison.Ordinal);
        Assert.DoesNotContain("Shared ADR template", toc, StringComparison.Ordinal);
        Assert.DoesNotContain("_template.md", toc, StringComparison.Ordinal);
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
            Path = System.IO.Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("devops-tests"), "leancorpus-adr-graph-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
