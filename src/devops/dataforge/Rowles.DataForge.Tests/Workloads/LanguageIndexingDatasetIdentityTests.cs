using Rowles.DataForge;
using Rowles.LeanCorpus.Benchmarks;

namespace Rowles.DataForge.Tests.Workloads;

/// <summary>
/// The language indexing producer benchmark registers a complete dataset identity for the fixed
/// documents each workload indexes, so its run reports are not rejected for a missing dataset.
/// </summary>
public sealed class LanguageIndexingDatasetIdentityTests
{
    public static TheoryData<LanguageAnalysisWorkload> Workloads() => new(Enum.GetValues<LanguageAnalysisWorkload>());

    [Theory]
    [MemberData(nameof(Workloads))]
    public void Identity_IsCompleteAndDeterministic(LanguageAnalysisWorkload workload)
    {
        string[] fields = LanguageAnalysisIndexingBenchmarks.CreateFieldValues(workload);
        int documents = LanguageAnalysisIndexingBenchmarks.DocumentCountFor(workload);

        var first = LanguageAnalysisIndexingBenchmarks.CreateIdentity(workload, documents, fields);
        var second = LanguageAnalysisIndexingBenchmarks.CreateIdentity(workload, documents, fields);

        Assert.Equal(DataForgeSourceKind.Generated, first.SourceKind);
        Assert.Equal(LanguageAnalysisIndexingBenchmarks.DatasetProfileId, first.ProfileId);
        Assert.Equal(LanguageAnalysisIndexingBenchmarks.DatasetProfileVersion, first.ProfileVersion);
        Assert.Equal(documents, first.RecordCount);
        Assert.Equal(workload.ToString(), first.Parameters["workload"]);
        Assert.Matches("^[0-9a-f]{64}$", first.ContentSha256);
        Assert.Equal(first.GetCanonicalBytes(), second.GetCanonicalBytes());
    }

    [Fact]
    public void Identity_DiffersByWorkloadAndContent()
    {
        var keys = Enum.GetValues<LanguageAnalysisWorkload>()
            .Select(workload => LanguageAnalysisIndexingBenchmarks.CreateIdentity(workload,
                LanguageAnalysisIndexingBenchmarks.DocumentCountFor(workload),
                LanguageAnalysisIndexingBenchmarks.CreateFieldValues(workload)).GetShortKey())
            .ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());

        var workload = LanguageAnalysisWorkload.ShortSingleField;
        string[] fields = LanguageAnalysisIndexingBenchmarks.CreateFieldValues(workload);
        var baseline = LanguageAnalysisIndexingBenchmarks.CreateIdentity(workload, 8, fields);
        var fewer = LanguageAnalysisIndexingBenchmarks.CreateIdentity(workload, 7, fields);
        var edited = LanguageAnalysisIndexingBenchmarks.CreateIdentity(workload, 8, [fields[0] + " x", .. fields[1..]]);
        Assert.NotEqual(baseline.ContentSha256, fewer.ContentSha256);
        Assert.NotEqual(baseline.ContentSha256, edited.ContentSha256);
    }
}
