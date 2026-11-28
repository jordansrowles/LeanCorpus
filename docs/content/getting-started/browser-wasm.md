# Browser WebAssembly

The 5.0 development sources can build and query a volatile index inside a Blazor WebAssembly application. Use the normal `LeanCorpus` reference and `MMapDirectory`; .NET's browser runtime provides the memory-backed virtual filesystem used by the engine. There is no separate browser index format or search implementation. This feature is not included in the released 4.0 package.

This support is for single-threaded `browser-wasm` on .NET 10 and .NET 11. It does not provide IndexedDB, OPFS, cross-tab sharing, WASI, or persistence across a page reload. A successful commit publishes a generation within the current runtime. It cannot guarantee recovery after closing or reloading the page. See Microsoft's [WebAssembly state-management guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/webassembly?view=aspnetcore-10.0).

## First index

```csharp
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

using var directory = new MMapDirectory("/search/catalogue");
using var writer = new IndexWriter(directory, new IndexWriterConfig());
var document = new LeanDocument();
document.Add(new StringField("id", "one"));
document.Add(new TextField("body", "Search this browser catalogue"));
await writer.AddDocumentAsync(document);
await writer.CommitAsync();
using var searcher = new IndexSearcher(directory);
var results = searcher.Search(new TermQuery("body", "browser"), 10);
```

Dispose searchers, their leases and the writer before the directory. Keep the directory and writer in an application-owned service when multiple components share the index. Do not rebuild the index on every render. The [maintained browser example](../examples/browser-wasm.md) demonstrates a component-owned, small catalogue.

## Execution and cancellation

Indexing, physical flushes, merges and queries execute serially on the calling browser thread. Async ingestion supports single documents, ordered lists, concurrent-batch APIs, child-parent blocks and asynchronous streams. The concurrent APIs use serial admission in a browser. `CommitAsync` completes the same synchronous commit work inline. These APIs do not move CPU work off the UI thread.

Cancellation prevents admission of the next command or stream batch. An accepted command finishes; cancellation does not roll it back. Ordered oversized lists retain their normal chunking and partial-success semantics. Await asynchronous sources and yield between application batches to keep the UI responsive. Large merges and individual large documents remain synchronous. Dedicated worker hosting is an application integration requiring separate qualification.

`Dispose` cannot synchronously wait for a suspended source, retained mapping or active operation that needs the same event loop. It throws `InvalidOperationException`, closes admission and retains owned resources. Finish or cancel and await the operation, release child leases, then retry disposal. A failed close is not successful cleanup. Normal close with no outstanding work remains synchronous and idempotent.

## Defaults and capability errors

| Setting | Browser | Desktop |
| --- | ---: | ---: |
| `DurableCommits` | false | true |
| `IndexingConcurrency` | 1 | 0, automatic |
| `RamBufferSizeMB` | 16 | 512 |
| `RamPerThreadHardLimitMB` | 8 | 256 |
| `MaxBufferedDocs` | 1,000 | 10,000 |
| `MaxQueuedDocs` | 2,000 | 20,000 |
| `MaxQueuedBytes` | 16 MiB | 512 MiB |
| `MaxCachedSegmentReaders` | 32 | 256 |
| `MaxCachedSegmentReaderBytes` | 16 MiB | 256 MiB |

These are buffering and cache thresholds, not a total heap limit. Virtual files, mappings, active readers, vectors, merge output, result arrays and caller documents consume additional memory. Size the corpus and batches for the devices you support. Cache eviction cannot reclaim a reader while its operation or cursor retains it.

Global defaults still apply, followed by explicit configuration. Browser writers reject `DurableCommits = true`, producer concurrency greater than one, and flush or merge concurrency other than one with `PlatformNotSupportedException`. Zero producer concurrency resolves to one. Mutating an existing writer configuration does not bypass these checks. Direct native durability requests also fail explicitly.

Browser searcher construction rejects `ParallelSearch = true`; later mutation cannot enable parallel segment execution. The background `SlowQueryLog` is unsupported in a browser; use synchronous application diagnostics, metrics or activities instead. Ordinary metrics, searcher refresh and retained generation leases remain available. Built-in `None` and `Deflate` compression are supported. Brotli is outside the browser support contract: the interpreted runtime rejects it, and writers reject the policy before ownership in both deployment modes. Transcode imported Brotli fields on a desktop before loading them into the browser. Optional compression packages and third-party analysers require their own browser qualification.

## Deployment and verification

Use a normal Blazor WebAssembly publish and static host configured for the framework's WASM assets. The browser suite publishes its client with trimming and runs the published output, rather than substituting a desktop process. Browser AOT requires the matching `wasm-tools` workload and a separate publish-and-run result. A trimmed interpreted result does not prove WASM AOT.

From a bootstrapped checkout:

```bash
./devops test browser --framework net10.0
./devops test browser --framework net11.0
LEANCORPUS_BROWSER_ENGINE=firefox ./devops test browser --framework net10.0
# Requires wasm-tools and wasm-tools-net10 in the selected SDK:
LEANCORPUS_BROWSER_AOT=true ./devops test browser --framework net10.0
```

