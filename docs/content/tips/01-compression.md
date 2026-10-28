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

Use LeanCorpus 4.0.0 with optional compression packages 2.0.0. Each optional
package requires Core `>= 4.0.0` and `< 5.0.0`.

```bash
dotnet add package LeanCorpus --version 4.0.0
# Install the codec you need:
dotnet add package LeanCorpus.Compression.LZ4 --version 2.0.0
dotnet add package LeanCorpus.Compression.Snappy --version 2.0.0
dotnet add package LeanCorpus.Compression.Zstandard --version 2.0.0
```

Register before opening an index:

```csharp
Lz4Compression.Register();
SnappyCompression.Register();
ZstandardCompression.Register();
```

In standard .NET the module initialiser registers automatically. In Native AOT, call `Register()` explicitly at startup.

## Block size

`StoredFieldBlockSize` (default `16`) is the maximum number of documents in a compression block. The writer also targets 1 MiB of raw stored-field data and flushes before the next document would exceed that target. A single larger document gets its own block, up to the 256 MiB raw/decompressed hard limit. Current stored-fields v5 has a separate 320 MiB encoded limit, allowing a legal raw block to expand during compression without a raw-to-encoded ratio restriction. Historical v1-v4 files retain their 256 MiB encoded limit. These are absolute byte ceilings; output above the encoded ceiling is rejected before it is written or read.

The stored-fields format remains v5 with unchanged body layouts. Existing 3.1.1 stored-fields v3 files still migrate to v5 through the coordinated `.fdt`/`.fdx` rewrite; this limit correction introduces no additional migration.

Retrieval cost scales with the raw bytes in the selected block. Current readers understand the byte-bounded v4 and v5 layouts and continue to read stored-fields versions 1 through 4. Builds predating v4 reject v4 segments, and v4 readers reject v5 segments rather than mis-mapping documents.

## Trade-offs

- Write speed: `None` > `Lz4` ≈ `Snappy` > `Deflate` > `Zstandard` > `Brotli`
- Disk size: `Brotli` ≈ `Zstandard` < `Deflate` < `Lz4` ≈ `Snappy` < `None`
- Retrieval cost scales with block bytes and the document-count maximum

## See also

- <xref:Rowles.LeanCorpus.Codecs.StoredFields.FieldCompressionPolicy>
