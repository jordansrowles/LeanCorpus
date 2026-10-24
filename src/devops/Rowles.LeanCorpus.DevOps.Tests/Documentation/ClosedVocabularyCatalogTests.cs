using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Diagnostics;
using Rowles.LeanCorpus.DevOps.Documentation;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Reporting;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Documentation;

public sealed class ClosedVocabularyCatalogTests
{
    [Fact]
    public void DocumentationActionsKeepTheirCliNamesAndCaseInsensitiveParsing()
    {
        Assert.Equal(["prepare", "metadata", "build", "test", "serve"], DocumentationActionCatalog.Names);
        Assert.Equal("prepare, metadata, build, test or serve", DocumentationActionCatalog.ExpectedNames);
        foreach (var name in DocumentationActionCatalog.Names)
        {
            Assert.True(DocumentationActionCatalog.TryParse(name.ToUpperInvariant(), out var action));
            Assert.Equal(name, DocumentationActionCatalog.GetName(action));
        }
        Assert.False(DocumentationActionCatalog.TryParse("publish", out _));
    }

    [Fact]
    public void ReportGroupsKeepTheirHistoricalNamesAndAliases()
    {
        Assert.Equal(["all", "git", "files", "code"], RepositoryReportGroupCatalog.Names);
        foreach (var name in RepositoryReportGroupCatalog.Names)
        {
            Assert.True(RepositoryReportGroupCatalog.TryParse(name.ToUpperInvariant(), out var group));
            Assert.Equal(name, RepositoryReportGroupCatalog.GetName(group));
        }
        Assert.False(RepositoryReportGroupCatalog.TryParse("all-with-explicit", out _));
    }

    [Fact]
    public void DiagnosticsDumpTypesKeepDotnetDumpOptionCasing()
    {
        Assert.Equal("Mini", DiagnosticsDumpTypeCatalog.DefaultOption);
        Assert.Equal("Mini, Heap, Full or Triage", DiagnosticsDumpTypeCatalog.ExpectedNames);
        foreach (var pair in new[]
        {
            ("mini", "Mini"), ("heap", "Heap"), ("full", "Full"), ("triage", "Triage")
        })
        {
            Assert.True(DiagnosticsDumpTypeCatalog.TryParse(pair.Item1, out var dumpType));
            Assert.Equal(pair.Item2, DiagnosticsDumpTypeCatalog.GetOption(dumpType));
        }
        Assert.False(DiagnosticsDumpTypeCatalog.TryParse("1", out _));
    }

    [Fact]
    public void RemoteBenchmarkStatesKeepTheirPersistedNamesAndTerminalClassification()
    {
        Assert.Equal(
            ["starting", "running", "cancel-requested", "succeeded", "failed", "cancelled", "timed-out", "unknown"],
            RemoteBenchmarkStateCatalog.All.Select(static descriptor => descriptor.Name));

        foreach (var descriptor in RemoteBenchmarkStateCatalog.All)
        {
            Assert.True(RemoteBenchmarkStateCatalog.TryParse(descriptor.Name, out var parsed));
            Assert.Equal(descriptor.State, parsed);
        }

        Assert.True(RemoteBenchmarkStateCatalog.IsStartResponse("starting"));
        Assert.False(RemoteBenchmarkStateCatalog.IsStartResponse("cancel-requested"));
        Assert.True(RemoteBenchmarkStateCatalog.IsTerminal("timed-out"));
        Assert.False(RemoteBenchmarkStateCatalog.IsTerminal("unknown"));
        Assert.False(RemoteBenchmarkStateCatalog.TryParse("Succeeded", out _));
    }

    [Fact]
    public void TestReportOutcomeLabelsAndCountOrderRemainStable()
    {
        Assert.Equal(Enum.GetValues<TestExecutionOutcome>().Order(),
            TestExecutionOutcomeReportCatalog.All.Select(static descriptor => descriptor.Outcome).Order());
        Assert.Equal(
            ["Passed", "Failed", "Crashed", "Hung", "Cancelled", "Incomplete", "InfrastructureError"],
            TestExecutionOutcomeReportCatalog.CountedLabels);
        Assert.Equal("Incomplete", TestExecutionOutcomeReportCatalog.GetLabel(TestExecutionOutcome.DiscoveryFailure));
        Assert.Equal("InfrastructureError", TestExecutionOutcomeReportCatalog.GetLabel(TestExecutionOutcome.InfrastructureFailure));
    }

    [Fact]
    public void TestRunnerCapabilitiesKeepTheirMtpNamesAndCaseInsensitiveConfigParsing()
    {
        Assert.Equal(["Trx", "HangDump", "CrashDump"],
            TestRunnerCapabilityCatalog.All.Select(static descriptor => descriptor.Name));
        foreach (var descriptor in TestRunnerCapabilityCatalog.All)
        {
            Assert.True(TestRunnerCapabilityCatalog.TryParse(descriptor.Name.ToLowerInvariant(), out var parsed));
            Assert.Equal(descriptor.Capability, parsed);
            Assert.Equal(descriptor.Name, TestRunnerCapabilityCatalog.GetName(parsed));
        }
        Assert.False(TestRunnerCapabilityCatalog.TryParse("HangDunp", out _));
    }
}
