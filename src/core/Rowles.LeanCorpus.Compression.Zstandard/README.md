# LeanCorpus.Compression.Zstandard

[![NuGet](https://img.shields.io/nuget/v/LeanCorpus.Compression.Zstandard?label=LeanCorpus.Compression.Zstandard)](https://www.nuget.org/packages/LeanCorpus.Compression.Zstandard/)

Use this package when you want a stronger stored-field compression ratio while retaining good read performance.

## Enable Zstandard compression

Install the core and codec packages:

```bash
dotnet add package LeanCorpus
dotnet add package LeanCorpus.Compression.Zstandard
```

Register the codec and select its policy:

```csharp
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Compression.Zstandard;
using Rowles.LeanCorpus.Index.Indexer;

ZstandardCompression.Register();

var config = new IndexWriterConfig
{
    CompressionPolicy = FieldCompressionPolicy.Zstandard
};
```

Use `config` when constructing the `IndexWriter`. The policy is stored in the segment header, so readers can open indices containing segments written with different policies when every required codec is registered.

> [!NOTE]
> Normal .NET applications also receive automatic module-initialiser registration. Calling `Register()` explicitly is harmless and makes startup intent visible.

> [!IMPORTANT]
> Native AOT applications should call `ZstandardCompression.Register()` explicitly before opening or writing an index that uses Zstandard. Confirm that all package assets required by the deployment target are published.

## Choose Zstandard when

- index size matters more than minimum write latency;
- stored fields need a better ratio than LZ4 or Snappy;
- Brotli's write cost is too high for the workload.

The core package already includes Deflate and Brotli. See [stored-field compression](../../../docs/tips/01-compression.md) for policy trade-offs and block-size guidance.
