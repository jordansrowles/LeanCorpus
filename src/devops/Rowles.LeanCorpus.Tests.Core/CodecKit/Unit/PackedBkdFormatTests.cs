using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class PackedBkdFormatTests
{
    [Fact(DisplayName = "Packed BKD v1 body has locked golden bytes")]
    public void Writer_UsesLockedV1GoldenBytes()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "golden.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 2));
            Span<byte> first = stackalloc byte[8];
            Span<byte> second = stackalloc byte[8];
            XYEncodingUtils.Encode(0, first[..4]);
            XYEncodingUtils.Encode(0, first[4..]);
            XYEncodingUtils.Encode(1, second[..4]);
            XYEncodingUtils.Encode(1, second[4..]);
            buffer.Append(first, 0);
            buffer.Append(second, 1);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
            Assert.Equal(
                "50424631020204000200000001000000020000000000000002000000000000008000000080000000BF800000BF80000000000000000000002A0000000000000002000100000000008000000080000000BF800000BF80000000018000000080000000BF800000BF80000001000000086C6F636174696F6E00000000000000006A0000000000000050424B44010000006A00000000000000",
                Convert.ToHexString(PackedBkdTestSupport.ReadBody(path)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory(DisplayName = "Packed BKD document IDs use the smallest delta width")]
    [InlineData(0, 0)]
    [InlineData(255, 1)]
    [InlineData(256, 2)]
    [InlineData(65_536, 3)]
    [InlineData(16_777_216, 4)]
    public void Writer_UsesSmallestDocumentWidth(int secondDocument, int expectedWidth)
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "doc-width.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 2));
            PackedBkdTestSupport.AppendPoint(buffer, 0, 0, 0);
            PackedBkdTestSupport.AppendPoint(buffer, 1, 1, secondDocument);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            var header = PackedBkdTestSupport.ReadFirstLeafHeader(path);
            Assert.Equal(expectedWidth, header.DocumentWidth);
            Assert.Equal(0, header.MinimumDocument);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD selects raw and prefix leaf encodings deterministically")]
    public void Writer_SelectsRawAndPrefixLeafEncodings()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string rawPath = Path.Combine(directory, "raw.pbkd");
            using (var raw = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 2)))
            {
                PackedBkdTestSupport.AppendPoint(raw, -1, -1, 0);
                PackedBkdTestSupport.AppendPoint(raw, 1, 1, 1);
                PackedBkdWriter.Write(rawPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = raw });
            }

            string prefixPath = Path.Combine(directory, "prefix.pbkd");
            using (var prefix = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 2)))
            {
                PackedBkdTestSupport.AppendPoint(prefix, 0, 0, 0);
                PackedBkdTestSupport.AppendPoint(prefix, float.BitIncrement(0), float.BitIncrement(0), 1);
                PackedBkdWriter.Write(prefixPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = prefix });
            }

            Assert.Equal(0, PackedBkdTestSupport.ReadFirstLeafHeader(rawPath).Encoding);
            Assert.Equal(1, PackedBkdTestSupport.ReadFirstLeafHeader(prefixPath).Encoding);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD uses common prefixes for identical values")]
    public void Writer_UsesPrefixEncodingForIdenticalValues()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "identical.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 2));
            buffer.Append([0, 1, 2, 3, 4, 5, 6, 7], 0);
            buffer.Append([0, 1, 2, 3, 4, 5, 6, 7], 1);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
            Assert.Equal(1, PackedBkdTestSupport.ReadFirstLeafHeader(path).Encoding);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD writes the v1 footer and relative leaf offsets")]
    public void Writer_WritesV1FooterAndRelativeLeafOffsets()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "layout.pbkd");
            using var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            using var input = new IndexInput(path);
            using var session = CodecFileReader.Open(input, PackedBkdCodecFiles.Descriptor, ownsInput: true);
            session.ValidateChecksum();
            using var body = session.OpenBodyInput();
            body.Seek(body.Length - 16);
            Assert.Equal(unchecked((int)PackedBkdFormat.FooterMagic), body.ReadInt32());
            Assert.Equal(1, body.ReadInt32());
            long directoryOffset = body.ReadInt64();
            body.Seek(directoryOffset);
            Assert.Equal(1, body.ReadInt32());
            Assert.Equal("location", body.ReadLengthPrefixedString());
            long sectionOffset = body.ReadInt64();
            long sectionLength = body.ReadInt64();
            Assert.Equal(0, sectionOffset);
            body.Seek(sectionOffset);
            Assert.Equal(unchecked((int)PackedBkdFormat.FieldMagic), body.ReadInt32());
            int dimensions = body.ReadByte();
            int indexedDimensions = body.ReadByte();
            int bytesPerDimension = body.ReadByte();
            Assert.Equal(2, dimensions);
            Assert.Equal(2, indexedDimensions);
            Assert.Equal(4, bytesPerDimension);
            Assert.Equal(0, body.ReadByte());
            int maxPointsPerLeaf = body.ReadByte() | (body.ReadByte() << 8);
            Assert.Equal(0, body.ReadByte());
            Assert.Equal(0, body.ReadByte());
            int leafCount = body.ReadInt32();
            _ = body.ReadInt64();
            _ = body.ReadInt32();
            int splitCount = body.ReadInt32();
            body.Seek(body.Position + indexedDimensions * bytesPerDimension * 2L + splitCount * (1L + bytesPerDimension));
            long leafDataOffset = body.Position + (leafCount + 1L) * sizeof(long);
            long firstLeafOffset = body.ReadInt64();
            body.Seek(body.Position + leafCount * sizeof(long) - sizeof(long));
            long finalLeafOffset = body.ReadInt64();

            Assert.Equal(2, maxPointsPerLeaf);
            Assert.Equal(leafCount - 1, splitCount);
            Assert.Equal(0, firstLeafOffset);
            Assert.Equal(sectionLength - leafDataOffset, finalLeafOffset);
            Assert.True(finalLeafOffset > 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects invalid UTF-16 field names before emission")]
    public void Writer_RejectsInvalidUtf16FieldNames()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "invalid-name.pbkd");
            using var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false);
            Assert.Throws<ArgumentException>(() => PackedBkdWriter.Write(
                path,
                new Dictionary<string, PackedBkdFieldBuffer> { ["bad\uD800"] = buffer }));
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
