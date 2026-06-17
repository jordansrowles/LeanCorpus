# Adding formats to the pipeline

A "format" is a named codec ID (like `"pos"` or `"nrm"`) registered in `CodecMigrationRegistry` with one or more `CodecVersionStep`s. Readers and writers use the format via `CodecFileHeader`.

## Register a format

```csharp
using Rowles.LeanCorpus.Codecs.CodecKit;

var reg = CodecMigrationRegistry.Default;

reg.Register(new CodecFormat("myf", [
    new CodecVersionStep(1, "myf-v1", Codec.BytesOwnedRemaining()),
]));
```

The `CodecVersionStep` holds a reader codec for that version. The body codec is typically `BytesOwnedRemaining()` (reads all remaining bytes as `byte[]`). The actual parsing of those bytes happens in the reader class, not in the codec itself.

At startup, built-in formats are registered by `CodecFormats`:

```csharp
// CodecFormats.cs (simplified)
reg.Register(new CodecFormat("nrm", [
    new CodecVersionStep(1, "nrm-v1", Codec.BytesOwnedRemaining()),
    new CodecVersionStep(2, "nrm-v2", Codec.BytesOwnedRemaining())
]));

reg.Register(new CodecFormat("fln", [
    new CodecVersionStep(1, "fln-v1", Codec.BytesOwnedRemaining())
]));
```

## Wire into a writer

```csharp
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;

// Build body bytes in an ArrayBufferWriter<byte>
var bodyBuf = new ArrayBufferWriter<byte>();
// ... write your data into bodyBuf ...

// Write the envelope: version byte + length + body
CodecFileHeader.Write(output, CodecFormats.Norms, bodyBuf.WrittenSpan);
```

`CodecFileHeader.Write` looks up the current version from `CodecConstants` and writes `[version:byte][VarInt64 bodyLen][body bytes]`.

## Wire into a reader

```csharp
// Read version byte (validates against the format's version steps)
byte version = CodecFileHeader.ReadVersion(input, CodecFormats.Norms);

// Read the body length and body bytes
int bodyLen = Codec.Decode<int>(VarInt32Codec.Instance, input);
byte[] body = new byte[bodyLen];
input.ReadBytes(body, 0, bodyLen);

// Parse body bytes based on version
switch (version)
{
    case 1:
        // parse v1 body
        break;
    case 2:
        // parse v2 body
        break;
}
```

`ReadVersion` dispatches through the format's version step to read the version byte and validate it. The body length and parsing are handled by your reader.

## CodecConstants versions

Each format has a version constant in `CodecConstants`:

```csharp
public static class CodecConstants
{
    public const int NormsVersion          = 2;
    public const int PostingsVersion       = 1;
    public const int StoredFieldsVersion   = 1;
    public const int TermVectorsVersion    = 2;
    public const int HnswVersion           = 1;
    public const int VectorVersion         = 1;
    // ...
}
```

`CodecFileHeader.Write` uses these constants to pick the current version. When you bump a version, update both `CodecConstants` and the `CodecFormat` registration.

## The complete picture

```
Writer                          Reader
──────                          ──────
Build body bytes                CodecFileHeader.ReadVersion(format)
  ↓                                   ↓
CodecFileHeader.Write(              reads version byte
  output, format, body)              validates against format steps
  ↓                                   ↓
writes [version][length][body]     reads body length
                                   reads body bytes
                                   parses based on version
```

## See also

- [CodecKit overview](index.md)
- [Creating codecs](01-creating-codecs.md)
- [Codec migrations](03-migrations.md)
