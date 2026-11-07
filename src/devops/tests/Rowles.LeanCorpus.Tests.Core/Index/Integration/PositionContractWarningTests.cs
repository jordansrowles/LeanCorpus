using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;
using Rowles.LeanCorpus.Cli;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
[Area(TestArea.Search)]
public sealed class PositionContractWarningTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "position-warning-" + Guid.NewGuid().ToString("N"));
    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);

    private void Build(bool legacy, bool compound = false)
    {
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance, UseCompoundFile = compound
        }))
        {
            for (int i = 0; i < 2; i++)
            {
                var document = new LeanDocument();
                document.Add(new TextField("body", "said the government"));
                writer.AddDocument(document);
            }
            writer.Commit();
        }
        if (legacy)
            foreach (string file in Directory.GetFiles(_path, "*.seg"))
            {
                var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
                json.Remove("PositionContract");
                File.WriteAllText(file, json.ToJsonString());
            }
    }

    private static void AssertWarning(IndexCheckIssue issue)
    {
        Assert.Equal("LLIDX052", issue.Code);
        Assert.Equal(IndexCheckSeverity.Warning, issue.Severity);
        Assert.False(issue.IsRepairable);
        Assert.EndsWith(".seg", issue.FileName);
        Assert.Contains("ReindexRecommended", issue.Message);
        Assert.Contains("may be affected", issue.Message);
        Assert.Contains("original text", issue.Message);
        Assert.Contains(issue.SuggestedActions, action => action.Contains("original text", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyWarningKeepsCompatibilityAndStrictWriting(bool compound)
    {
        Build(legacy: true, compound);
        using var directory = new MMapDirectory(_path);
        var check = IndexValidator.Check(directory);
        Assert.True(check.IsHealthy);
        Assert.Equal(2, check.DetailedIssues.Count);
        Assert.All(check.DetailedIssues, AssertWarning);
        var compatibility = IndexCompatibility.Check(directory);
        Assert.Equal(IndexCompatibilityStatus.Compatible, compatibility.Status);
        Assert.True(compatibility.CanWrite);
        Assert.Equal(2, compatibility.Issues.Count);
        var plan = IndexCodecMigrator.Plan(directory);
        Assert.True(plan.CanExecute);
        Assert.Equal(2, plan.Issues.Count);
        Assert.All(plan.Issues, AssertWarning);
        Assert.Empty(plan.Actions);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance });
        writer.Commit();
    }

    [Theory]
    [InlineData("check")]
    [InlineData("inspect")]
    [InlineData("compat")]
    [InlineData("migrate")]
    public void CliShowsReindexGuidance(string command)
    {
        Build(legacy: true);
        using var output = new StringWriter();
        using var error = new StringWriter();
        string[] arguments = command == "migrate" ? [command, _path, "--dry-run"] : [command, _path];
        Assert.Equal(0, IndexCheckerCli.Run(arguments, output, error));
        Assert.Empty(error.ToString());
        Assert.Contains("LLIDX052", output.ToString());
        Assert.Contains("original text", output.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplanationNotesLegacyPositionsWithoutChangingScore(bool legacy)
    {
        Build(legacy);
        using var directory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(directory);
        var explanation = searcher.Explain(new TermQuery("body", "said"), 0);
        Assert.NotNull(explanation);
        Assert.Equal(legacy, explanation.Details.Any(detail => detail.Description.Contains("LLIDX052", StringComparison.Ordinal)));
        Assert.Equal(searcher.Search(new TermQuery("body", "said"), 10, TestContext.Current.CancellationToken).ScoreDocs[0].Score,
            explanation.Score);
    }

    [Fact]
    public void InspectionCountsLegacySegments()
    {
        Build(legacy: true);
        var measurements = new ConcurrentBag<(long Value, int Segments)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Name == "leancorpus.index.position_contract.legacy_segments")
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "index.legacy_segment_count" && tag.Value is int count)
                    measurements.Add((value, count));
        });
        listener.Start();
        using var directory = new MMapDirectory(_path);
        IndexFormatInspector.Inspect(directory);
        Assert.Contains((2L, 2), measurements);
    }
}
