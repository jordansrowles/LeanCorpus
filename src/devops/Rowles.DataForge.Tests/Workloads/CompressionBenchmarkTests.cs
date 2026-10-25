using Rowles.LeanCorpus.Benchmarks.Compression;
using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class CompressionBenchmarkTests
{
    [Fact]
    public void Extension_codecs_are_registered_before_test_execution()
    {
        Assert.True(CompressionCodecRegistry.TryGet((byte)FieldCompressionPolicy.Lz4, out _));
        Assert.True(CompressionCodecRegistry.TryGet((byte)FieldCompressionPolicy.Snappy, out _));
        Assert.True(CompressionCodecRegistry.TryGet((byte)FieldCompressionPolicy.Zstandard, out _));
    }

    [Fact]
    public void Unknown_payload_size_is_rejected_before_timing()
    {
        var benchmark = new CompressionBenchmarks { Policy = FieldCompressionPolicy.None, Size = "typo" };
        Assert.Throws<ArgumentOutOfRangeException>(benchmark.Setup);
    }
    public static TheoryData<int, string, int> CodecCases
    {
        get
        {
            var cases = new TheoryData<int, string, int>();
            foreach (var policy in new[] { FieldCompressionPolicy.None, FieldCompressionPolicy.Deflate,
                         FieldCompressionPolicy.Brotli, FieldCompressionPolicy.Lz4,
                         FieldCompressionPolicy.Snappy, FieldCompressionPolicy.Zstandard })
            {
                cases.Add((int)policy, "small", 128);
                cases.Add((int)policy, "medium", 4096);
                cases.Add((int)policy, "large", 65536);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(CodecCases))]
    public void Every_codec_retains_exact_pre_timing_byte_counts_and_round_trip(int policy, string size, int originalBytes)
    {
        var benchmark = new CompressionBenchmarks { Policy = (FieldCompressionPolicy)policy, Size = size };
        benchmark.Setup();

        var encoded = benchmark.Compress();
        var decoded = benchmark.Decompress();
        var evidence = benchmark.Measurement;
        Assert.Equal(originalBytes, decoded.Length);
        Assert.Equal(CompressionFixture.BuildPayload(originalBytes), decoded);
        Assert.Equal(originalBytes, evidence.OriginalBytes);
        Assert.Equal(encoded.Length, evidence.CompressedBytes);
        Assert.Equal((double)originalBytes / encoded.Length, evidence.CompressionRatio);
        Assert.Equal(evidence.PayloadSha256, evidence.DecodedSha256);
        Assert.Equal("Directional", evidence.Classification);
        Assert.Equal("rowles-text-multilingual", evidence.Dataset.ProfileId);
        Assert.Equal((ulong)42, evidence.Dataset.Seed);
        Assert.Equal("en", evidence.Dataset.Parameters["language"]);
    }

    [Fact]
    public void Corrupt_or_empty_codec_output_cannot_create_passing_evidence()
    {
        Assert.Throws<InvalidDataException>(() => CompressionFixture.Measure(FieldCompressionPolicy.None,
            [1, 2, 3], [1, 2, 3], [1, 2, 4]));
        Assert.Throws<InvalidDataException>(() => CompressionFixture.Measure(FieldCompressionPolicy.None,
            [1, 2, 3], [], [1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => CompressionFixture.Measure(FieldCompressionPolicy.None, [], [], []));
    }

    [Fact]
    public void Evidence_retains_recalculable_bytes_hashes_and_dataset_identity()
    {
        var benchmark = new CompressionBenchmarks { Policy = FieldCompressionPolicy.Deflate, Size = "medium" };
        benchmark.Setup();
        var directory = Path.Combine(Path.GetTempPath(), "compression-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            CompressionFixture.WriteEvidence(directory, benchmark.Measurement);
            var file = Assert.Single(Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories));
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(4096, root.GetProperty("originalBytes").GetInt32());
            Assert.Equal(benchmark.Compress().Length, root.GetProperty("compressedBytes").GetInt32());
            Assert.Equal(1, root.GetProperty("operationsPerInvoke").GetInt32());
            Assert.Equal("Generated", root.GetProperty("dataset").GetProperty("sourceKind").GetString());
            Assert.Equal("utf8-prefix", root.GetProperty("payloadSelection").GetString());
            Assert.Equal(root.GetProperty("payloadSha256").GetString(), root.GetProperty("decodedSha256").GetString());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

}
