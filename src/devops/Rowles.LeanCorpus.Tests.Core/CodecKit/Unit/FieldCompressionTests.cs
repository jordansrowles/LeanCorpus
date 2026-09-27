using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Compression.LZ4;
using Rowles.LeanCorpus.Compression.Zstandard;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

/// <summary>
/// Contains unit tests for Field Compression.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
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
            var results = searcher.Search(new TermQuery("body", "fox"), 100, TestContext.Current.CancellationToken);
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

    [Fact(DisplayName = "Custom immutable catalogue round-trips an unlisted stored-field policy")]
    public void CustomCatalog_RoundTripsCustomPolicyThroughWriterAndSearcher()
    {
        const byte policyByte = 0xE1;
        const string expected = "custom-catalogue-stored-value";
        string path = SubDir("custom-catalogue");
        var innerCodec = CodecCatalog.Default.GetCompressionCodec((byte)FieldCompressionPolicy.Deflate);
        var customCodec = new TrackingCompressionCodec(innerCodec, policyByte);
        var catalog = new CodecCatalogBuilder()
            .AddBuiltIns()
            .AddCompressionCodec(customCodec)
            .Build();

        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            CodecCatalog = catalog,
            CompressionPolicy = (FieldCompressionPolicy)policyByte
        }))
        {
            var document = new LeanDocument();
            document.Add(new StoredField("body", expected));
            writer.AddDocument(document);
            writer.Commit();
        }

        Assert.True(customCodec.CompressCalls > 0);

        using (var searcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            CodecCatalog = catalog
        }))
        {
            var stored = searcher.GetStoredFields(0);
            Assert.Equal(expected, stored["body"][0]);
        }

        Assert.True(customCodec.DecompressCalls > 0);
    }

    private static long GetFdtSize(string dir)
    {
        return Directory.GetFiles(dir, "*.fdt")
            .Sum(f => new FileInfo(f).Length);
    }

    private sealed class TrackingCompressionCodec(IFieldCompressionCodec inner, byte policyByte)
        : IFieldCompressionCodec
    {
        private int _compressCalls;
        private int _decompressCalls;

        public byte PolicyByte => policyByte;
        internal int CompressCalls => Volatile.Read(ref _compressCalls);
        internal int DecompressCalls => Volatile.Read(ref _decompressCalls);

        public byte[] Compress(ReadOnlySpan<byte> raw)
        {
            Interlocked.Increment(ref _compressCalls);
            return inner.Compress(raw);
        }

        public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
        {
            Interlocked.Increment(ref _decompressCalls);
            return inner.Decompress(compressed, originalSize);
        }
    }
}
