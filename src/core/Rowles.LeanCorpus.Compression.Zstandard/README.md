# LeanCorpus.Compression.Zstandard

[![NuGet](https://img.shields.io/nuget/v/LeanCorpus.Compression.Zstandard?label=LeanCorpus.Compression.Zstandard)](https://www.nuget.org/packages/LeanCorpus.Compression.Zstandard/)

Use this package when you want a stronger stored-field compression ratio while retaining good read performance.

> **Documentation preview:** This repository branch targets LeanCorpus 4.0.0 and `LeanCorpus.Compression.Zstandard` 2.0.0. These versions are unreleased; the NuGet badge shows the latest published package.

## Enable Zstandard compression

Install the core and codec packages:

```bash
dotnet add package LeanCorpus --version 4.0.0
dotnet add package LeanCorpus.Compression.Zstandard --version 2.0.0
```

Version 2.0.0 supports LeanCorpus `>= 4.0.0` and `< 5.0.0`.

Register the codec before opening an index and select its policy:

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

The core package already includes Deflate and Brotli. See [stored-field compression](../../../docs/content/tips/01-compression.md) for policy trade-offs and block-size guidance.
