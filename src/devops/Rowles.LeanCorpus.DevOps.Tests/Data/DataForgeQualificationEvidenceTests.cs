using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Data;

namespace Rowles.LeanCorpus.DevOps.Tests.Data;

public sealed class DataForgeQualificationEvidenceTests
{
    [Fact]
    public void MtpTestEvidenceRequiresCompletedTrxAndZeroFailures()
    {
        using var directory = new TemporaryDirectory();
        var trxPath = Path.Combine(directory.Path, "results.trx");
        File.WriteAllText(trxPath, Trx("Completed", total: 2, failed: 0));

        var passed = DataForgeQualificationEvidence.ReadMtpTestTrx(trxPath);
        File.WriteAllText(trxPath, Trx("Completed", total: 2, failed: 1));
        var failed = DataForgeQualificationEvidence.ReadMtpTestTrx(trxPath);
        var missing = DataForgeQualificationEvidence.ReadMtpTestTrx(Path.Combine(directory.Path, "missing.trx"));

        Assert.True(passed.Passed, passed.Failure);
        Assert.Equal(2, passed.TotalTests);
        Assert.False(failed.Passed);
        Assert.False(missing.Passed);
        Assert.Contains("missing", missing.Failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReferenceIdentityRequiresThePinnedImportedWikipediaV1Manifest()
    {
        using var directory = new TemporaryDirectory();
        WriteReferenceManifest(directory.Path, datasetVersion: 1);

        var identity = DataForgeQualificationEvidence.ReadReferenceIdentity(directory.Path, out var failure);

        Assert.NotNull(identity);
        Assert.Null(failure);
        Assert.Equal("leancorpus-wikipedia-en", identity.DatasetId);
        Assert.Equal(20_000, identity.RecordCount);
        WriteReferenceManifest(directory.Path, datasetVersion: 2);
        Assert.Null(DataForgeQualificationEvidence.ReadReferenceIdentity(directory.Path, out failure));
        Assert.Contains("required 20,000-record", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void WikipediaReportMustMatchTheVerifiedReferenceIdentity()
    {
        using var directory = new TemporaryDirectory();
        var core000 = Directory.CreateDirectory(Path.Combine(directory.Path, "core", "000")).FullName;
        var core001 = Directory.CreateDirectory(Path.Combine(directory.Path, "core", "001")).FullName;
        var identity = new DataForgeWikipediaIdentity(
            "Imported", "leancorpus-wikipedia-en", 1, 20_000, 123, "a".PadRight(64, 'a'),
            "b".PadRight(64, 'b'), "20260901", "primary", "index");
        File.WriteAllText(Path.Combine(core000, "report.json"), BuildReport(identity.DatasetId));
        File.WriteAllText(Path.Combine(core001, "report.json"), BuildReport(identity.DatasetId));

        Assert.Null(DataForgeQualificationEvidence.ReadWikipediaReportError(directory.Path, identity));
        File.WriteAllText(Path.Combine(core001, "report.json"), BuildReport("other-dataset"));
        Assert.Contains("does not match", DataForgeQualificationEvidence.ReadWikipediaReportError(directory.Path, identity), StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptSyntheticIdentityIsAQualificationFailure()
    {
        using var directory = new TemporaryDirectory();
        var dataForge = Directory.CreateDirectory(Path.Combine(directory.Path, "core", "dataforge")).FullName;
        File.WriteAllText(Path.Combine(dataForge, "identity.json"), """
            { "sourceKind": "Generated", "dataForgeVersion": 1, "profileId": "leancorpus-search", "profileVersion": 1,
              "seed": 42, "recordCount": 100, "parameters": {}, "contentSha256": "short" }
            """);

        var result = DataForgeQualificationEvidence.ReadSyntheticIdentities(directory.Path, "benchmark-run");

        Assert.Empty(result.Identities);
        Assert.Single(result.Failures);
    }

    private static void WriteReferenceManifest(string path, int datasetVersion)
    {
        File.WriteAllText(Path.Combine(path, "manifest.json"), $$"""
            {
              "sourceKind": "Imported",
              "source": { "datasetId": "leancorpus-wikipedia-en", "datasetVersion": "{{datasetVersion}}", "dumpDate": "20260901", "primarySha1": "primary", "indexSha1": "index" },
              "recordCount": 20000,
              "logicalByteCount": 123,
              "contentSha256": "{{new string('a', 64)}}",
              "artefactSha256": "{{new string('b', 64)}}"
            }
            """);
    }

    private static string BuildReport(string datasetId)
        => JsonSerializer.Serialize(new
        {
            schemaVersion = 4,
            provenance = new
            {
                datasets = new[]
                {
                    new { sourceKind = "Imported", dataForgeVersion = 1, datasetId, datasetVersion = 1, recordCount = 20_000, contentSha256 = new string('a', 64) }
                }
            }
        });

    private static string Trx(string outcome, int total, int failed)
        => $"""<TestRun><ResultSummary outcome="{outcome}"><Counters total="{total}" executed="{total}" passed="{total - failed}" failed="{failed}" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" /></ResultSummary></TestRun>""";

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LeanCorpus DataForge qualification " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
