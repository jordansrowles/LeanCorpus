# CodecKit

CodecKit has two related jobs:

1. `ICodec<T>` composes in-memory values into binary bodies.
2. `CodecCatalog` describes every persistent LeanCorpus file role, while canonical Frame v1 identifies and protects current binary files.

The catalogue is the authority for persistent format IDs, body-format versions, file matching, access patterns, checksums and migration policy. Fixed internal version envelopes remain only for reading supported 2.x files.

## Key types

| Type | Role |
|---|---|
| `ICodec<T>` | Immutable body encoder and decoder |
| `CodecCatalog` | Immutable catalogue of persistent format families and file roles |
| `CodecCatalogBuilder` | Builds and validates a catalogue snapshot |
| `CodecFileDescriptor` | Stable format ID, matcher, versions, framing, checksum and migration policy |
| `CodecFileWriter` | Streams a current body into canonical Frame v1 |
| `CodecFileReader` | Opens canonical or explicitly supported legacy framing |
| `CodecWriteSession` | Owns a streaming body output and finalises its footer |
| `CodecBodyReadSession` | Exposes a bounded body and its detected version |
| `CodecContext` | Per-operation paths, limits, scratch space and checkpoints |
| `CodecOptions` | Immutable resource and materialisation limits |

## Persistent write path

Normal flush, merge, direct and migration writers must call the same current writer. A typical streaming writer is:

```csharp
var descriptor = catalog.GetFile("example.product.data");
CodecFileWriter.WriteAtomically(path, descriptor, durable: false, body =>
{
    body.WriteInt32(items.Count);
    foreach (var item in items)
        WriteItem(body, item);
});
```

Large bodies should be written incrementally. Do not assemble a postings, vector, BKD or stored-field file in one `byte[]` merely to add framing.

## Persistent read path

Current readers use the descriptor and retain a bounded input where random access is required:

```csharp
var descriptor = catalog.GetFile("example.product.data");
using var input = new IndexInput(path);
using var frame = CodecFileReader.OpenSupported(input, descriptor);

long bodyStart = frame.BodyStart;
long bodyLength = frame.BodyLength;
```

Opening is structural and does not automatically scan a large body checksum. Deep validation calls the checksum scan explicitly. Materialising readers call `ReadBody()`, which applies `CodecOptions.MaxMaterialisedBodyBytes`.

## Canonical Frame v1

All integers are little-endian.

```text
[magic: uint32 = LCCF]
[frame-version: byte = 1]
[format-id-length: byte]
[format-version: int32]
[flags: uint32 = 0]
[checksum-algorithm: byte]
[reserved: byte = 0]
[format-id: lowercase ASCII]
[body]
[body-length: int64]
[body-checksum: uint64]
```

Frame version and body-format version are separate. Current built-in binary formats use xxHash64 over the body only. The footer must end at the physical or logical file boundary, so the same parser works for loose files and bounded compound members.

## Logical files and compound storage

`ISegmentFileSource` presents the same logical member interface for loose and compound segments. Inspection, compatibility and validation work on logical names and bounded inputs. Code that guesses a format from the `.cfs` container name is incorrect.

## Pure codecs remain useful

Frame v1 does not replace `ICodec<T>`. Primitives, record builders, optional values, checksums and compression combinators remain appropriate inside bodies or for non-persistent values. See [Creating codecs](01-creating-codecs.md).

## Source layout

```text
Codecs/CodecKit/
├── Catalog/                  immutable format catalogue and descriptors
├── CodecFileFrame.cs         frame metadata and structured errors
├── CodecFileWriter.cs        canonical streaming writer
├── CodecFileReader.cs        canonical and supported-reader entry points
├── LegacyCodecFileReader.cs  legacy envelope and trailer adapters
├── Codecs/                   ICodec implementations and operation context
├── Primitives/               scalar body codecs
├── Combinators/              higher-level body composition
└── Checksum/                 checksum providers and accumulators
```

## See also

- [Adding persistent formats](02-adding-formats.md)
- [Codec migrations](03-migrations.md)
- [Storage formats](../storage-formats.md)
- [ADR025: unified codec catalogue](../../articles/ADRs/ADR025-unified-codec-catalogue.md)
- [ADR026: canonical binary file frame](../../articles/ADRs/ADR026-canonical-binary-file-frame.md)
