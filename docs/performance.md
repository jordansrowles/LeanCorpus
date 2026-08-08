# Performance

Benchmark data from the most recent full run on Debian 13, .NET 10.0.3, a 100k-document corpus and an Intel Xeon E3-1220. All numbers are BenchmarkDotNet single-operation measurements, not service throughput or tail-latency measurements. Compare only workloads with the same corpus, configuration, runtime and host; full artefacts record those inputs.

## Indexing

| Engine | 100k documents | Allocation |
|---|---|---|
| LeanCorpus | 7.4 s | 1.0 GB |
| Lucene.Net 4.8 | 13.6 s | 2.3 GB |

LeanCorpus indexes the corpus 1.8x faster with 2.3x less allocation. Both engines used default analysers and merge policies.

## Query latency

Selected query types on the 100k-document index. Numbers are mean execution time per operation.

| Query | LeanCorpus | Lucene.Net 4.8 | LC advantage |
|---|---|---|---|
| `TermQuery` | 104 us, 0.9 KB | 150 us, 50 KB | 1.4x faster, 56x less alloc |
| `BooleanQuery` (Must) | 470 us, 14 KB | 564 us, 118 KB | 1.2x faster, 8x less alloc |
| `FuzzyQuery` (edit 2) | 469 us, 7 KB | 2.2 ms, 1,247 KB | 4.7x faster, 186x less alloc |
| `GeoDistanceQuery` | 96 us, 59 KB | 2.2 ms, 148 KB | 23x faster, 2.5x less alloc |
| `Highlighting` (5 terms) | 176 us, 93 KB | 4.2 ms, 5,390 KB | 24x faster, 58x less alloc |
| `SearchWithStats` | 425 us, 220 KB | 10.1 ms, 4,018 KB | 24x faster, 18x less alloc |

LeanCorpus is consistently faster on every query type measured, with dramatically lower allocation. The gap is largest on analysis-heavy operations (highlighting, fuzzy matching, geo-distance) where the zero-allocation token pipeline and SWAR-accelerated edit-distance algorithms compound.

Lucene.Net edges ahead on `PhraseQuery` (448 us vs 829 us) and `RegexpQuery` (30.3 ms vs 38.2 ms), though in both cases LeanCorpus allocates 5x to 26x less memory.

## Allocation profiles

LeanCorpus's allocation advantage comes from several architectural decisions:

- **Ref-struct analysis pipeline.** `SpanToken` is a value type. No `Token` objects are allocated per analysed term.
- **Value-type postings enumerators.** `PostingsEnum` is a `ref struct` that operates directly on memory-mapped buffers.
- **`SkipLocalsInit`.** Assembly-wide suppression of `.locals init` avoids zeroing stack allocations in hot paths.
- **Bounded caches.** Segment reader caches use lease-based eviction rather than unbounded retention.

The practical effect: a `TermQuery` allocates 928 bytes in LeanCorpus vs 51,200 bytes in Lucene.Net. A high-throughput service issuing thousands of queries per second sees the difference in GC pressure and tail latency.

## Where the time goes

On a 100k-document `TermQuery`:

1. **FST lookup** (term dictionary): locates the postings offset for `field\0term`. Single-digit microseconds.
2. **Postings decode** (block-packed): decodes a block of 128 doc IDs and frequencies. Tens of microseconds.
3. **Scoring loop** (BM25): iterates decoded doc IDs, computes BM25 per hit, applies field boosts. Dominates query time.
4. **Top-N collect** (priority queue): maintains the top 10 hits. Negligible at typical N.

For multi-term queries (Boolean, Phrase), the postings decode and scoring loop are repeated per term. Block-Max WAND scoring on `BooleanQuery` skips entire postings blocks that cannot make the top-N, making multi-term queries sublinear in corpus size.

## Full benchmark data

Complete per-suite breakdowns with per-method statistics are available in the [benchmarks section](benchmarks/index.md). Each run records the commit hash, runtime version, corpus fingerprint, and raw BenchmarkDotNet output for reproducibility.
