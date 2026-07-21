# Linux Kernel Code Search

End-to-end example that indexes the Linux kernel source tree and runs search scenarios. Serves as a reproducible regression test for the high-segment-count `IndexSearcher` open-time issue (GitHub #42).

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
| `--no-compact` | `false` | Skip `Compact()` after indexing |
| `--skip-index` | `false` | Skip indexing, only search |
| `--skip-search` | `false` | Skip search, only index |

Indexing uses `NoMergePolicy` to maximise segment count. `MaxBufferedDocs` is 10,000. With 30M lines, this produces roughly 3,000 unmerged segments.

## Query scenarios

Each scenario runs 10 warmup iterations then 50 measured iterations. Results include p50/p99 latency and total hits.

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
  "searcher_open_ms": ...,
  "searcher_open_working_set_bytes": ...
}
```

## Reproducing the segment-count issue

1. Clone the Linux kernel source
2. Run with `--no-compact` to skip merging: `dotnet run -- --source /path/to/linux --no-compact`
3. Observe `searcher_open_ms` and `searcher_open_working_set_bytes` in the metrics output
4. Compare before/after the lazy-reader fix
