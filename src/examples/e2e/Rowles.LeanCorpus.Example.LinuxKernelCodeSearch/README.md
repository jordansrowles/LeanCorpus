# Linux Kernel Code Search

End-to-end example that indexes the Linux kernel source tree and runs search scenarios. Serves as a reproducible regression test for the high-segment-count `IndexSearcher` open-time problem tracked by [GitHub issue #42](https://github.com/jordansrowles/LeanCorpus/issues/42).

## Corpus

Linux kernel `v6.6` LTS. Clone depth-1:

```
git clone --depth 1 --branch v6.6 https://git.kernel.org/pub/scm/linux/kernel/git/torvalds/linux.git
```

Roughly 70K `.c` and `.h` files, roughly 30M lines, roughly 1.5 GB raw source.

## Document model

One document per line of code:

| Field | Type | Stored | Indexed |
|---|---|---|---|
| `path_id` | String | Yes | Docs only |
| `line` | Stored (int) | Yes | No |
| `content` | Text | Yes | Yes (Docs, Freqs, Positions) |

`WhitespaceAnalyser` is used for all text fields. `path_id` is a numeric file identifier that resolves to a full path via the console output.

## Build

```
dotnet build
```

No solution entry needed: the example is standalone.

## Run

```
dotnet run -- [options]
```

| Flag | Default | Description |
|---|---|---|
| `--source <path>` | `./linux` | Path to Linux kernel clone |
| `--index <path>` | `./kernel-index` | Path for index directory |
| `--output <path>` | `./output` | Path for telemetry output |
| `--max-docs <n>` | `0` (all) | Max documents to index |
| `--max-cached-segment-readers <n>` | `256` | Maximum retained heavy segment readers |
| `--scenario <name>` | all | Run one query scenario |
| `--warmup <n>` | `10` | Warmup iterations per scenario |
| `--measured <n>` | `50` | Measured iterations per scenario |
| `--no-compact` | `false` | Skip `Compact()` after indexing |
| `--skip-index` | `false` | Skip indexing, only search |
| `--skip-search` | `false` | Skip search, only index |

Indexing uses `NoMergePolicy` to maximise segment count. `MaxBufferedDocs` is 10,000. With 30M lines, this produces roughly 3,000 unmerged segments.

## Query scenarios

Each scenario records an untimed-cache first query, then runs 10 warmup iterations and 50 measured iterations by default. Results include first-query latency, p50, p99, hit count, and working set after the cold and warm passes. The query-result cache is disabled so repeated measurements exercise segment-reader caching.

| Scenario | Query |
|---|---|
| term-symbol | `TermQuery("content", "task_struct")` |
| phrase-symbol | `PhraseQuery("content", "struct", "task_struct")` |
| wildcard-callsite | `WildcardQuery("content", "*kmalloc*")` |
| fuzzy-typo | `FuzzyQuery("content", "schedulr")` |
| regex-grep | `RegexpQuery("content", "BUG_ON.*")` |
| boolean-filter | `BooleanQuery(TermQuery("path_id", ...) + WildcardQuery("content", "*spin_lock*"))` |
| stored-retrieval | Read stored fields for top 100 `MatchAllDocsQuery` hits |

## Telemetry

After the run, a metrics JSON file is written to the output directory:

```json
{
  "index_time_ms": ...,
  "docs_indexed": ...,
  "final_segment_count": ...,
  "commit_time_ms": ...,
  "index_size_bytes": ...,
  "max_cached_segment_readers": 256,
  "working_set_before_open_bytes": ...,
  "searcher_open_ms": ...,
  "searcher_open_working_set_bytes": ...,
  "scenarios": [
    {
      "name": "term-symbol",
      "first_query_ms": ...,
      "p50_ms": ...,
      "p99_ms": ...,
      "total_hits": ...,
      "working_set_after_cold_bytes": ...,
      "working_set_after_warm_bytes": ...
    }
  ]
}
```

## Measured comparison

These Linux measurements use the same 27-million-document, 2,700-segment index and a fresh Release `--skip-index` process. The earlier 555-segment observation remains useful preliminary evidence, but is not the acceptance index.

| Reader implementation | Segments | Cache | Open time | Working set after open |
|---|---:|---:|---:|---:|
| Eager, preliminary | 555 | n/a | 3.440 s | 643.6 MiB |
| Eager baseline | 2,700 | n/a | 77.888 s | 2,905 MiB |
| Lazy readers | 2,700 | 256 | 0.757 s | 75.8 MiB |
| Lazy readers | 2,700 | 2,700 | 0.806 s | 74.0 MiB |

The default-capacity term scenario recorded a 1.581 s cold query and a 1.128 s subsequent broad reload pass, with 3,077 hits. This deliberately used one measured reload iteration because repeated broad passes churn a cache smaller than the segment count.

With capacity covering every segment, the term scenario recorded a 2.038 s cold query, 5.811 ms p50, 9.672 ms p99, and 3,077 hits. The phrase scenario recorded a 2.201 s cold query, 27.386 ms p50, 82.742 ms p99, and 1,308 hits. Its warm p50 was lower than the eager baseline of 32.51 ms. The narrow term p50 did not meet the five per cent parity target against the eager baseline of 3.39 ms, so that acceptance target remains open.

The reader cache bounds retained heavy state, not the memory touched by a query. A broad query over more segments than the configured capacity can repeatedly reload readers and increase latency. Set `MaxCachedSegmentReaders` to at least the active segment count when stable warm-query latency matters more than the additional retained memory.

Compaction timing could not be compared on this acceptance index. Both the reconstructed eager build and the lazy-reader build reached the existing CodecKit scratch-buffer limit while writing the merged term dictionary: 151,771,577 bytes requested against a 67,108,864-byte limit. Smaller-index merge and cleanup tests pass, but the ten per cent compaction criterion is therefore unmeasured here.

## Reproducing the segment-count issue

1. Clone the Linux kernel source
2. Run with `--no-compact` to skip merging: `dotnet run -- --source /path/to/linux --no-compact`
3. Stop the process, then open the preserved index in a fresh process with `--skip-index`
4. Observe the open, working-set, and scenario metrics
5. Repeat with the default cache and with `--max-cached-segment-readers` at least equal to the segment count
