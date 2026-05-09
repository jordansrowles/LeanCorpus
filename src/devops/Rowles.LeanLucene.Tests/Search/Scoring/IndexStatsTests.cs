using Rowles.LeanLucene.Search.Scoring;

namespace Rowles.LeanLucene.Tests.Search.Scoring;

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
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static IndexStats Build(
        Dictionary<string, float>? avgLengths = null,
        Dictionary<string, int>? docCounts = null)
        => new(
            totalDocCount: 100,
            liveDocCount: 90,
            avgFieldLengths: avgLengths ?? new(StringComparer.Ordinal) { ["body"] = 12.5f },
            fieldDocCounts: docCounts ?? new(StringComparer.Ordinal) { ["body"] = 85 });

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
    }

    // ── WriteTo / TryLoadFrom ─────────────────────────────────────────────────

    [Fact(DisplayName = "IndexStats: WriteTo Then TryLoadFrom Round-Trips")]
    public void WriteTo_ThenTryLoadFrom_RoundTrips()
    {
        var stats = Build(
            avgLengths: new(StringComparer.Ordinal) { ["title"] = 5.0f, ["body"] = 25.0f },
            docCounts: new(StringComparer.Ordinal) { ["title"] = 40, ["body"] = 80 });

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

    // ── GetStatsPath ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexStats: GetStatsPath Returns Correct Path")]
    public void GetStatsPath_ReturnsCorrectPath()
    {
        var expected = Path.Combine(_dir, "stats_7.json");
        Assert.Equal(expected, IndexStats.GetStatsPath(_dir, 7));
    }
}
