using Rowles.LeanLucene.Codecs.Bkd;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Verifies BKDReader.RangeQuery returns the same set of points as a brute-force
/// scan, across many random data shapes and ranges.
/// </summary>
[Trait("Category", "Codecs")]
public sealed class BKDPropertyTests
{
    private static string TempPath(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "leanlucene-bkd-prop", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name + ".bkd");
    }

    [Theory]
    [InlineData(1, 16)]
    [InlineData(64, 16)]
    [InlineData(512, 16)]
    [InlineData(1024, 16)]
    [InlineData(4096, 64)]
    [InlineData(10_000, 128)]
    public void RangeQueryMatchesBruteForceScanForRandomData(int pointCount, int leafSize)
    {
        var seed = 20260502 + pointCount;
        var rng = new Random(seed);

        var points = new List<(double Value, int DocId)>(pointCount);
        for (int i = 0; i < pointCount; i++)
        {
            double v = rng.NextDouble() * 2_000_000.0 - 1_000_000.0;
            points.Add((v, i));
        }

        var path = TempPath("rand");
        BKDWriter.Write(path, new Dictionary<string, List<(double, int)>> { ["price"] = points }, leafSize);

        using var reader = BKDReader.Open(path);

        for (int q = 0; q < 50; q++)
        {
            double a = rng.NextDouble() * 2_000_000.0 - 1_000_000.0;
            double b = rng.NextDouble() * 2_000_000.0 - 1_000_000.0;
            (double min, double max) = a <= b ? (a, b) : (b, a);

            var expected = points
                .Where(p => p.Value >= min && p.Value <= max)
                .OrderBy(p => p.DocId).ThenBy(p => p.Value)
                .ToList();

            var actual = reader.RangeQuery("price", min, max)
                .OrderBy(p => p.DocId).ThenBy(p => p.Value)
                .ToList();

            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].DocId, actual[i].DocId);
                Assert.Equal(expected[i].Value, actual[i].Value);
            }
        }
    }

    [Fact]
    public void OpenRangeUnderMinReturnsAllPoints()
    {
        var points = Enumerable.Range(0, 200).Select(i => ((double)i, i)).ToList();
        var path = TempPath("openmin");
        BKDWriter.Write(path, new Dictionary<string, List<(double, int)>> { ["x"] = points }, maxLeafSize: 16);

        using var reader = BKDReader.Open(path);
        var hits = reader.RangeQuery("x", double.NegativeInfinity, double.PositiveInfinity);
        Assert.Equal(200, hits.Count);
    }

    [Fact]
    public void RangeQueryOnUnknownFieldReturnsEmpty()
    {
        var points = new List<(double, int)> { (1.0, 0), (2.0, 1) };
        var path = TempPath("unknown");
        BKDWriter.Write(path, new Dictionary<string, List<(double, int)>> { ["a"] = points });

        using var reader = BKDReader.Open(path);
        Assert.Empty(reader.RangeQuery("missing", 0, 10));
    }

    [Fact]
    public void DuplicateValuesArePreserved()
    {
        var points = new List<(double, int)>();
        for (int i = 0; i < 1000; i++)
            points.Add((42.0, i));

        var path = TempPath("dups");
        BKDWriter.Write(path, new Dictionary<string, List<(double, int)>> { ["x"] = points }, maxLeafSize: 32);

        using var reader = BKDReader.Open(path);
        var hits = reader.RangeQuery("x", 42.0, 42.0);
        Assert.Equal(1000, hits.Count);
        Assert.All(hits, h => Assert.Equal(42.0, h.Value));
        var ids = hits.Select(h => h.DocId).OrderBy(x => x).ToArray();
        Assert.Equal(Enumerable.Range(0, 1000).ToArray(), ids);
    }

    [Fact]
    public void NegativeAndPositiveBoundariesAreHandled()
    {
        var points = new List<(double, int)>();
        for (int i = -500; i <= 500; i++)
            points.Add(((double)i, i + 500));

        var path = TempPath("signed");
        BKDWriter.Write(path, new Dictionary<string, List<(double, int)>> { ["x"] = points }, maxLeafSize: 32);

        using var reader = BKDReader.Open(path);

        var hits = reader.RangeQuery("x", -1, 1).OrderBy(h => h.Value).ToList();
        Assert.Equal(3, hits.Count);
        Assert.Equal(-1.0, hits[0].Value);
        Assert.Equal(0.0, hits[1].Value);
        Assert.Equal(1.0, hits[2].Value);
    }
}