The explicit `browser` suite requires the pinned Playwright browsers and is excluded from `test all`. Set `LEANCORPUS_BROWSER_AOT=true` to publish with actual native AOT compilation into a separate `aot` site directory and exercise the same contracts. CI covers both compilation modes. Use `LEANCORPUS_BROWSER_SITE` only to test an alternative published client, such as a WASM AOT output. It must target the same framework as the test host. Both engine exceptions and external timeouts fail the test.

Formats remain shared with desktop. Loading an existing index into the virtual filesystem is an application responsibility; use trusted, compatible files and retain complete commit generations. No public archive import/export or persistent-storage adapter is introduced here.

## Shipping a prebuilt index

Build the index on a desktop or in CI, then serve its files as static assets. The browser downloads them into the virtual filesystem and opens a read-only `IndexSearcher`; no `IndexWriter`, flush or merge runs in the page.

1. Build with `UseCompoundFile = true` (or loose files, see below), `Deflate` or `None` stored-field compression, then `ForceMerge(1)` and commit. Index only the fields the browser queries.
2. Publish every file in the index directory except `write.lock`, under a content-hashed path with long-lived caching, together with a manifest listing each file's name, length and SHA-256, the LeanCorpus version and the analysers used.
3. In the browser, stream each file from `HttpClient` into a `FileStream` under one directory, verify its length and hash, then open `new IndexSearcher(new MMapDirectory(path))`.

```csharp
foreach (var file in manifest.Files)
{
    using var response = await http.GetAsync($"index/{hash}/{file.Name}", HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();
    await using var body = await response.Content.ReadAsStreamAsync();
    await using var output = File.Create(Path.Combine(path, file.Name));
    await body.CopyToAsync(output);
}
using var searcher = new IndexSearcher(new MMapDirectory(path));
```

Let the web server apply HTTP compression; do not ship an archive that the page must extract. The HTTP cache avoids repeat downloads, but each page load copies the payload into memory again. A payload must be built by a LeanCorpus version whose formats the browser client reads, with the same analysers. Treat payloads as trusted application assets: validation detects corruption, not a hostile index.

## Measured limits

The browser suite's probes record these figures for a single-segment, 40,000-document payload of about 31 MiB (64-dimensional vectors, Deflate stored fields). They are local Linux Chromium and Firefox results for orientation, not guarantees:

| Measurement | Compound | Loose |
| --- | ---: | ---: |
| WebAssembly memory growth after download, open, queries and a second searcher | 4.0× payload | 2.5–3.2× payload |
| Searcher open | about 0.5 s | about 0.5 s |
| First query set / repeated query set (10 queries) | 0.65 s / 0.11 s | 0.67 s / 0.11 s |

- `MMapDirectory` mappings are copies in a browser. Opening a searcher validates the committed segments; with compound files this copies the whole `.cfs` into WebAssembly memory, while loose files copy only the components that queries read. Each additional searcher over the same files holds its own copies, so share one searcher or `SearcherManager`.
- WebAssembly memory has a 2 GiB ceiling and never shrinks after growth. Memory released by disposed searchers is reused by the runtime but is not returned to the browser.
- At the ceiling, opening a searcher currently reports `InvalidDataException` ("index is corrupt") rather than an out-of-memory error. In the probe, the runtime stayed usable after the searchers were disposed, and the read-only index reopened with identical results.
- Disabling browser response streaming buffers each file in managed memory and raised growth to 5.2× in the same probe.
- Browser-side indexing of 5,000 small documents took about 6.5 s in Chromium and 11.5 s in Firefox (interpreted). A single `AddDocumentAsync` that triggers a flush blocked the page for up to 1.8 s, and `ForceMerge(1)` for 4 to 7 s.

## Ownership and unsupported operations

- Writer exclusion applies within one browser runtime: a second `IndexWriter` on the same path fails with `WriteLockException`. There is no cross-tab or persistent locking.
- Backup, restore and codec migration depend on durable filesystem barriers. In a browser they throw `PlatformNotSupportedException` before writing any output.
- A payload with Brotli stored fields can be searched, but reading its stored fields throws `PlatformNotSupportedException`.
- The default Chinese and Japanese analysers locate their lexicons on disk beside the application. A browser has none: Chinese analysis silently falls back to a small built-in word list under the same analysis identity, so a desktop-built Chinese index can be queried with different tokens, and the default Japanese analyser throws `FileNotFoundException`. Load the lexicon into the browser yourself (`ChineseLexiconTokeniser.FromStream`, `AnalyserFactory.CreateOwnedJapaneseAnalyser`) or avoid these analysers in browser queries. Other built-in languages produce the same tokens as desktop.

The execution and storage contract is recorded in [ADR053](../articles/ADRs/ADR053-browser-wasm-execution-and-volatile-storage.md). Platform support does not imply a throughput, mobile-memory or persistence guarantee.
