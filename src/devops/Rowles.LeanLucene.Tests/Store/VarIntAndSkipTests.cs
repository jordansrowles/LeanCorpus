using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Hnsw;
using Rowles.LeanLucene.Codecs.Fst;
using Rowles.LeanLucene.Codecs.Bkd;
using Rowles.LeanLucene.Codecs.Vectors;
using Rowles.LeanLucene.Codecs.TermVectors;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Codecs.Postings;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Store;

/// <summary>Tests for VarInt encoding, skip pointers, and prefetch on IndexInput/IndexOutput.</summary>
[Trait("Category", "Store")]
[Trait("Category", "Codecs")]
public sealed class VarIntAndSkipTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public VarIntAndSkipTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string FilePath(string name) => System.IO.Path.Combine(_fixture.Path, name);

    /// <summary>
    /// Verifies the Write VarInt: Read VarInt Round Trips Small Values scenario.
    /// </summary>
    [Fact(DisplayName = "Write VarInt: Read VarInt Round Trips Small Values")]
    public void WriteVarInt_ReadVarInt_RoundTripsSmallValues()
    {
        var path = FilePath("varint_small.bin");
        using (var output = new IndexOutput(path))
        {
            output.WriteVarInt(0);
            output.WriteVarInt(1);
            output.WriteVarInt(127);
        }

        using var input = new IndexInput(path);
        Assert.Equal(0, input.ReadVarInt());
        Assert.Equal(1, input.ReadVarInt());
        Assert.Equal(127, input.ReadVarInt());
    }

    /// <summary>
    /// Verifies the Write VarInt: Read VarInt Round Trips Large Values scenario.
    /// </summary>
    [Fact(DisplayName = "Write VarInt: Read VarInt Round Trips Large Values")]
    public void WriteVarInt_ReadVarInt_RoundTripsLargeValues()
    {
        var path = FilePath("varint_large.bin");
        int[] values = [128, 255, 16383, 16384, 2097151, int.MaxValue];

        using (var output = new IndexOutput(path))
        {
            foreach (var v in values)
                output.WriteVarInt(v);
        }

        using var input = new IndexInput(path);
        foreach (var expected in values)
            Assert.Equal(expected, input.ReadVarInt());
    }

    /// <summary>
    /// Verifies the Write VarInt: Single Byte For Values Under 128 scenario.
    /// </summary>
    [Fact(DisplayName = "Write VarInt: Single Byte For Values Under 128")]
    public void WriteVarInt_SingleByte_ForValuesUnder128()
    {
        var path = FilePath("varint_onebyte.bin");
        using (var output = new IndexOutput(path))
        {
            output.WriteVarInt(0);
            output.WriteVarInt(127);
        }

        // Each value 0–127 should encode as exactly 1 byte
        Assert.Equal(2, new FileInfo(path).Length);
    }

    /// <summary>
    /// Verifies the Write VarInt: Two Bytes For Values Up To 16383 scenario.
    /// </summary>
    [Fact(DisplayName = "Write VarInt: Two Bytes For Values Up To 16383")]
    public void WriteVarInt_TwoBytes_ForValuesUpTo16383()
    {
        var path = FilePath("varint_twobyte.bin");
        using (var output = new IndexOutput(path))
        {
            output.WriteVarInt(128);
            output.WriteVarInt(16383);
        }

        // 128 and 16383 each encode as 2 bytes
        Assert.Equal(4, new FileInfo(path).Length);
    }

    /// <summary>
    /// Verifies the Postings Writer: Read VarInt Compatible With Index Input scenario.
    /// </summary>
    [Fact(DisplayName = "Postings Writer: Read VarInt Compatible With Index Input")]
    public void PostingsWriter_ReadVarInt_CompatibleWithIndexInput()
    {
        // Verify PostingsWriter.WriteVarInt (BinaryWriter) and IndexInput.ReadVarInt are compatible
        var path = FilePath("varint_compat.bin");
        int[] values = [0, 42, 128, 1000, 100_000, int.MaxValue];

        using (var fs = File.Create(path))
        using (var bw = new BinaryWriter(fs))
        {
            foreach (var v in values)
                PostingsWriter.WriteVarInt(bw, v);
        }

        using var input = new IndexInput(path);
        foreach (var expected in values)
            Assert.Equal(expected, input.ReadVarInt());
    }

    /// <summary>
    /// Verifies the Index Output: Write VarInt Compatible With Postings Reader scenario.
    /// </summary>
    [Fact(DisplayName = "Index Output: Write VarInt Compatible With Postings Reader")]
    public void IndexOutput_WriteVarInt_CompatibleWithPostingsReader()
    {
        // Verify IndexOutput.WriteVarInt and PostingsReader.ReadVarInt are compatible
        var path = FilePath("varint_compat2.bin");
        int[] values = [0, 42, 128, 1000, 100_000, int.MaxValue];

        using (var output = new IndexOutput(path))
        {
            foreach (var v in values)
                output.WriteVarInt(v);
        }

        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        foreach (var expected in values)
            Assert.Equal(expected, PostingsReader.ReadVarInt(br));
    }

    /// <summary>
    /// Verifies the Prefetch: Does Not Throw On Valid Input scenario.
    /// </summary>
    [Fact(DisplayName = "Prefetch: Does Not Throw On Valid Input")]
    public void Prefetch_DoesNotThrow_OnValidInput()
    {
        var path = FilePath("prefetch_test.bin");
        var data = new byte[4096];
        new Random(42).NextBytes(data);
        File.WriteAllBytes(path, data);

        using var input = new IndexInput(path);
        // Prefetch is advisory — just verify it doesn't throw
        input.Prefetch();
        Assert.Equal(4096, input.Length);
    }

    /// <summary>
    /// Verifies the Prefetch: Empty File Does Not Throw scenario.
    /// </summary>
    [Fact(DisplayName = "Prefetch: Empty File Does Not Throw")]
    public void Prefetch_EmptyFile_DoesNotThrow()
    {
        var path = FilePath("prefetch_empty.bin");
        File.WriteAllBytes(path, []);

        using var input = new IndexInput(path);
        input.Prefetch();
        Assert.Equal(0, input.Length);
    }
}
