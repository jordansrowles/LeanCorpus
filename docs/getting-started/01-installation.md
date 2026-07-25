# Installation and first index

## Install

```bash
dotnet add package LeanCorpus
```

Targets `net10.0` and `net11.0`.

## Index two documents

```csharp
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

using var dir = new MMapDirectory("./index");
using var writer = new IndexWriter(dir, new IndexWriterConfig());

var doc1 = new LeanDocument();
doc1.Add(new TextField("title", "The quick brown fox"));
doc1.Add(new StringField("id", "1"));
writer.AddDocument(doc1);

var doc2 = new LeanDocument();
doc2.Add(new TextField("title", "Lazy dogs and slow cats"));
doc2.Add(new StringField("id", "2"));
writer.AddDocument(doc2);

writer.Commit();
```

## Search

```csharp
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;

using var searcher = new IndexSearcher(dir);
var hits = searcher.Search(new TermQuery("title", "fox"), topN: 10);

foreach (var hit in hits.ScoreDocs)
    Console.WriteLine($"docId={hit.DocId} score={hit.Score}");
```

## What is on disk

The directory holds segment files (`*.seg`, `*.dic`, `*.pos`, `*.fdt`, `*.fdx`, `*.nrm`, etc.) and one or more `segments_N` commit files. See the [architecture overview](../architecture.md) for details.

## Native AOT

`LeanCorpus` is marked AOT-compatible for `net10.0` and `net11.0`. The core library avoids reflection-based JSON metadata and is validated by a dedicated xUnit smoke test (run with `./devops aot`) rather than the ASP.NET JSON API example.

The smoke test publishes `src/examples/Rowles.LeanCorpus.Example.NativeAot` for `win-x64` with `PublishAot=true`, then runs the native executable through several code paths that have previously been problematic under AOT.

> [!NOTE]
> If you use one of the optional compression libraries with Native AOT, call their `Register()` methods at startup:
> ```csharp
> Lz4Compression.Register();
> SnappyCompression.Register();
> ZstandardCompression.Register();
> ```
> In standard .NET the module initialiser registers them automatically, but AOT may trim the initialiser.

> [!IMPORTANT]
> LeanCorpus does not support Blazor WASM. The segment-centric design requires a filesystem for memory-mapped I/O. Blazor Server and Blazor Hybrid are supported, as long as indexing happens server-side.

## See also

- [Architecture overview](../architecture.md)
- [Why LeanCorpus?](../why-leancorpus.md)
- [Field types](02-fields.md)
