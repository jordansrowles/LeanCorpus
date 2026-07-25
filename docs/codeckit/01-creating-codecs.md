# Creating codecs

Every codec implements `ICodec<T>` — two methods, encode and decode. CodecKit ships with primitives for scalar types and combinators for building up from there.

## Primitives

Leaf codecs for individual values. All in `Rowles.LeanCorpus.Codecs.CodecKit.Primitives`.

```csharp
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

// Encode a single int
byte[] bytes = Codec.EncodeToArray(Int32LECodec.Instance, 42);

// Decode it back
int value = Codec.Decode<int>(Int32LECodec.Instance, bytes);
```

Available primitives:

| Codec | Wire format |
|---|---|
| `Int8Codec`, `UInt8Codec` | Single byte |
| `Int16LECodec`, `UInt16LECodec` | 2 bytes, little-endian |
| `Int32LECodec`, `UInt32LECodec` | 4 bytes, little-endian |
| `Int64LECodec`, `UInt64LECodec` | 8 bytes, little-endian |
| `Float32LECodec`, `Float64LECodec` | IEEE 754, little-endian |
| `VarInt32Codec`, `VarUInt32Codec` | Variable-length integer |
| `VarInt64Codec`, `VarUInt64Codec` | Variable-length long |
| `BoolCodec` | Single byte (`0x00` / `0x01`) |
| `Utf8StringCodec` | `[VarUInt32 length][UTF-8 bytes]` |
| `BytesOwnedCodec` | `[VarUInt32 length][bytes]` (allocates `byte[]`) |
| `BytesBorrowedCodec` | Like `BytesOwnedCodec` but returns a `ReadOnlySequence<byte>` slice |
| `MagicCodec` | Fixed magic bytes, validated on decode |

## Building a record codec

`RecordBuilder<T>` composes multiple fields into a single codec:

```csharp
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

// Define a data type
public sealed record SegmentHeader(int DocCount, long MaxDoc, string Name);

// Build a codec for it
var headerCodec = new RecordBuilder<SegmentHeader>()
    .Field("docCount", h => h.DocCount, VarInt32Codec.Instance)
    .Field("maxDoc",   h => h.MaxDoc,   VarInt64Codec.Instance)
    .Field("name",     h => h.Name,     Utf8StringCodec.Instance)
    .Build((docCount, maxDoc, name) => new SegmentHeader(docCount, maxDoc, name));

// Use it
var header = new SegmentHeader(100, 99, "seg_0");
byte[] bytes = Codec.EncodeToArray(headerCodec, header);
var decoded = Codec.Decode<SegmentHeader>(headerCodec, bytes);
```

`Build` accepts up to 16 typed parameters. For more fields use `Build(Func<FieldValues, T>)`.

## Constant fields

Fields that don't vary between instances (magic bytes, padding) use `Constant`:

```csharp
var codec = new RecordBuilder<MyType>()
    .Constant("magic", new MagicCodec("LC"u8))
    .Field("version", t => t.Version, Int32LECodec.Instance)
    .Build(version => new MyType(version));
```

Constants are validated on decode but not passed to the factory.

## Dependent fields

When a field's codec depends on a previously-read field:

```csharp
var codec = new RecordBuilder<MyType>()
    .Field("count", t => t.Count, VarInt32Codec.Instance)
    .Field<int, string[]>("items", t => t.Items,
        count => Codec.Repeat(Utf8StringCodec.Instance, count))
    .Build((count, items) => new MyType(count, items));
```

## Optional fields

```csharp
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;

var optionalString = Utf8StringCodec.Instance.Optional(BoolCodec.Instance);
// Wire format: [bool hasValue][value?]
```

Or with a sentinel value:

```csharp
var optionalInt = Int32LECodec.Instance.Optional(sentinel: -1);
// Wire format: [int32 value]; -1 means absent
```

## Version envelopes

Wraps a codec with `[version:byte][VarInt64 bodyLen][body]`. Known versions dispatch to version cases. Unknown versions pass raw bytes to a forward-compat delegate:

```csharp
var envelope = Codec.VersionEnvelope<byte[], int>(
    versionCodec: UInt8Codec.Instance,
    bodyLengthCodec: VarInt64Codec.Instance,
    unknown: (version, bytes) => bytes,
    cases:
    [
        Codec.VersionCase<byte[], byte[]>(1, "v1-body", BytesOwnedCodec.Instance),
        Codec.VersionCase<byte[], byte[]>(2, "v2-body", BytesOwnedCodec.Instance),
    ]);
```

## Adding checksums

```csharp
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum;

var withChecksum = innerCodec.WithChecksum(
    ChecksumAlgorithms.Crc32,
    ChecksumPlacement.Trailer);
```

Wire format with `Trailer`: `[body][checksum]`. With `Header`: `[checksum][body]`. Decode verifies and throws `ChecksumMismatchException` on mismatch.

## Adding compression

```csharp
var withCompression = innerCodec.WithCompression();
// Wire format: [VarUInt32 compressedLen][deflate(body)]
```

Compresses the encoded body with deflate. Decode decompresses and passes to the inner codec.

## See also

- [CodecKit overview](index.md)
- [Adding formats to the pipeline](02-adding-formats.md)
- [Codec migrations](03-migrations.md)
