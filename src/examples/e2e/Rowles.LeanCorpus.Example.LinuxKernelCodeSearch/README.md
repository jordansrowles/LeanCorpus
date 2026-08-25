# Linux kernel code search

This end-to-end workload indexes Linux kernel source one line per document and measures searcher open time, working set and representative query latency. It was created for [issue #42](https://github.com/jordansrowles/LeanCorpus/issues/42).

> [!WARNING]
> The full workload clones about 1.5 GB of source and can create tens of millions of documents across thousands of segments. Start with the bounded smoke run.

## Prerequisites

- A Release-capable .NET SDK supported by the repository.
- A local Linux kernel source checkout.
- A dedicated index path with sufficient free space.
- A separate output path for telemetry JSON.

Clone Linux `v6.6` LTS:

```bash
git clone --depth 1 --branch v6.6 \
  https://git.kernel.org/pub/scm/linux/kernel/git/torvalds/linux.git
```

## Run a bounded smoke workload

Build the example:

```bash
dotnet build --configuration Release \
  src/examples/e2e/Rowles.LeanCorpus.Example.LinuxKernelCodeSearch
```

Index a bounded sample without compaction:

```bash
dotnet run --configuration Release \
  --project src/examples/e2e/Rowles.LeanCorpus.Example.LinuxKernelCodeSearch \
  -- \
  --source /path/to/linux \
  --index /path/to/kernel-index-smoke \
  --output /path/to/kernel-output-smoke \
  --max-docs 100000 \
  --no-compact
```

Expected result:

- indexing progress is printed;
- the final segment count is reported;
- search scenarios run;
- a metrics JSON file is written under the output path.

Use this run to confirm paths, permissions and telemetry before removing the document bound.

## Reproduce the high-segment-count issue

1. Choose fresh, dedicated index and output paths.
2. Run the full corpus with `--no-compact`.
3. Record the final document and segment counts.
4. Stop the process.
5. Open the preserved index in a fresh Release process with `--skip-index`.
6. Repeat with the default cache.
7. Repeat with `--max-cached-segment-readers` at least equal to the segment count.
8. Compare open time, working set, cold query and warm query measurements.

Full indexing:

```bash
dotnet run --configuration Release \
  --project src/examples/e2e/Rowles.LeanCorpus.Example.LinuxKernelCodeSearch \
  -- \
  --source /path/to/linux \
  --index /path/to/kernel-index \
  --output /path/to/kernel-output \
  --no-compact \
  --skip-search
```

Fresh-process search:

```bash
dotnet run --configuration Release \
  --project src/examples/e2e/Rowles.LeanCorpus.Example.LinuxKernelCodeSearch \
  -- \
  --index /path/to/kernel-index \
  --output /path/to/kernel-output \
  --skip-index
```

> [!IMPORTANT]
> Compare runs only when corpus, index, segment count, commit, framework, configuration and host state are equivalent.

## Workload design

The `v6.6` corpus contains roughly 70,000 C and header files and about 30 million lines. Each source line becomes one document.

| Field | Type | Stored | Indexed |
| --- | --- | --- | --- |
| `path_id` | String | Yes | Docs only |
| `line` | Stored integer | Yes | No |
| `content` | Text | Yes | Docs, frequencies and positions |

`WhitespaceAnalyser` is used for text. `NoMergePolicy` and `MaxBufferedDocs = 10,000` deliberately create thousands of unmerged segments on a full run.

## Useful options

| Flag | Default | Use |
| --- | --- | --- |
| `--source <path>` | `./linux` | Kernel checkout |
| `--index <path>` | `./kernel-index` | Persistent index directory |
| `--output <path>` | `./output` | Telemetry output |
| `--max-docs <n>` | `0`, all | Bound a smoke run |
| `--max-cached-segment-readers <n>` | `256` | Control retained heavy readers |
| `--scenario <name>` | All | Run one query scenario |
| `--warmup <n>` | `10` | Warm-up iterations |
| `--measured <n>` | `50` | Measured iterations |
| `--no-compact` | False | Preserve high segment count |
| `--skip-index` | False | Search an existing index |
| `--skip-search` | False | Build the index only |

## Query scenarios

| Scenario | Query shape |
| --- | --- |
| `term-symbol` | Exact term |
| `phrase-symbol` | Phrase |
| `wildcard-callsite` | Wildcard |
| `fuzzy-typo` | Fuzzy term |
| `regex-grep` | Regular expression |
| `boolean-filter` | Filtered Boolean query |
| `stored-retrieval` | Stored fields for top match-all hits |

The query-result cache is disabled so repeated measurements exercise segment-reader caching.

## Read the metrics

The JSON output records:

- indexing, commit and searcher-open time;
- indexed document and final segment counts;
- index size and process working set;
- configured segment-reader cache capacity;
- first-query, p50 and p99 latency;
- hit counts and working set after cold and warm passes.

Keep the raw JSON with the source commit and run command. A Markdown summary is not a substitute for the evidence.

## Historical measured comparison

The existing acceptance measurements used the same 27-million-document, 2,700-segment index and fresh Release `--skip-index` processes.

| Reader implementation | Segments | Cache | Open time | Working set after open |
| --- | ---: | ---: | ---: | ---: |
| Eager, preliminary | 555 | Not applicable | 3.440 s | 643.6 MiB |
| Eager baseline | 2,700 | Not applicable | 77.888 s | 2,905 MiB |
| Lazy readers | 2,700 | 256 | 0.757 s | 75.8 MiB |
| Lazy readers | 2,700 | 2,700 | 0.806 s | 74.0 MiB |

With cache capacity covering every segment, the term scenario recorded a 2.038 s cold query and 5.811 ms p50. The phrase scenario recorded a 2.201 s cold query and 27.386 ms p50.

The narrow term p50 did not meet the five per cent parity target against the 3.39 ms eager baseline, so that acceptance target remained open. Full-index compaction also remained unmeasured because both compared builds reached the CodecKit scratch-buffer limit.

## After the run

The source checkout, index and telemetry paths are independent. Confirm that you no longer need the index or raw evidence before removing those directories.
