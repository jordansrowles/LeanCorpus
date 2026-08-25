using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class NumericIndexAndParentBitSetFrameTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public NumericIndexAndParentBitSetFrameTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void NumericIndexWriters_EmitCanonicalFrames_AndReadersRetainHeaderlessLegacySupport()
    {
        string currentDouble = Path.Combine(_fixture.Path, $"{Guid.NewGuid():N}.num");
        string currentInt64 = Path.Combine(_fixture.Path, $"{Guid.NewGuid():N}.numl");
        var doubles = new Dictionary<string, Dictionary<int, double>>(StringComparer.Ordinal)
        {
            ["price"] = new() { [1] = 1.25, [9] = -4.5 }
        };
        var int64s = new Dictionary<string, Dictionary<int, long>>(StringComparer.Ordinal)
        {
            ["count"] = new() { [2] = long.MinValue, [8] = long.MaxValue }
        };

        NumericIndexCodec.WriteDouble(currentDouble, doubles);
        NumericIndexCodec.WriteInt64(currentInt64, int64s);

        AssertCanonical(currentDouble, NumericIndexCodec.DoubleDescriptor);
        AssertCanonical(currentInt64, NumericIndexCodec.Int64Descriptor);
        Assert.Equal(doubles["price"], NumericIndexCodec.ReadDouble(new IndexInput(currentDouble))["price"]);
        Assert.Equal(int64s["count"], NumericIndexCodec.ReadInt64(new IndexInput(currentInt64))["count"]);

        string legacyDouble = Path.Combine(_fixture.Path, $"{Guid.NewGuid():N}.legacy.num");
        string legacyInt64 = Path.Combine(_fixture.Path, $"{Guid.NewGuid():N}.legacy.numl");
        WriteLegacyDouble(legacyDouble, doubles);
        WriteLegacyInt64(legacyInt64, int64s);

        Assert.Equal(doubles["price"], NumericIndexCodec.ReadDouble(new IndexInput(legacyDouble))["price"]);
        Assert.Equal(int64s["count"], NumericIndexCodec.ReadInt64(new IndexInput(legacyInt64))["count"]);
    }

    [Fact]
    public void NumericIndexAndParentBitSet_ReadCanonicalMembersFromCompoundFile()
    {
        const string segmentId = "seg_sidecars";
        string numericPath = Path.Combine(_fixture.Path, segmentId + ".num");
        string parentsPath = Path.Combine(_fixture.Path, segmentId + ".pbs");
        NumericIndexCodec.WriteDouble(numericPath, new Dictionary<string, Dictionary<int, double>>
        {
            ["price"] = new() { [3] = 7.5 }
        });
        var parents = new ParentBitSet(12);
        parents.Set(3);
        parents.Set(11);
        parents.WriteTo(parentsPath);

        Assert.True(CompoundFileWriter.Pack(_fixture.Path, segmentId));

        using var directory = new MMapDirectory(_fixture.Path);
        using var compound = CompoundFileReader.Open(directory, segmentId + ".cfs");
        var numeric = NumericIndexCodec.ReadDouble(compound.OpenInput(directory, segmentId + ".num"));
        var restoredParents = ParentBitSet.ReadFrom(compound.OpenInput(directory, segmentId + ".pbs"));

        Assert.Equal(7.5, numeric["price"][3]);
        Assert.True(restoredParents.IsParent(3));
        Assert.True(restoredParents.IsParent(11));
        Assert.Equal(-1, restoredParents.NextParent(12));
    }

    [Fact]
    public void ParentBitSetReader_RetainsHeaderlessLegacySupport()
    {
        string path = Path.Combine(_fixture.Path, $"{Guid.NewGuid():N}.legacy.pbs");
        using (var output = new IndexOutput(path))
        {
            output.WriteInt32(65);
            output.WriteInt32(2);
            output.WriteInt64(1L << 4);
            output.WriteInt64(1L);
        }

        var parents = ParentBitSet.ReadFrom(path);

        Assert.True(parents.IsParent(4));
        Assert.True(parents.IsParent(64));
        Assert.Equal(64, parents.NextParent(5));
        Assert.Equal(4, parents.PrevParent(64));
    }

    [Fact]
    public void CanonicalSidecars_RejectChecksumCorruption()
    {
        string numericPath = Path.Combine(_fixture.Path, $"{Guid.NewGuid():N}.num");
        NumericIndexCodec.WriteDouble(numericPath, new Dictionary<string, Dictionary<int, double>>
        {
            ["price"] = new() { [1] = 1.0 }
        });
        CorruptFinalBodyByte(numericPath);
        Assert.Throws<CodecFileException>(() => NumericIndexCodec.ReadDouble(new IndexInput(numericPath)));

        string parentsPath = Path.Combine(_fixture.Path, $"{Guid.NewGuid():N}.pbs");
        var parents = new ParentBitSet(2);
        parents.Set(1);
        parents.WriteTo(parentsPath);
        CorruptFinalBodyByte(parentsPath);
        Assert.Throws<CodecFileException>(() => ParentBitSet.ReadFrom(parentsPath));
    }

    private static void AssertCanonical(string path, CodecFileDescriptor descriptor)
    {
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.Open(input, descriptor);
        Assert.Equal(descriptor.CurrentFormatVersion, frame.Metadata.FormatVersion);
        frame.ValidateChecksum();
    }

    private static void WriteLegacyDouble(string path, IReadOnlyDictionary<string, Dictionary<int, double>> fields)
    {
        using var output = new IndexOutput(path);
        output.WriteInt32(fields.Count);
        foreach (var (field, values) in fields)
        {
            output.WriteString(field);
            output.WriteInt32(values.Count);
            foreach (var (docId, value) in values)
            {
                output.WriteInt32(docId);
                output.WriteInt64(BitConverter.DoubleToInt64Bits(value));
            }
        }
    }

    private static void WriteLegacyInt64(string path, IReadOnlyDictionary<string, Dictionary<int, long>> fields)
    {
        using var output = new IndexOutput(path);
        output.WriteInt32(fields.Count);
        foreach (var (field, values) in fields)
        {
            output.WriteString(field);
            output.WriteInt32(values.Count);
            foreach (var (docId, value) in values)
            {
                output.WriteInt32(docId);
                output.WriteInt64(value);
            }
        }
    }

    private static void CorruptFinalBodyByte(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^17] ^= 0x01;
        File.WriteAllBytes(path, bytes);
    }
}
