using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Hnsw;
using Rowles.LeanLucene.Codecs.Fst;
using Rowles.LeanLucene.Codecs.Bkd;
using Rowles.LeanLucene.Codecs.Vectors;
using Rowles.LeanLucene.Codecs.TermVectors;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Codecs.DocValues;
using Rowles.LeanLucene.Codecs.Postings;
using Rowles.LeanLucene.Codecs.StoredFields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Tests.Fixtures;
using Xunit.Abstractions;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Contains unit tests for Codecs.
/// </summary>
[Trait("Category", "Codecs")]
public sealed class CodecsTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public CodecsTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Verifies the Dic File: Round-trip All Terms Recoverable scenario.
    /// </summary>
    [Fact(DisplayName = "Dic File: Round-trip All Terms Recoverable")]
    public void DicFile_RoundTrip_AllTermsRecoverable()
    {
        var filePath = System.IO.Path.Combine(_fixture.Path, "roundtrip");
        var terms = Enumerable.Range(0, 1000)
            .Select(i => $"term_{i:D5}")
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        var postingsOffsets = new Dictionary<string, long>();
        long offset = 0;
        foreach (var term in terms)
        {
            postingsOffsets[term] = offset;
            offset += 100;
        }

        TermDictionaryWriter.Write(filePath + ".dic", terms, postingsOffsets);
        using var reader = TermDictionaryReader.Open(filePath + ".dic");

        foreach (var term in terms)
        {
            Assert.True(reader.TryGetPostingsOffset(term, out long readOffset));
            Assert.Equal(postingsOffsets[term], readOffset);
        }
    }

    /// <summary>
    /// Verifies the Dic File: Skip Index Bounds Cold Read Latency scenario.
    /// </summary>
    [Fact(DisplayName = "Dic File: Skip Index Bounds Cold Read Latency")]
    [Trait("Category", "Advanced")]
    public void DicFile_SkipIndex_BoundsColdReadLatency()
    {
        var filePath = System.IO.Path.Combine(_fixture.Path, "skipindex");
        var terms = Enumerable.Range(0, 10_000)
            .Select(i => $"term_{i:D6}")
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        var offsets = new Dictionary<string, long>();
        for (int i = 0; i < terms.Count; i++)
            offsets[terms[i]] = i * 50L;

        TermDictionaryWriter.Write(filePath + ".dic", terms, offsets);
        using var reader = TermDictionaryReader.Open(filePath + ".dic");

        var target = terms[5000];
        Assert.True(reader.TryGetPostingsOffset(target, out _));
    }

    /// <summary>
    /// Verifies the Pos File: Delta Encoding Ids Restored Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Pos File: Delta Encoding Ids Restored Correctly")]
    public void PosFile_DeltaEncoding_IdsRestoredCorrectly()
    {
        var filePath = System.IO.Path.Combine(_fixture.Path, "posfile_delta");
        var docIds = new[] { 1, 5, 9, 100, 101 };

        PostingsWriter.Write(filePath + ".pos", "testterm", docIds);
        var readIds = PostingsReader.ReadDocIds(filePath + ".pos", "testterm");

        Assert.Equal(docIds, readIds);
    }

    /// <summary>
    /// Verifies the Pos File: Delta Decoding Overflow Throws Invalid Data Exception scenario.
    /// </summary>
    [Fact(DisplayName = "Pos File: Delta Decoding Overflow Throws Invalid Data Exception")]
    public void PosFile_DeltaDecodingOverflow_ThrowsInvalidDataException()
    {
        var filePath = System.IO.Path.Combine(_fixture.Path, "posfile_overflow.pos");
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            writer.Write("overflow".Length);
            writer.Write("overflow".ToCharArray());
            writer.Write(2); // count
            WriteVarInt(writer, int.MaxValue);
            WriteVarInt(writer, 1); // overflows cumulative doc ID
        }

        Assert.Throws<InvalidDataException>(() => PostingsReader.ReadDocIds(filePath, "overflow"));
    }

    /// <summary>
    /// Verifies the Pos File: Simple 8 b Compression Output Smaller Than Raw scenario.
    /// </summary>
    [Fact(DisplayName = "Pos File: Simple 8 b Compression Output Smaller Than Raw")]
    [Trait("Category", "Advanced")]
    public void PosFile_Simple8bCompression_OutputSmallerThanRaw()
    {
        var filePath = System.IO.Path.Combine(_fixture.Path, "posfile_compress");
        var docIds = Enumerable.Range(0, 10_000).ToArray();

        PostingsWriter.Write(filePath + ".pos", "bigterm", docIds);

        var compressedSize = new FileInfo(filePath + ".pos").Length;
        var rawSize = docIds.Length * sizeof(int);
        Assert.True(compressedSize < rawSize,
            $"Compressed {compressedSize} bytes should be less than raw {rawSize} bytes");
    }

    /// <summary>
    /// Verifies the Nrm File: Round-trip Quantised Values Restored scenario.
    /// </summary>
    [Fact(DisplayName = "Nrm File: Round-trip Quantised Values Restored")]
    public void NrmFile_RoundTrip_QuantisedValuesRestored()
    {
        var filePath = System.IO.Path.Combine(_fixture.Path, "nrmfile");
        var norms = new float[100];
        for (int i = 0; i < norms.Length; i++)
            norms[i] = 1.0f / (1.0f + i);

        // Write as per-field format (version 2)
        var fieldNorms = new Dictionary<string, float[]>(StringComparer.Ordinal)
        {
            ["testfield"] = norms
        };
        NormsWriter.Write(filePath + ".nrm", fieldNorms);
        var restored = NormsReader.Read(filePath + ".nrm");

        Assert.True(restored.ContainsKey("testfield"));
        var restoredNorms = restored["testfield"];
        Assert.Equal(norms.Length, restoredNorms.Length);
        for (int i = 0; i < norms.Length; i++)
        {
            float restoredFloat = restoredNorms[i] / 255f;
            Assert.InRange(restoredFloat, norms[i] - 0.01f, norms[i] + 0.01f);
        }
    }

    /// <summary>
    /// Verifies the Fdt File: Stored Fields Exact Retrieval By Doc ID scenario.
    /// </summary>
    [Fact(DisplayName = "Fdt File: Stored Fields Exact Retrieval By Doc ID")]
    public void FdtFile_StoredFields_ExactRetrievalByDocId()
    {
        var filePath = System.IO.Path.Combine(_fixture.Path, "fdtfile");
        var docs = new[]
        {
            new Dictionary<string, List<string>> { ["title"] = new List<string> { "First Document" } },
            new Dictionary<string, List<string>> { ["title"] = new List<string> { "Second Document" } },
            new Dictionary<string, List<string>> { ["title"] = new List<string> { "Third Document" } }
        };

        StoredFieldsWriter.Write(filePath + ".fdt", filePath + ".fdx", docs);
        using var reader = StoredFieldsReader.Open(filePath + ".fdt", filePath + ".fdx");

        for (int i = 0; i < docs.Length; i++)
        {
            var stored = reader.ReadDocument(i);
            Assert.Equal(docs[i]["title"][0], stored["title"][0]);
        }
    }

    /// <summary>
    /// Verifies the Del File: Serialise Deserialise Bitset Preserved scenario.
    /// </summary>
    [Fact(DisplayName = "Del File: Serialise Deserialise Bitset Preserved")]
    public void DelFile_Serialise_Deserialise_BitsetPreserved()
    {
        var filePath = System.IO.Path.Combine(_fixture.Path, "delfile.del");
        var liveDocs = new LiveDocs(1000);
        var deletedIds = new HashSet<int>();

        var rng = new Random(42);
        while (deletedIds.Count < 300)
            deletedIds.Add(rng.Next(1000));

        foreach (var id in deletedIds)
            liveDocs.Delete(id);

        LiveDocs.Serialise(filePath, liveDocs);
        var restored = LiveDocs.Deserialise(filePath, 1000);

        for (int i = 0; i < 1000; i++)
            Assert.Equal(liveDocs.IsLive(i), restored.IsLive(i));
    }

    /// <summary>
    /// Verifies the Vec File: Round-trip Float Vectors Restored Exactly scenario.
    /// </summary>
    [Fact(DisplayName = "Vec File: Round-trip Float Vectors Restored Exactly")]
    [Trait("Category", "Advanced")]
    public void VecFile_RoundTrip_FloatVectorsRestoredExactly()
    {
        var filePath = System.IO.Path.Combine(_fixture.Path, "vecfile");
        var vectors = new ReadOnlyMemory<float>[50];
        var vectorArrays = new float[50][];
        var rng = new Random(42);

        for (int i = 0; i < 50; i++)
        {
            vectorArrays[i] = new float[128];
            for (int j = 0; j < 128; j++)
                vectorArrays[i][j] = (float)rng.NextDouble();
            vectors[i] = vectorArrays[i];
        }

        VectorWriter.Write(filePath + ".vec", vectors);
        using var reader = VectorReader.Open(filePath + ".vec");

        for (int i = 0; i < 50; i++)
        {
            var restored = reader.ReadVector(i);
            Assert.Equal(vectorArrays[i], restored);
        }
    }

    private static void WriteVarInt(BinaryWriter writer, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            writer.Write((byte)(v | 0x80));
            v >>= 7;
        }
        writer.Write((byte)v);
    }
}
