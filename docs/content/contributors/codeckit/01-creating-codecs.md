# Creating codecs

Every codec implements `ICodec<T>` through two methods, encode and decode. CodecKit ships with primitives for scalar types and combinators for building up from there.

## Primitives

Leaf codecs for individual values. The primitive types themselves are internal; use them through the static members of `Codec` (namespace `Rowles.LeanCorpus.Codecs.CodecKit`).

```csharp
using Rowles.LeanCorpus.Codecs.CodecKit;

// Encode a single int
byte[] bytes = Codec.EncodeToArray(Codec.Int32LE, 42);

// Decode it back
int value = Codec.Decode(Codec.Int32LE, bytes);
```

Available primitives:

| Member | Wire format |
|---|---|
| `Codec.Int8`, `Codec.UInt8` | Single byte |
| `Codec.Int16LE`, `Codec.UInt16LE` (and `BE`) | 2 bytes |
| `Codec.Int32LE`, `Codec.UInt32LE` (and `BE`) | 4 bytes |
| `Codec.Int64LE`, `Codec.UInt64LE` (and `BE`) | 8 bytes |
| `Codec.Float32LE`, `Codec.Float64LE` (and `BE`) | IEEE 754 |
| `Codec.VarInt32`, `Codec.VarUInt32` | Variable-length integer |
| `Codec.VarInt64`, `Codec.VarUInt64` | Variable-length long |
| `Codec.Bool` | Single byte (`0x00` / `0x01`) |
| `Codec.GuidRfc4122`, `Codec.GuidDotNet` | 16 bytes |
| `Codec.Utf8String(n)`, `Codec.Utf8StringRemaining()` | UTF-8 bytes of a fixed length, or the rest of the frame |
| `Codec.BytesOwned(n)`, `Codec.BytesOwnedRemaining()` | Bytes copied into a `byte[]` |
| `Codec.BytesBorrowed(n)`, `Codec.BytesBorrowedRemaining()` | Like `BytesOwned` but returns a `ReadOnlySequence<byte>` slice |
| `Codec.Magic(...)`, `Codec.Padding(n)`, `Codec.Skip(n)` | Fixed bytes, validated (magic) or skipped on decode |

Variable-length strings and byte arrays are framed explicitly with a length prefix:

```csharp
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

// [VarInt32 byte length][UTF-8 bytes]
ICodec<string> name = Codec.LengthPrefixed(
    Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow);
```

`TrailingDataPolicy.Reject`, the default, rejects unread bytes inside the frame and any bytes after it, so it suits only a frame that ends the input. Use `Allow` for a length-prefixed value inside a record, a list or anything else that is followed by more data.

## Building a record codec

`Codec.Record<T>()` returns a `RecordBuilder<T>` that composes fields, in registration order, into a single codec:

```csharp
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

// Define a data type
public sealed record SegmentHeader(int DocCount, long MaxDoc, string Name);

// Build a codec for it
ICodec<string> name = Codec.LengthPrefixed(
    Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow);
var headerCodec = Codec.Record<SegmentHeader>()
    .Field("docCount", h => h.DocCount, Codec.VarInt32)
    .Field("maxDoc",   h => h.MaxDoc,   Codec.VarInt64)
    .Field("name",     h => h.Name,     name)
    .Build<int, long, string>((docCount, maxDoc, name) => new SegmentHeader(docCount, maxDoc, name));

// Use it
var header = new SegmentHeader(100, 99, "seg_0");
byte[] bytes = Codec.EncodeToArray(headerCodec, header);
var decoded = Codec.Decode(headerCodec, bytes);
```

`Build` accepts up to 16 typed parameters, given as explicit type arguments. For more fields use `Build(Func<FieldValues, T>)`.

## Constant fields

Fields that don't vary between instances (magic bytes, padding) use `Constant` with an `ICodec<Unit>`:

```csharp
var codec = Codec.Record<MyType>()
    .Constant("magic", Codec.Magic((byte)'L', (byte)'C'))
    .Field("version", t => t.Version, Codec.Int32LE)
    .Build<int>(version => new MyType(version));
```

Constants are validated on decode but not passed to the factory.

## Dependent fields

When a field's codec depends on a previously read field, pass a `DependentFieldFactory` from `Codec.From` or `RepeatFrom`:

```csharp
ICodec<string> item = Codec.LengthPrefixed(
    Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow);
var codec = Codec.Record<MyType>()
    .Field("count", t => t.Count, Codec.VarInt32)
    .Field("items", t => t.Items, item.RepeatFrom("count"))
    .Build<int, IReadOnlyList<string>>((count, items) => new MyType(count, items));
```

`Codec.From<TDep, TOut>("field", value => codec)` builds any other dependent codec.

## Optional fields

Optional values are reference types. With a flag:

```csharp
var optionalString = name.Optional(Codec.Bool);
// Wire format: [bool hasValue][value?]
```

Or with sentinel values that mark absence and presence:

```csharp
var optionalTagged = name.Optional(Codec.UInt8, absentValue: (byte)0x00, presentValue: (byte)0xFF);
// Wire format: [0x00] when absent, [0xFF][value] when present
```

## Version envelopes

Wraps a codec with `[version][VarInt64 bodyLen][body]`. Known versions dispatch to version cases, whose version values must have the version codec's type. Unknown versions pass raw bytes to a forward-compatibility delegate:

```csharp
var envelope = Codec.VersionEnvelope<byte[], byte>(
    versionCodec: Codec.UInt8,
    bodyLengthCodec: Codec.VarInt64,
    unknown: (version, bytes) => bytes,
    cases:
    [
        Codec.VersionCase<byte[], byte[]>((byte)1, "v1-body", Codec.BytesOwnedRemaining()),
        Codec.VersionCase<byte[], byte[]>((byte)2, "v2-body", Codec.BytesOwnedRemaining()),
    ]);
```

`Codec.Versioned` is the same discriminated union without the body-length frame.

## Adding checksums

```csharp
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum;

var withChecksum = innerCodec.WithChecksum(
    ChecksumAlgorithms.Crc32,
    ChecksumPlacement.Trailer);
```

Wire format with `Trailer`: `[body][checksum]`. With `Header`: `[checksum][body]`. `ChecksumAlgorithms` (CRC32, xxHash32, xxHash64) is internal; their providers are registered in `CodecRegistry.Default`. Decode verifies the checksum and throws on mismatch.

## Adding compression

```csharp
var withCompression = innerCodec.WithCompression();
// Wire format: [VarUInt32 compressedLen][deflate(body)]
```

Compresses the encoded body with deflate at `CodecCompressionLevel.Optimal` by default. Decode decompresses and passes the result to the inner codec.

## See also

- [CodecKit overview](index.md)
- [Adding formats to the pipeline](02-adding-formats.md)
- [Codec migrations](03-migrations.md)
