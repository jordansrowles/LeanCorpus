using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class DocValuesUntrustedCountTests : IDisposable
{
    private const int MaliciousDocumentCount = 1_000_000;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LeanCorpus_DocValuesCounts", Guid.NewGuid().ToString("N"));

    public DocValuesUntrustedCountTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(DocValuesKind.Numeric)]
    [InlineData(DocValuesKind.Sorted)]
    [InlineData(DocValuesKind.SortedSet)]
    [InlineData(DocValuesKind.SortedNumeric)]
    [InlineData(DocValuesKind.Binary)]
    [InlineData(DocValuesKind.Int64)]
    [InlineData(DocValuesKind.Int64SortedNumeric)]
    public void OpenColumns_RejectsDocumentCountMismatchBeforeAllocating(DocValuesKind kind)
    {
        string path = Path.Combine(_directory, $"malformed-{kind}{Extension(kind)}");
        WriteDocumentCountOnly(path, kind, MaliciousDocumentCount);

        using var input = new IndexInput(path);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Exception? exception = Record.Exception(() => OpenColumns(input, kind));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(allocated < 2 * 1024 * 1024,
            $"Rejecting {kind} DocValues allocated {allocated:N0} bytes for a count that disagrees with the one-document segment.");
        CodecFileException corruption = Assert.IsType<CodecFileException>(exception);
        Assert.Equal(CodecFileErrorCode.SemanticValidationFailure, corruption.ErrorCode);
    }

    [Fact]
    public void OpenColumns_RejectsPresenceChunkCountBeforeAllocating()
    {
        string path = Path.Combine(_directory, "malformed-presence.dvn");
        byte[] malformedBitmap = CreateBitmapWithTruncatedChunkTable(chunkCount: 1_000_000);
        CodecFileWriter.WriteAtomically(path, Descriptor(DocValuesKind.Numeric), durable: false, body =>
        {
            body.WriteInt32(1);
            body.WriteString("field");
            body.WriteInt32(malformedBitmap.Length);
            body.WriteBytes(malformedBitmap);
            body.WriteInt32(1);
            body.WriteInt64(0);
            body.WriteByte(0);
        });

        using var input = new IndexInput(path);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Exception? exception = Record.Exception(() => NumericDocValuesReader.OpenColumns(input, expectedDocumentCount: 1));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(allocated < 2 * 1024 * 1024,
            $"Rejecting a truncated presence bitmap allocated {allocated:N0} bytes from its nested chunk count.");
        CodecFileException corruption = Assert.IsType<CodecFileException>(exception);
        Assert.Equal(CodecFileErrorCode.SemanticValidationFailure, corruption.ErrorCode);
    }

    [Fact]
    public void OpenColumns_RejectsPresenceLengthOutsideFrameBody_ThenValidRewriteCanBeRead()
    {
        string path = Path.Combine(_directory, "malformed-presence-length.dvn");
        CodecFileWriter.WriteAtomically(path, Descriptor(DocValuesKind.Numeric), durable: false, body =>
        {
            body.WriteInt32(1);
            body.WriteString("field");
            body.WriteInt32(1); // declares one bitmap byte, but the frame body ends here
        });

        using (var input = new IndexInput(path))
        {
            CodecFileException corruption = Assert.Throws<CodecFileException>(
                () => NumericDocValuesReader.OpenColumns(input, expectedDocumentCount: 1));
            Assert.Equal(CodecFileErrorCode.SemanticValidationFailure, corruption.ErrorCode);
        }

        NumericDocValuesWriter.Write(path, new Dictionary<string, double[]> { ["field"] = [42] }, docCount: 1);
        var reopened = NumericDocValuesReader.Read(path, expectedDocumentCount: 1);
        Assert.Equal([42d], reopened.Values["field"]);
    }

    [Fact]
    public void BinaryValueCount_IsCheckedAgainstFrameBodyBeforeOffsetArrayAllocation()
    {
        string path = Path.Combine(_directory, "malformed-value-count.dvb");
        CodecFileWriter.WriteAtomically(path, Descriptor(DocValuesKind.Binary), durable: false, body =>
        {
            body.WriteInt32(1);
            body.WriteString("field");
            body.WriteInt32(1); // document count
            body.WriteInt32(0); // first document offset
            body.WriteInt32(0); // terminal document offset
            body.WriteInt32(int.MaxValue); // value count cannot fit its offset table
        });

        using var input = new IndexInput(path);
        CodecFileException corruption = Assert.Throws<CodecFileException>(
            () => BinaryDocValuesReader.OpenColumns(input, expectedDocumentCount: 1));
        Assert.Equal(CodecFileErrorCode.SemanticValidationFailure, corruption.ErrorCode);
    }

    [Theory]
    [InlineData(DocValuesKind.Sorted)]
    [InlineData(DocValuesKind.SortedSet)]
    public void MaterialisingTermAndFieldEnumerationShareTheValidatedParser(DocValuesKind kind)
    {
        string path = Path.Combine(_directory, $"malformed-enumeration-{kind}{Extension(kind)}");
        WriteDocumentCountOnly(path, kind, documentCount: 3);

        Action read = kind switch
        {
            DocValuesKind.Sorted => () => SortedDocValuesReader.Read(path, expectedDocumentCount: 1),
            DocValuesKind.SortedSet => () => SortedSetDocValuesReader.Read(path, expectedDocumentCount: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        Action readTerms = kind switch
        {
            DocValuesKind.Sorted => () => SortedDocValuesReader.ReadTerms(path, expectedDocumentCount: 1),
            DocValuesKind.SortedSet => () => SortedSetDocValuesReader.ReadTerms(path, expectedDocumentCount: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        Action enumerate = kind switch
        {
            DocValuesKind.Sorted => () => SortedDocValuesReader.EnumerateFields(path, expectedDocumentCount: 1),
            DocValuesKind.SortedSet => () => SortedSetDocValuesReader.EnumerateFields(path, expectedDocumentCount: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        Assert.Equal(CodecFileErrorCode.SemanticValidationFailure, Assert.Throws<CodecFileException>(read).ErrorCode);
        Assert.Equal(CodecFileErrorCode.SemanticValidationFailure, Assert.Throws<CodecFileException>(readTerms).ErrorCode);
        Assert.Equal(CodecFileErrorCode.SemanticValidationFailure, Assert.Throws<CodecFileException>(enumerate).ErrorCode);
    }

    private static byte[] CreateBitmapWithTruncatedChunkTable(int chunkCount)
    {
        byte[] payload = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(payload, chunkCount);
        byte[] body = new byte[sizeof(int) + payload.Length + sizeof(uint)];
        BinaryPrimitives.WriteInt32LittleEndian(body, payload.Length);
        payload.CopyTo(body.AsSpan(sizeof(int)));
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(sizeof(int) + payload.Length), Crc32.Compute(payload));

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            CodecFileHeader.Write(writer, CodecFormats.RoaringBitmap, body);
        return stream.ToArray();
    }

    private static object OpenColumns(IndexInput input, DocValuesKind kind)
        => kind switch
        {
            DocValuesKind.Numeric => NumericDocValuesReader.OpenColumns(input, expectedDocumentCount: 1),
            DocValuesKind.Sorted => SortedDocValuesReader.OpenColumns(input, expectedDocumentCount: 1),
            DocValuesKind.SortedSet => SortedSetDocValuesReader.OpenColumns(input, expectedDocumentCount: 1),
            DocValuesKind.SortedNumeric => SortedNumericDocValuesReader.OpenColumns(input, expectedDocumentCount: 1),
            DocValuesKind.Binary => BinaryDocValuesReader.OpenColumns(input, expectedDocumentCount: 1),
            DocValuesKind.Int64 => Int64DocValuesReader.OpenColumns(input, expectedDocumentCount: 1),
            DocValuesKind.Int64SortedNumeric => Int64SortedNumericDocValuesReader.OpenColumns(input, expectedDocumentCount: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static void WriteDocumentCountOnly(string path, DocValuesKind kind, int documentCount)
    {
        CodecFileWriter.WriteAtomically(path, Descriptor(kind), durable: false, body =>
        {
            body.WriteInt32(1);
            body.WriteString("field");
            switch (kind)
            {
                case DocValuesKind.Numeric:
                case DocValuesKind.Int64:
                    body.WriteInt32(0); // no presence bitmap
                    body.WriteInt32(documentCount);
                    body.WriteInt64(0);
                    body.WriteByte(0); // zero bits per value
                    break;
                case DocValuesKind.Sorted:
                    body.WriteInt32(0); // no presence bitmap
                    body.WriteInt32(documentCount);
                    body.WriteInt32(0); // no ordinals
                    body.WriteByte(0); // zero bits per ordinal
                    break;
                case DocValuesKind.SortedSet:
                case DocValuesKind.SortedNumeric:
                case DocValuesKind.Int64SortedNumeric:
                    body.WriteInt32(documentCount);
                    body.WriteInt32(0); // no values
                    break;
                case DocValuesKind.Binary:
                    body.WriteInt32(documentCount);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        });
    }

    private static CodecFileDescriptor Descriptor(DocValuesKind kind)
        => CodecCatalog.Default.GetFile(kind switch
        {
            DocValuesKind.Numeric => "leancorpus.doc-values.numeric",
            DocValuesKind.Sorted => "leancorpus.doc-values.sorted",
            DocValuesKind.SortedSet => "leancorpus.doc-values.sorted-set",
            DocValuesKind.SortedNumeric => "leancorpus.doc-values.sorted-numeric",
            DocValuesKind.Binary => "leancorpus.doc-values.binary",
            DocValuesKind.Int64 => "leancorpus.doc-values.int64",
            DocValuesKind.Int64SortedNumeric => "leancorpus.doc-values.int64-sorted-numeric",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        });

    private static string Extension(DocValuesKind kind)
        => kind switch
        {
            DocValuesKind.Numeric => ".dvn",
            DocValuesKind.Sorted => ".dvs",
            DocValuesKind.SortedSet => ".dss",
            DocValuesKind.SortedNumeric => ".dsn",
            DocValuesKind.Binary => ".dvb",
            DocValuesKind.Int64 => ".dvnl",
            DocValuesKind.Int64SortedNumeric => ".dsnl",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            TestDirectoryFixture.TryDeleteDirectory(_directory);
    }

    public enum DocValuesKind
    {
        Numeric,
        Sorted,
        SortedSet,
        SortedNumeric,
        Binary,
        Int64,
        Int64SortedNumeric,
    }
}
