# LeanCorpus.Compression.LZ4

[![NuGet](https://img.shields.io/nuget/v/LeanCorpus.Compression.LZ4?label=LeanCorpus.Compression.LZ4)](https://www.nuget.org/packages/LeanCorpus.Compression.LZ4/)

Use this package when stored-field write and retrieval speed matter more than achieving the smallest possible index.

## Enable LZ4 compression

Install the core and codec packages:

```bash
dotnet add package LeanCorpus
dotnet add package LeanCorpus.Compression.LZ4
```

Register the codec and select its policy:

```csharp
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Compression.LZ4;
using Rowles.LeanCorpus.Index.Indexer;

Lz4Compression.Register();

var config = new IndexWriterConfig
{
    CompressionPolicy = FieldCompressionPolicy.Lz4
};
```

Use `config` when constructing the `IndexWriter`. The selected policy is recorded in each segment, so readers can open an index containing segments written with different policies.

> [!NOTE]
> Normal .NET applications also receive automatic module-initialiser registration. Calling `Register()` explicitly is harmless and makes startup intent visible.

> [!IMPORTANT]
> Native AOT applications should call `Lz4Compression.Register()` explicitly before opening or writing an index that uses LZ4. Confirm that all package assets required by the deployment target are published.

## Choose LZ4 when

- low compression latency is important;
- stored fields are read frequently;
- a modest compression ratio is acceptable.

The core package already includes Deflate and Brotli. See [stored-field compression](../../../docs/tips/01-compression.md) for policy trade-offs and block-size guidance.
