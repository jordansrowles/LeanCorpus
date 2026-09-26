# Stored field compression

Stored fields are written in compressed blocks under `.fdt`.

## Choose a policy

```csharp
var config = new IndexWriterConfig
{
    CompressionPolicy = FieldCompressionPolicy.Deflate, // default
    StoredFieldBlockSize = 16,
};
```

| Policy | Package | Notes |
|---|---|---|
| `None` | Core | No compression. Fastest write, largest disk |
| `Deflate` (default) | Core | BCL `DeflateStream`. Good ratio |
| `Brotli` | Core | BCL `BrotliStream`. Better ratio, slower writes |
| `Lz4` | `LeanCorpus.Compression.LZ4` | Very fast, modest ratio |
| `Snappy` | `LeanCorpus.Compression.Snappy` | Similar speed to LZ4 |
| `Zstandard` | `LeanCorpus.Compression.Zstandard` | Better ratio than LZ4, still fast |

The policy is recorded in the segment header; reads tolerate mixed segments.

## Optional codecs

Install and register:

```csharp
Lz4Compression.Register();
SnappyCompression.Register();
ZstandardCompression.Register();
```

In standard .NET the module initialiser registers automatically. In Native AOT, call `Register()` explicitly at startup.

## Block size

`StoredFieldBlockSize` (default `16`) is the maximum number of documents in a compression block. The writer also targets 1 MiB of raw stored-field data and flushes before the next document would exceed that target. A single larger document gets its own block, up to the 256 MiB hard limit.

Retrieval cost scales with the raw bytes in the selected block. Current readers understand the byte-bounded v4 layout and continue to read stored-fields versions 1 through 3. Older builds reject v4 segments rather than mis-mapping documents.

## Trade-offs

- Write speed: `None` > `Lz4` ≈ `Snappy` > `Deflate` > `Zstandard` > `Brotli`
- Disk size: `Brotli` ≈ `Zstandard` < `Deflate` < `Lz4` ≈ `Snappy` < `None`
- Retrieval cost scales with block bytes and the document-count maximum

## See also

- <xref:Rowles.LeanCorpus.Codecs.StoredFields.FieldCompressionPolicy>
