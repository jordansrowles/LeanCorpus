using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search.Scoring;

/// <summary>
/// Unit tests for <see cref="IndexStats"/> covering accessors, the Empty factory,
/// persistence (WriteTo/TryLoadFrom), and path helpers.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class IndexStatsTests : IDisposable
{
    private readonly string _dir;

    public IndexStatsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_stats_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    private static IndexStats Build(
        Dictionary<string, float>? avgLengths = null,
        Dictionary<string, int>? docCounts = null,
        Dictionary<string, long>? lengthSums = null)
        => new(
            totalDocCount: 100,
            liveDocCount: 90,
            avgFieldLengths: avgLengths ?? new(StringComparer.Ordinal) { ["body"] = 12.5f },
            fieldDocCounts: docCounts ?? new(StringComparer.Ordinal) { ["body"] = 85 },
            fieldLengthSums: lengthSums ?? new(StringComparer.Ordinal) { ["body"] = 12_500 });

    // ── Accessors ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexStats: GetAvgFieldLength Returns Stored Value")]
    public void GetAvgFieldLength_ReturnsStoredValue()
    {
        var stats = Build();
        Assert.Equal(12.5f, stats.GetAvgFieldLength("body"));
    }

    [Fact(DisplayName = "IndexStats: GetAvgFieldLength Defaults To 1.0 For Unknown Field")]
    public void GetAvgFieldLength_DefaultsToOne()
    {
        var stats = Build();
        Assert.Equal(1.0f, stats.GetAvgFieldLength("missing"));
    }

    [Fact(DisplayName = "IndexStats: GetFieldDocCount Returns Stored Value")]
    public void GetFieldDocCount_ReturnsStoredValue()
    {
        var stats = Build();
        Assert.Equal(85, stats.GetFieldDocCount("body"));
    }

    [Fact(DisplayName = "IndexStats: GetFieldDocCount Defaults To Zero For Unknown Field")]
    public void GetFieldDocCount_DefaultsToZero()
    {
        var stats = Build();
        Assert.Equal(0, stats.GetFieldDocCount("missing"));
    }

    [Fact(DisplayName = "IndexStats: TotalDocCount And LiveDocCount Set Correctly")]
    public void TotalAndLiveDocCount_SetCorrectly()
    {
        var stats = Build();
        Assert.Equal(100, stats.TotalDocCount);
        Assert.Equal(90, stats.LiveDocCount);
    }

    [Fact(DisplayName = "IndexStats: GetFieldLengthSum Returns Stored Sum")]
    public void GetFieldLengthSum_ReturnsStoredValue()
    {
        var sums = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["body"] = 12_500,
            ["title"] = 3_200
        };
        var stats = Build(lengthSums: sums);
        Assert.Equal(12_500L, stats.GetFieldLengthSum("body"));
        Assert.Equal(3_200L, stats.GetFieldLengthSum("title"));
    }

    [Fact(DisplayName = "IndexStats: GetFieldLengthSum Defaults To Zero For Unknown Field")]
    public void GetFieldLengthSum_DefaultsToZero()
    {
        var stats = Build();
        Assert.Equal(0L, stats.GetFieldLengthSum("missing"));
    }

    // ── Internal copies ───────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexStats: GetAvgFieldLengths Returns Independent Copy")]
    public void GetAvgFieldLengths_ReturnsIndependentCopy()
    {
        var stats = Build();
        var copy = stats.GetAvgFieldLengths();
        Assert.Equal(12.5f, copy["body"]);
        copy["body"] = 999f;
        Assert.Equal(12.5f, stats.GetAvgFieldLength("body"));
    }

    [Fact(DisplayName = "IndexStats: GetFieldDocCounts Returns Independent Copy")]
    public void GetFieldDocCounts_ReturnsIndependentCopy()
    {
        var stats = Build();
        var copy = stats.GetFieldDocCounts();
        Assert.Equal(85, copy["body"]);
        copy["body"] = 0;
        Assert.Equal(85, stats.GetFieldDocCount("body"));
    }

    // ── Empty ─────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexStats: Empty Has Zero Counts And Empty Dictionaries")]
    public void Empty_HasZeroCountsAndEmptyDicts()
    {
        var e = IndexStats.Empty;
        Assert.Equal(0, e.TotalDocCount);
        Assert.Equal(0, e.LiveDocCount);
        Assert.Equal(1.0f, e.GetAvgFieldLength("any"));
        Assert.Equal(0, e.GetFieldDocCount("any"));
        Assert.Equal(0L, e.GetFieldLengthSum("any"));
    }

    // ── WriteTo / TryLoadFrom ─────────────────────────────────────────────────

    [Fact(DisplayName = "IndexStats: WriteTo Then TryLoadFrom Round-Trips")]
    public void WriteTo_ThenTryLoadFrom_RoundTrips()
    {
        var stats = Build(
            avgLengths: new(StringComparer.Ordinal) { ["title"] = 5.0f, ["body"] = 25.0f },
            docCounts: new(StringComparer.Ordinal) { ["title"] = 40, ["body"] = 80 },
            lengthSums: new(StringComparer.Ordinal) { ["title"] = 5_000, ["body"] = 2_500 });

        var path = Path.Combine(_dir, "stats_1.json");
        stats.WriteTo(path);

        var loaded = IndexStats.TryLoadFrom(path);
        Assert.NotNull(loaded);
        Assert.Equal(100, loaded.TotalDocCount);
        Assert.Equal(90, loaded.LiveDocCount);
        Assert.Equal(5.0f, loaded.GetAvgFieldLength("title"));
        Assert.Equal(25.0f, loaded.GetAvgFieldLength("body"));
        Assert.Equal(40, loaded.GetFieldDocCount("title"));
        Assert.Equal(80, loaded.GetFieldDocCount("body"));
        Assert.Equal(5_000L, loaded.GetFieldLengthSum("title"));
        Assert.Equal(2_500L, loaded.GetFieldLengthSum("body"));
    }

    [Fact(DisplayName = "IndexStats: TryLoadFrom Returns Null For Missing File")]
    public void TryLoadFrom_MissingFile_ReturnsNull()
        => Assert.Null(IndexStats.TryLoadFrom(Path.Combine(_dir, "does_not_exist.json")));

    [Fact(DisplayName = "IndexStats: TryLoadFrom Returns Null For Corrupt File")]
    public void TryLoadFrom_CorruptFile_ReturnsNull()
    {
        var path = Path.Combine(_dir, "corrupt.json");
        File.WriteAllText(path, "{ not valid json !!!");
        Assert.Null(IndexStats.TryLoadFrom(path));
    }

    [Fact(DisplayName = "IndexStats: WriteTo Overwrites Existing File")]
    public void WriteTo_OverwritesExistingFile()
    {
        var path = Path.Combine(_dir, "stats_over.json");
        File.WriteAllText(path, "old content");
        Build().WriteTo(path);
        Assert.NotNull(IndexStats.TryLoadFrom(path));
    }

    [Fact(DisplayName = "IndexStats: TryLoadFrom Returns Null For JSON Null Literal")]
    public void TryLoadFrom_NullJsonLiteral_ReturnsNull()
    {
        var path = Path.Combine(_dir, "null_stats.json");
        File.WriteAllText(path, "null");
        Assert.Null(IndexStats.TryLoadFrom(path));
    }

    // ── GetStatsPath ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexStats: GetStatsPath Returns Correct Path")]
    public void GetStatsPath_ReturnsCorrectPath()
    {
        var expected = Path.Combine(_dir, "stats_7.json");
        Assert.Equal(expected, IndexStats.GetStatsPath(_dir, 7));
    }
}
