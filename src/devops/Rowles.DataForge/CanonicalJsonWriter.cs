using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rowles.DataForge;

/// <summary>Writes the DataForge canonical JSON subset directly to a byte stream.</summary>
public sealed class CanonicalJsonWriter : IDisposable
{
    private readonly Stream stream;
    private readonly IncrementalHash hash;
    private readonly List<ContainerState> containers = [];
    private long bytesWritten;

    public CanonicalJsonWriter(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("The stream must be writable.", nameof(stream));
        this.stream = stream;
        hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    }

    public long BytesWritten => bytesWritten;

    public void WriteStartObject()
    {
        BeforeValue();
        WriteAscii("{");
        containers.Add(new ContainerState(isObject: true));
    }

    public void WriteEndObject()
    {
        var state = GetContainer(isObject: true);
        if (state.AwaitingValue)
            throw new InvalidOperationException("An object property has no value.");
        containers.RemoveAt(containers.Count - 1);
        WriteAscii("}");
    }

    public void WriteStartArray()
    {
        BeforeValue();
        WriteAscii("[");
        containers.Add(new ContainerState(isObject: false));
    }

    public void WriteEndArray()
    {
        _ = GetContainer(isObject: false);
        containers.RemoveAt(containers.Count - 1);
        WriteAscii("]");
    }

    public void WritePropertyName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var state = GetContainer(isObject: true);
        if (state.AwaitingValue)
            throw new InvalidOperationException("The previous object property has no value.");
        if (!state.First)
            WriteAscii(",");
        state.First = false;
        WriteEscapedString(name);
        WriteAscii(":");
        state.AwaitingValue = true;
    }

    public void WriteStringValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BeforeValue();
        WriteEscapedString(value);
    }

    public void WriteNullValue()
    {
        BeforeValue();
        WriteAscii("null");
    }

    public void WriteBooleanValue(bool value)
    {
        BeforeValue();
        WriteAscii(value ? "true" : "false");
    }

    public void WriteInt32Value(int value)
    {
        BeforeValue();
        Span<byte> buffer = stackalloc byte[11];
        if (!Utf8Formatter.TryFormat(value, buffer, out var written))
            throw new InvalidOperationException("Could not format an Int32 value.");
        WriteBytes(buffer[..written]);
    }

    public void WriteInt64Value(long value)
    {
        BeforeValue();
        Span<byte> buffer = stackalloc byte[20];
        if (!Utf8Formatter.TryFormat(value, buffer, out var written))
            throw new InvalidOperationException("Could not format an Int64 value.");
        WriteBytes(buffer[..written]);
    }

    public void WriteUInt64Value(ulong value)
    {
        BeforeValue();
        Span<byte> buffer = stackalloc byte[20];
        if (!Utf8Formatter.TryFormat(value, buffer, out var written))
            throw new InvalidOperationException("Could not format a UInt64 value.");
        WriteBytes(buffer[..written]);
    }

    public void WriteBase64Value(ReadOnlySpan<byte> value)
    {
        BeforeValue();
        WriteEscapedString(Convert.ToBase64String(value));
    }

    public void WriteSingleBitsBase64Value(ReadOnlySpan<float> values)
    {
        var bytes = new byte[checked(values.Length * sizeof(float))];
        for (var i = 0; i < values.Length; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(i * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(values[i]));
        WriteBase64Value(bytes);
    }

    public void WriteUtcTimestampValue(DateTimeOffset value)
    {
        BeforeValue();
        var text = value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        WriteEscapedString(text);
    }

    public void WriteLine()
    {
        if (containers.Count != 0)
            throw new InvalidOperationException("A record can end only after its JSON value is complete.");
        WriteByte((byte)'\n');
    }

    public byte[] GetSha256() => hash.GetHashAndReset();

    public void Dispose() => hash.Dispose();

    private ContainerState GetContainer(bool isObject)
    {
        if (containers.Count == 0 || containers[^1].IsObject != isObject)
            throw new InvalidOperationException(isObject ? "The current JSON container is not an object." : "The current JSON container is not an array.");
        return containers[^1];
    }

    private void BeforeValue()
    {
        if (containers.Count == 0)
            return;

        var state = containers[^1];
        if (state.IsObject)
        {
            if (!state.AwaitingValue)
                throw new InvalidOperationException("An object value must follow a property name.");
            state.AwaitingValue = false;
            return;
        }

        if (!state.First)
            WriteAscii(",");
        state.First = false;
    }

    private void WriteEscapedString(string value)
    {
        WriteByte((byte)'"');
        var remaining = value.AsSpan();
        Span<byte> escaped = stackalloc byte[6];
        Span<byte> utf8 = stackalloc byte[4];
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            if (status != OperationStatus.Done)
                throw new ArgumentException("Strings must not contain unpaired UTF-16 surrogates.", nameof(value));
            remaining = remaining[consumed..];

            if (rune.Value == '"')
            {
                WriteAscii("\\\"");
            }
            else if (rune.Value == '\\')
            {
                WriteAscii("\\\\");
            }
            else if (rune.Value <= 0x1f)
            {
                escaped[0] = (byte)'\\';
                escaped[1] = (byte)'u';
                escaped[2] = (byte)'0';
                escaped[3] = (byte)'0';
                const string Hex = "0123456789abcdef";
                escaped[4] = (byte)Hex[(rune.Value >> 4) & 0xf];
                escaped[5] = (byte)Hex[rune.Value & 0xf];
                WriteBytes(escaped);
            }
            else
            {
                var count = rune.EncodeToUtf8(utf8);
                WriteBytes(utf8[..count]);
            }
        }
        WriteByte((byte)'"');
    }

    private void WriteAscii(string value)
    {
        Span<byte> bytes = stackalloc byte[64];
        if (value.Length > bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(value), "Canonical ASCII tokens must be short.");
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] > 0x7f)
                throw new ArgumentException("The token must contain ASCII characters only.", nameof(value));
            bytes[i] = (byte)value[i];
        }
        WriteBytes(bytes[..value.Length]);
    }

    private void WriteByte(byte value)
    {
        Span<byte> single = stackalloc byte[1] { value };
        WriteBytes(single);
    }

    private void WriteBytes(ReadOnlySpan<byte> value)
    {
        stream.Write(value);
        hash.AppendData(value);
        bytesWritten += value.Length;
    }

    private sealed class ContainerState(bool isObject)
    {
        public bool IsObject { get; } = isObject;

        public bool First { get; set; } = true;

        public bool AwaitingValue { get; set; }
    }
}
