using System.Collections.Immutable;
using Rowles.LeanCorpus.DevOps.Configuration;

namespace Rowles.LeanCorpus.DevOps.Tests.Configuration;

public sealed class BenchmarkCaseInventoryValidatorTests
{
    [Fact]
    public void MapsDiscoveredMethodAndParameterFingerprintToItsCaseFamily()
    {
        var cases = ImmutableArray.Create(CreateCase(["1000"], ["2"]));

        BenchmarkCaseInventoryValidator.Validate(cases, "core", CreateListing("MaxConcurrentFlushes: 2, UniqueTermCount: 1000"));
    }

    [Fact]
    public void RejectsDeclaredParameterValueMissingFromBdnDiscovery()
    {
        var cases = ImmutableArray.Create(CreateCase(["1000", "10000"], ["2"]));

        var error = Assert.Throws<InvalidDataException>(() =>
            BenchmarkCaseInventoryValidator.Validate(cases, "core", CreateListing("MaxConcurrentFlushes: 2, UniqueTermCount: 1000")));

        Assert.Contains("UniqueTermCount", error.Message, StringComparison.Ordinal);
        Assert.Contains("10000", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateBdnCaseIdentity()
    {
        var cases = ImmutableArray<BenchmarkCaseConfiguration>.Empty;

        var error = Assert.Throws<InvalidDataException>(() =>
            BenchmarkCaseInventoryValidator.Validate(cases, "core", """
                [
                  { "uid": "duplicate", "filterName": "Tests.A.Bench", "fullName": "Tests.A.Bench", "job": { "id": "DefaultJob" } },
                  { "uid": "duplicate", "filterName": "Tests.A.Bench", "fullName": "Tests.A.Bench", "job": { "id": "DefaultJob" } }
                ]
                """));

        Assert.Contains("duplicate BDN case identity 'duplicate'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDiscoveredCaseWithoutSemanticFamilyOrExplicitExclusion()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            BenchmarkCaseInventoryValidator.Validate(
                ImmutableArray<BenchmarkCaseConfiguration>.Empty,
                ImmutableArray<BenchmarkCaseExclusionConfiguration>.Empty,
                "core",
                CreateListing("UniqueTermCount: 1000")));

        Assert.Contains("has no semantic case family or active exclusion", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsExpiredBenchmarkExclusion()
    {
        var exclusions = ImmutableArray.Create(new BenchmarkCaseExclusionConfiguration(
            "core", "FlushTermCardinalityBenchmarks", "FlushUniqueTerms", ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            "Temporary exclusion", new DateOnly(2000, 1, 1)));

        var error = Assert.Throws<InvalidDataException>(() =>
            BenchmarkCaseInventoryValidator.Validate(
                ImmutableArray<BenchmarkCaseConfiguration>.Empty,
                exclusions,
                "core",
                CreateListing("UniqueTermCount: 1000"),
                new DateOnly(2026, 10, 23)));

        Assert.Contains("exclusion", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expired", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitExclusionAccountsForEveryDiscoveredCaseInItsMethod()
    {
        var exclusions = ImmutableArray.Create(new BenchmarkCaseExclusionConfiguration(
            "core", "FlushTermCardinalityBenchmarks", "FlushUniqueTerms", ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            "Awaiting correctness verification", new DateOnly(2027, 1, 31)));

        BenchmarkCaseInventoryValidator.Validate(
            ImmutableArray<BenchmarkCaseConfiguration>.Empty,
            exclusions,
            "core",
            CreateListing("UniqueTermCount: 1000"),
            new DateOnly(2026, 10, 23));
    }

    [Fact]
    public void FamilyAndParameterScopedExclusionCoverSeparateCasesInOneMethod()
    {
        var cases = ImmutableArray.Create(CreateCase(["1000"], ["2"]));
        var exclusion = new BenchmarkCaseExclusionConfiguration(
            "core", "FlushTermCardinalityBenchmarks", "FlushUniqueTerms",
            ImmutableDictionary.CreateRange(StringComparer.Ordinal,
            [
                KeyValuePair.Create("UniqueTermCount", ImmutableArray.Create("10000")),
                KeyValuePair.Create("MaxConcurrentFlushes", ImmutableArray.Create("2"))
            ]),
            "The larger cardinality remains unqualified.", new DateOnly(2027, 1, 31));
        var listing = """
            [
              { "uid": "case-001", "filterName": "Rowles.LeanCorpus.Benchmarks.FlushTermCardinalityBenchmarks.FlushUniqueTerms", "fullName": "Rowles.LeanCorpus.Benchmarks.FlushTermCardinalityBenchmarks.FlushUniqueTerms(UniqueTermCount: 1000, MaxConcurrentFlushes: 2)", "job": { "id": "DefaultJob" } },
              { "uid": "case-002", "filterName": "Rowles.LeanCorpus.Benchmarks.FlushTermCardinalityBenchmarks.FlushUniqueTerms", "fullName": "Rowles.LeanCorpus.Benchmarks.FlushTermCardinalityBenchmarks.FlushUniqueTerms(UniqueTermCount: 10000, MaxConcurrentFlushes: 2)", "job": { "id": "DefaultJob" } }
            ]
            """;

        BenchmarkCaseInventoryValidator.Validate(cases, [exclusion], "core", listing, new DateOnly(2026, 10, 23));
    }

    [Fact]
    public void RejectsCaseCoveredByBothFamilyAndExclusion()
    {
        var cases = ImmutableArray.Create(CreateCase(["1000"], ["2"]));
        var exclusions = ImmutableArray.Create(new BenchmarkCaseExclusionConfiguration(
            "core", "FlushTermCardinalityBenchmarks", "FlushUniqueTerms", ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            "Conflicting exclusion", new DateOnly(2027, 1, 31)));

        var error = Assert.Throws<InvalidDataException>(() =>
            BenchmarkCaseInventoryValidator.Validate(
                cases,
                exclusions,
                "core",
                CreateListing("MaxConcurrentFlushes: 2, UniqueTermCount: 1000"),
                new DateOnly(2026, 10, 23)));

        Assert.Contains("covered by multiple", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsExclusionThatNoLongerMatchesDiscoveredCase()
    {
        var exclusions = ImmutableArray.Create(new BenchmarkCaseExclusionConfiguration(
            "core", "RenamedBenchmarks", "RemovedMethod", ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            "Stale exclusion", new DateOnly(2027, 1, 31)));

        var error = Assert.Throws<InvalidDataException>(() =>
            BenchmarkCaseInventoryValidator.Validate(
                ImmutableArray<BenchmarkCaseConfiguration>.Empty,
                exclusions,
                "core",
                CreateListing("UniqueTermCount: 1000"),
                new DateOnly(2026, 10, 23)));

        Assert.Contains("matched no discovered BDN cases", error.Message, StringComparison.Ordinal);
    }

    private static BenchmarkCaseConfiguration CreateCase(ImmutableArray<string> uniqueTerms, ImmutableArray<string> flushCounts)
        => new(
            "flush-term-cardinality",
            "core",
            "FlushTermCardinalityBenchmarks",
            ["FlushUniqueTerms"],
            [],
            ImmutableDictionary.CreateRange(StringComparer.Ordinal,
            [
                KeyValuePair.Create("UniqueTermCount", uniqueTerms),
                KeyValuePair.Create("MaxConcurrentFlushes", flushCounts)
            ]),
            ImmutableDictionary<string, string>.Empty,
            "leancorpus",
            "Index",
            "FlushOnly",
            "synthetic-unique-terms-v1",
            "flush-term-cardinality-v1",
            "SegmentFlush",
            "NotApplicable",
            "new-index",
            null,
            "Unqualified",
            "committed-index-contains-1000-documents",
            "Reopening and verifying the committed document count is not yet recorded.",
            [new BenchmarkMetricConfiguration("Mean", "ns", "primary")],
            "short");

    private static string CreateListing(string fullNameParameters)
        => $$"""
            [
              {
                "uid": "case-001",
                "filterName": "Rowles.LeanCorpus.Benchmarks.FlushTermCardinalityBenchmarks.FlushUniqueTerms",
                "fullName": "Rowles.LeanCorpus.Benchmarks.FlushTermCardinalityBenchmarks.FlushUniqueTerms({{fullNameParameters}})",
                "job": { "id": "DefaultJob" }
              }
            ]
            """;
}
