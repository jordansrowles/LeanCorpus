# LeanCorpus.Compression.Snappy

[![NuGet](https://img.shields.io/nuget/v/LeanCorpus.Compression.Snappy?label=LeanCorpus.Compression.Snappy)](https://www.nuget.org/packages/LeanCorpus.Compression.Snappy/)

Use this package when you want fast stored-field compression with a speed and size profile similar to LZ4.

## Enable Snappy compression

Install the core and codec packages:

```bash
dotnet add package LeanCorpus
dotnet add package LeanCorpus.Compression.Snappy
```

Register the codec and select its policy:

```csharp
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Compression.Snappy;
using Rowles.LeanCorpus.Index.Indexer;

SnappyCompression.Register();

var config = new IndexWriterConfig
{
    CompressionPolicy = FieldCompressionPolicy.Snappy
};
```

Use `config` when constructing the `IndexWriter`. The policy is stored in the segment header, so mixed-policy indices remain readable when every required codec is registered.

> [!NOTE]
> Normal .NET applications also receive automatic module-initialiser registration. Calling `Register()` explicitly is harmless and makes startup intent visible.

> [!IMPORTANT]
> Native AOT applications should call `SnappyCompression.Register()` explicitly before opening or writing an index that uses Snappy. Confirm that all package assets required by the deployment target are published.

## Choose Snappy when

- compression and decompression latency are the main concern;
- interoperability with an existing Snappy-oriented deployment matters;
- a modest compression ratio is acceptable.

The core package already includes Deflate and Brotli. See [stored-field compression](../../../docs/tips/01-compression.md) for policy trade-offs and block-size guidance.
