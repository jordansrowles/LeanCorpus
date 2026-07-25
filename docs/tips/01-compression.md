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

`StoredFieldBlockSize` (default `16`) controls how many documents share a compression block. Larger blocks compress better but cost more on single-document retrieval.

## Trade-offs

- Write speed: `None` > `Lz4` ≈ `Snappy` > `Deflate` > `Zstandard` > `Brotli`
- Disk size: `Brotli` ≈ `Zstandard` < `Deflate` < `Lz4` ≈ `Snappy` < `None`
- Retrieval cost scales with block size, not policy

## See also

- <xref:Rowles.LeanCorpus.Codecs.StoredFields.FieldCompressionPolicy>
