# Stored field compression

Stored fields are written in blocks under the
`.fdt` extension and compressed.

## Choose a policy

```csharp
var config = new IndexWriterConfig
{
    CompressionPolicy = FieldCompressionPolicy.Deflate, // default
    StoredFieldBlockSize = 16,                          // docs per block
};
```

| Policy | Package | Notes |
|---|---|---|
| `None` | Core | No compression. Fastest write, largest disk. |
| `Deflate` (default) | Core | BCL `DeflateStream`. Good ratio, no native deps. |
| `Brotli` | Core | BCL `BrotliStream`. Better ratio, slower writes. |
| `Lz4` | `Rowles.LeanLucene.Compression.LZ4` | Very fast, modest ratio. |
| `Snappy` | `Rowles.LeanLucene.Compression.Snappy` | Similar speed to LZ4. |
| `Zstandard` | `Rowles.LeanLucene.Compression.Zstandard` | Better ratio than LZ4, still fast. |

The policy is recorded in the segment header, so reads tolerate mixed segments.

## Optional codec packages

Install and register a codec package to use LZ4, Snappy, or Zstandard:

```csharp
// In standard .NET the module initialiser registers the codec automatically.
// In Native AOT, call Register() explicitly at startup.
Lz4Compression.Register();
SnappyCompression.Register();
ZstandardCompression.Register();
```

## Block size

`StoredFieldBlockSize` (default `16`) controls how many documents share a
compression block. Larger blocks compress better but cost more on single-document
retrieval.

## Trade-offs

- Indexing throughput: `None` > `Lz4` ≈ `Snappy` > `Deflate` > `Zstandard` > `Brotli`.
- On-disk size: `Brotli` ≈ `Zstandard` < `Deflate` < `Lz4` ≈ `Snappy` < `None`.
- Retrieval cost scales with block size, not policy.

## See also

- <xref:Rowles.LeanLucene.Codecs.StoredFields.FieldCompressionPolicy>
- <xref:Rowles.LeanLucene.Index.Indexer.IndexWriterConfig>
