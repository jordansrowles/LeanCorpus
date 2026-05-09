using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.DocValues;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Contains unit tests for binary DocValues.
/// </summary>
[Trait("Category", "Codecs")]
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
        OverwriteInt32(path, StartsOffset(fieldName) + sizeof(int), 0);

        Assert.Throws<InvalidDataException>(() => BinaryDocValuesReader.Read(path));
    }

    private static int StartsOffset(string fieldName)
    {
        int byteCount = System.Text.Encoding.UTF8.GetByteCount(fieldName);
        return CodecConstants.HeaderSize + sizeof(int) + VarIntLength(byteCount) + byteCount + sizeof(int);
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
}
