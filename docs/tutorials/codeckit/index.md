# CodecKit

CodecKit is the internal serialisation framework that every LeanCorpus codec file format is built on. It handles versioned envelopes, checksumming, compression, and recovery.

This section is for contributors adding or evolving codec formats. End users don't need to touch CodecKit directly.

## Key types

| Type | Role |
|---|---|
| `ICodec<T>` | Central interface: `Encode(T, IBufferWriter<byte>, CodecContext)` and `Decode(ref SequenceReader<byte>, CodecContext)` |
| `Codec` | Static entry point with `Encode`, `Decode`, `TryEncode`, `TryDecode`, `EncodeToArray` |
| `CodecFileHeader` | Wraps a codec body in `[version:byte][VarInt64 bodyLen][body]` |
| `CodecFormat` | Maps a codec ID (e.g. `"pos"`, `"nrm"`) to an ordered list of `CodecVersionStep`s |
| `CodecVersionStep` | One version of a format: `(int Version, string Label, ICodec<byte[]> Reader)` |
| `CodecMigrationRegistry` | Global registry of all codec formats, keyed by codec ID |
| `CodecContext` | Per-operation mutable state: depth, path, offsets, scratch buffers, checkpoints |
| `CodecOptions` | Immutable config: max frame bytes, max nesting depth, UTF-8 validation, etc. |

## How it fits in

At write time, a segment flusher builds the body bytes for most codec files (postings, doc values, etc.) and calls `CodecFileHeader.Write(output, CodecFormats.Postings, bodySpan)`. The header writes the current version byte and length-prefixed body. Stored fields (.fdt/.fdx) stream directly to `IndexOutput` via `StoredFieldsFileHeader` and only buffer block offsets for the index.

At read time, `CodecFileHeader.ReadVersion(input, CodecFormats.Postings)` reads the version byte and dispatches to the correct `CodecVersionStep`'s reader codec.

CodecKit lives under `Rowles.LeanCorpus.Codecs.CodecKit`.

## File layout

```
CodecKit/
├── Codec.cs, ICodec.cs          # Entry point and interface
├── CodecFileHeader.cs           # Envelope writer/reader
├── CodecFormat.cs               # Format = codec ID + version steps
├── CodecVersionStep.cs          # One version of a format
├── CodecMigrationRegistry.cs    # Global format registry
├── Codecs/                      # Codec definitions
│   ├── CodecContext.cs          # Per-operation state
│   ├── CodecOptions.cs          # Global limits
│   ├── CodecResult.cs           # Success/failure struct
│   ├── CodecFailure.cs          # Error details
│   ├── RecordBuilder.cs         # Fluent builder for record codecs
│   └── ...
├── Internal/                    # Implementation codecs
│   ├── RecordCodec.cs           # Composes N fields into one codec
│   ├── VersionEnvelopeCodec.cs  # [version][length][body]
│   ├── VersionedCodec.cs        # [version][body] (no length prefix)
│   ├── ChoiceCodec.cs           # Tag-based discriminated union
│   ├── WithChecksumCodec.cs     # Wraps inner codec with checksum
│   ├── WithCompressionCodec.cs  # Wraps inner codec with deflate
│   ├── LengthPrefixedCodec.cs   # [length][body]
│   ├── OptionalCodec.cs         # [flag][body?]
│   ├── RepeatCodec.cs           # Fixed/repeated elements
│   └── ...
├── Combinators/
│   └── IndexedCodec.cs          # Eager header + lazy body via IndexInput
├── Primitives/                  # Leaf codecs for scalar types
│   ├── Int32LECodec.cs          # Little-endian 32-bit int
│   ├── VarInt32Codec.cs         # Variable-length integer
│   ├── Utf8StringCodec.cs       # Length-prefixed UTF-8 string
│   └── ...
├── Checksum/                    # Checksum algorithms
├── Compression/                 # Compression level enum
├── Recovery/                    # Frame scanning for corrupt data
└── Formats/
    └── CodecFormats.cs          # Built-in format registrations
```
