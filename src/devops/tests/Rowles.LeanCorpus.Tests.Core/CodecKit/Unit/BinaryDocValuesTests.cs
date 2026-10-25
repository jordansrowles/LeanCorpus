using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

/// <summary>
/// Contains unit tests for binary DocValues.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class BinaryDocValuesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ll-dvb-{Guid.NewGuid():N}");

    public BinaryDocValuesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>
    /// Verifies repeated UTF-8 values and empty strings round-trip in order.
    /// </summary>
    [Fact(DisplayName = "Roundtrip: Repeated Values Preserve Order")]
    public void Roundtrip_RepeatedValues_PreserveOrder()
    {
        var path = Path.Combine(_dir, "stored.dvb");
        static byte[] Bytes(string value) => System.Text.Encoding.UTF8.GetBytes(value);

        IReadOnlyList<byte[]>?[] values =
        [
            [Bytes("alpha"), Bytes(""), Bytes("bravo")],
            null,
            [Bytes("cafe")]
        ];
        var fields = new Dictionary<string, IReadOnlyList<byte[]>?[]>
        {
            ["stored"] = values
        };

        BinaryDocValuesWriter.Write(path, fields, 3);
        var result = BinaryDocValuesReader.Read(path);

        Assert.Equal(["alpha", "", "bravo"], result["stored"][0].Select(static value => System.Text.Encoding.UTF8.GetString(value)));
        Assert.Empty(result["stored"][1]);
        Assert.Equal(["cafe"], result["stored"][2].Select(static value => System.Text.Encoding.UTF8.GetString(value)));
    }

    /// <summary>
    /// Verifies missing optional sidecar files are treated as empty.
    /// </summary>
    [Fact(DisplayName = "Read: Missing File Returns Empty")]
    public void Read_MissingFile_ReturnsEmpty()
    {
        var result = BinaryDocValuesReader.Read(Path.Combine(_dir, "missing.dvb"));
        Assert.Empty(result);
    }

    /// <summary>
    /// Verifies corrupt document offsets are rejected before values are exposed.
    /// </summary>
    [Fact(DisplayName = "Read: Invalid Terminal Offset Throws")]
    public void Read_InvalidTerminalOffset_Throws()
    {
        const string fieldName = "stored";
        var path = Path.Combine(_dir, "corrupt.dvb");
        IReadOnlyList<byte[]>?[] values =
        [
            [System.Text.Encoding.UTF8.GetBytes("alpha")]
        ];
        var fields = new Dictionary<string, IReadOnlyList<byte[]>?[]>
        {
            [fieldName] = values
        };

        BinaryDocValuesWriter.Write(path, fields, 1);
        OverwriteInt32(path, TerminalByteOffset(path, fieldName, docCount: 1, valueCount: 1), int.MinValue);

        CodecFileException corruption = Assert.Throws<CodecFileException>(() => BinaryDocValuesReader.Read(path));
        Assert.Equal(CodecFileErrorCode.SemanticValidationFailure, corruption.ErrorCode);

        BinaryDocValuesWriter.Write(path, fields, 1);
        Assert.Equal("alpha", System.Text.Encoding.UTF8.GetString(BinaryDocValuesReader.Read(path)[fieldName][0][0]));
    }

    /// <summary>
    /// Verifies cumulative binary payload offsets are rejected before an overflowing table is emitted.
    /// </summary>
    [Fact(DisplayName = "Write: Payload Beyond Signed Offset Range Is Rejected Before Output")]
    public void Write_PayloadBeyondSignedOffsetRange_IsRejectedBeforeOutput()
    {
        const int valueSize = 1024;
        const int valueCount = 2_097_152;
        const int fieldPrefixBytes = 2 + sizeof(int) + 2 * sizeof(int) + sizeof(int);
        var payload = new byte[valueSize];
        IReadOnlyList<byte[]> repeatedValues = new RepeatedByteArrayList(payload, valueCount);
        IReadOnlyList<byte[]>?[] documents = [repeatedValues];
        int offsetTableEnd = fieldPrefixBytes + checked((valueCount + 1) * sizeof(int));
        var output = new StopAfterOffsetTableBufferWriter(offsetTableEnd);

        Exception? exception = Record.Exception(() =>
            BinaryDocValuesWriter.WriteFieldBlock(output, "x", documents, docCount: 1));

        if (exception is PayloadWriteReachedException)
        {
            Assert.True(
                output.LastWrittenInt32 >= 0,
                $"The legacy writer emitted a wrapped terminal offset of {output.LastWrittenInt32} before payload output.");
        }

        Assert.IsType<ArgumentException>(exception);
        Assert.Equal(0, output.WrittenBytes);
    }

    /// <summary>
    /// Verifies an oversized field cannot replace a valid file or leave its temporary output behind.
    /// </summary>
    [Fact(DisplayName = "Write: Oversized Payload Does Not Publish or Replace Existing File")]
    public void Write_OversizedPayload_DoesNotPublishOrReplaceExistingFile()
    {
        const string fieldName = "payload";
        var path = Path.Combine(_dir, "oversized.dvb");
        var existingFields = new Dictionary<string, IReadOnlyList<byte[]>?[]>
        {
            [fieldName] = [new[] { System.Text.Encoding.UTF8.GetBytes("kept") }]
        };
        BinaryDocValuesWriter.Write(path, existingFields, docCount: 1);
        byte[] existingBytes = File.ReadAllBytes(path);

        var oversizedFields = new Dictionary<string, IReadOnlyList<byte[]>?[]>
        {
            [fieldName] = CreateBeyondSignedOffsetRangeValues()
        };

        Assert.Throws<ArgumentException>(() => BinaryDocValuesWriter.Write(path, oversizedFields, docCount: 1));

        Assert.Equal(existingBytes, File.ReadAllBytes(path));
        Assert.Equal("kept", System.Text.Encoding.UTF8.GetString(BinaryDocValuesReader.Read(path)[fieldName][0][0]));
        Assert.Empty(Directory.EnumerateFiles(_dir, "oversized.dvb.*.codec.tmp"));
    }

    private static int StartsOffset(string path, string fieldName)
    {
        int byteCount = System.Text.Encoding.UTF8.GetByteCount(fieldName);
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.Open(input, CodecCatalog.Default.GetFile("leancorpus.doc-values.binary"));
        return checked((int)frame.Metadata.BodyStart) + sizeof(int) + VarIntLength(byteCount) + byteCount + sizeof(int);
    }

    private static int TerminalByteOffset(string path, string fieldName, int docCount, int valueCount)
        => checked(StartsOffset(path, fieldName)
            + (docCount + 1) * sizeof(int)
            + sizeof(int)
            + valueCount * sizeof(int));

    private static IReadOnlyList<byte[]>?[] CreateBeyondSignedOffsetRangeValues()
    {
        const int valueSize = 1024;
        const int valueCount = 2_097_152;
        return [new RepeatedByteArrayList(new byte[valueSize], valueCount)];
    }

    private static int VarIntLength(int value)
    {
        uint remaining = (uint)value;
        int length = 1;
        while (remaining >= 0x80)
        {
            remaining >>= 7;
            length++;
        }

        return length;
    }

    private static void OverwriteInt32(string path, int offset, int value)
    {
        var bytes = File.ReadAllBytes(path);
        BitConverter.TryWriteBytes(bytes.AsSpan(offset, sizeof(int)), value);
        File.WriteAllBytes(path, bytes);
    }

    private sealed class RepeatedByteArrayList(byte[] value, int count) : IReadOnlyList<byte[]>
    {
        public int Count => count;

        public byte[] this[int index]
            => (uint)index < (uint)count ? value : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<byte[]> GetEnumerator()
        {
            for (int index = 0; index < count; index++)
                yield return value;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class StopAfterOffsetTableBufferWriter(int stopAfterBytes) : IBufferWriter<byte>
    {
        private readonly byte[] _scratch = new byte[4096];

        public int WrittenBytes { get; private set; }
        public int LastWrittenInt32 { get; private set; }

        public void Advance(int count)
        {
            if (count < 0 || count > _scratch.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == sizeof(int))
                LastWrittenInt32 = BinaryPrimitives.ReadInt32LittleEndian(_scratch);
            WrittenBytes = checked(WrittenBytes + count);
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            GetSpan(sizeHint);
            return _scratch;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            if (WrittenBytes >= stopAfterBytes)
                throw new PayloadWriteReachedException();
            if (sizeHint > _scratch.Length)
                throw new ArgumentOutOfRangeException(nameof(sizeHint));
            return _scratch;
        }
    }

    private sealed class PayloadWriteReachedException : Exception
    {
    }
}
