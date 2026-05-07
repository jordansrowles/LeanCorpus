using Rowles.LeanLucene.Codecs.StoredFields;
using Rowles.LeanLucene.Compression.LZ4;
using Rowles.LeanLucene.Compression.Zstandard;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Contains unit tests for Field Compression.
/// </summary>
public class FieldCompressionTests : IDisposable
{
    static FieldCompressionTests()
    {
        Lz4Compression.Register();
        ZstandardCompression.Register();
    }
    private readonly string _baseDir;

    public FieldCompressionTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "compress_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true); }
        catch { }
    }

    private string SubDir(string name)
    {
        var d = Path.Combine(_baseDir, name);
        Directory.CreateDirectory(d);
        return d;
    }

    private void IndexDocs(string dir, IndexWriterConfig config, int count = 50)
    {
        using var writer = new IndexWriter(new MMapDirectory(dir), config);
        for (int i = 0; i < count; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body",
                "the quick brown fox jumps over the lazy dog " +
                "performance benchmarks allocation profiling memory " + i));
            doc.Add(new StringField("id", i.ToString()));
            writer.AddDocument(doc);
        }
        writer.Commit();
    }

    /// <summary>
    /// Verifies the Policy: None Produces Largest Files scenario.
    /// </summary>
    [Fact(DisplayName = "Policy: None Produces Largest Files")]
    public void Policy_None_ProducesLargestFiles()
    {
        var dirNone = SubDir("none");
        IndexDocs(dirNone, new IndexWriterConfig { CompressionPolicy = FieldCompressionPolicy.None });

        var dirLz4 = SubDir("lz4");
        IndexDocs(dirLz4, new IndexWriterConfig { CompressionPolicy = FieldCompressionPolicy.Lz4 });

        long sizeNone = GetFdtSize(dirNone);
        long sizeLz4 = GetFdtSize(dirLz4);

        Assert.True(sizeNone >= sizeLz4, $"None ({sizeNone}) should be >= Lz4 ({sizeLz4})");
    }

    /// <summary>
    /// Verifies the Policy: Zstandard Produces Smaller Than Lz 4 scenario.
    /// </summary>
    [Fact(DisplayName = "Policy: Zstandard Produces Smaller Than Lz 4")]
    public void Policy_Zstandard_ProducesSmallerThanLz4()
    {
        var dirLz4 = SubDir("lz4_2");
        IndexDocs(dirLz4, new IndexWriterConfig { CompressionPolicy = FieldCompressionPolicy.Lz4 }, count: 200);

        var dirZstd = SubDir("zstd");
        IndexDocs(dirZstd, new IndexWriterConfig { CompressionPolicy = FieldCompressionPolicy.Zstandard }, count: 200);

        long sizeLz4 = GetFdtSize(dirLz4);
        long sizeZstd = GetFdtSize(dirZstd);

        // Zstandard should be <= LZ4 (may be equal for very small data)
        Assert.True(sizeZstd <= sizeLz4, $"Zstandard ({sizeZstd}) should be <= Lz4 ({sizeLz4})");
    }

    /// <summary>
    /// Verifies the All Policies: Round-trip Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "All Policies: Round-trip Correctly")]
    public void AllPolicies_RoundTrip_Correctly()
    {
        foreach (var policy in new[] { FieldCompressionPolicy.None, FieldCompressionPolicy.Deflate, FieldCompressionPolicy.Brotli, FieldCompressionPolicy.Lz4, FieldCompressionPolicy.Zstandard })
        {
            var dir = SubDir($"roundtrip_{policy}");
            IndexDocs(dir, new IndexWriterConfig { CompressionPolicy = policy }, count: 10);

            using var searcher = new IndexSearcher(new MMapDirectory(dir));
            var results = searcher.Search(new TermQuery("body", "fox"), 100);
            Assert.Equal(10, results.TotalHits);

            var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
            Assert.True(stored.ContainsKey("body"));
        }
    }

    /// <summary>
    /// Verifies the Compression Policy: Default Is Deflate scenario.
    /// </summary>
    [Fact(DisplayName = "Compression Policy: Default Is Deflate")]
    public void CompressionPolicy_DefaultIsDeflate()
    {
        var config = new IndexWriterConfig();
        Assert.Equal(FieldCompressionPolicy.Deflate, config.CompressionPolicy);
    }

    /// <summary>
    /// Verifies the Compression Policy: Can Be Changed scenario.
    /// </summary>
    [Fact(DisplayName = "Compression Policy: Can Be Changed")]
    public void CompressionPolicy_CanBeChanged()
    {
        var config = new IndexWriterConfig { CompressionPolicy = FieldCompressionPolicy.Zstandard };
        Assert.Equal(FieldCompressionPolicy.Zstandard, config.CompressionPolicy);

        config.CompressionPolicy = FieldCompressionPolicy.None;
        Assert.Equal(FieldCompressionPolicy.None, config.CompressionPolicy);
    }

    private static long GetFdtSize(string dir)
    {
        return Directory.GetFiles(dir, "*.fdt")
            .Sum(f => new FileInfo(f).Length);
    }
}
