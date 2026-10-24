# 4.0 benchmark qualification audit

This audit records inventory and package evidence for the `issue-97` benchmark work. It does not report measured performance.

## Source and environment

- Audit source revision: `940334da5e5132e4a2d8378e21e8eedc1771ca95` on branch `issue-97`.
- The audit was run with a dirty working tree containing the wider Issue 97 sprint changes. The SHA identifies the pinned base revision; it does not identify those uncommitted edits.
- Host: Debian GNU/Linux 13 (trixie), x64.
- SDK: .NET `11.0.100-preview.7.26381.103`; installed runtimes include .NET `10.0.3` and .NET `11.0.0-preview.7.26381.103`.
- Benchmark dependencies: BenchmarkDotNet and BenchmarkDotNet.Annotations `0.16.0-preview.2`; Lucene.NET comparator packages `4.8.0-beta00018`.
- Direct BDN consumers are Core, Rowles.Text, Compression and Profiling. The managed semantic inventory covers Core, Rowles.Text and Compression; Profiling is built separately and is not an operational comparison suite.

All four BDN consumer projects built for `net10.0` and `net11.0`. The managed DevOps suite passed 377 tests with 11 explicit skips during `setup.ps1`, before the final approximation-benchmark source adjustment. Deep configuration validation after that adjustment enumerated Core, Rowles.Text and Compression on both target frameworks and passed.

The dependency change is scoped to the Core, Rowles.Text, Compression and Profiling benchmark executables. BenchmarkDotNet is pinned to preview.2, Lucene.NET comparator packages are pinned to beta00018, and the four consumer lock files are recorded. No shipped Core, Rowles.Text or Compression package references, versions, APIs or source-inclusion rules changed.

## Discovered inventory

BDN `--list json` output was captured for each managed inventory project and target framework. Core listing used `all-with-explicit` without a document-count override so that the listing retained all parameter permutations. The raw files and `inventory-index.json` are under `artifacts/benchmark/qualification/s13a/final/`. The index records the six commands, exit codes, empty stderr files, assembly and output hashes, all 97 benchmark source files, and the combined source fingerprint `0542fe6bcc52105dbd860db8b7cf25301156444aff1a86b192b4aea8663c0327`; its own SHA-256 is `71d3748380534b7462bc9a62baec9792af3337adee47c6a37ee42a3ce78fc2e3`.

| Project | Discovered methods | BDN cases per framework | Semantic families | Family-selected cases | Explicit exclusions | Excluded cases per framework |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Core | 322 | 1,085 | 8 | 21 | 322 | 1,064 |
| Rowles.Text | 63 | 156 | 0 | 0 | 63 | 156 |
| Compression | 2 | 36 | 0 | 0 | 2 | 36 |
| **Total** | **387** | **1,277** | **8** | **21** | **387** | **1,256** |

Every discovered BDN case maps to exactly one semantic family or active exclusion. Core reflection also verifies that registered benchmark classes and BDN-discovered classes agree. The two omitted Core suites, `ApproximationBenchmarks` and `FlushTermCardinalityBenchmarks`, are now registered and selectable.

BDN preview validation found that `ApproximationBenchmarks` had to be unsealed and each benchmark method marked `NoInlining`. After that benchmark-only correction, Linux out-of-process `ShortRun` execution completed 10 cases on each framework: `net10.0` run `fd56175f-fda7-4add-bb43-da5be2bfc317` used .NET `10.0.3`; `net11.0` run `074f8329-a6e7-472a-9c9c-d0708ab6e559` used .NET `11.0.0-preview.7.26381.103`. Both used BenchmarkDotNet `0.16.0-preview.2`, `Concurrent Workstation` GC, `MemoryDiagnoser`, and the `ShortRun` job. A six-case net10 query `Dry` run (`a3ce8646-7c49-4fd9-ad1c-ee0bf335a80a`) also completed. These runs establish local child execution and recorded runtime/job identity only. PGO identity was not captured explicitly, Windows BDN execution remains open, and these runs do not qualify semantic correctness or timing comparisons.

All eight semantic families are classified `Unqualified`: two fuzzy no-hit engine cases, five approximation cases and one flush-cardinality case. The 387 exclusions expire on 31 January 2027 and state why their methods or parameter cases are withheld. Inventory is not evidence that the timed operation is correct or comparable.

## Historical claims and qualification boundary

The August 2026 benchmark figures and fuzzy no-hit ratio are historical diagnostics, not 4.0 results. The old no-hit path could reuse an empty expansion-cache entry, and the prior comparison did not establish matching cache policy, fuzzy parameters and per-case correctness. Retain those raw outputs as historical evidence, but do not publish their ratios as current claims.

The current [performance page](../performance.md) correctly reports that no qualified results are published. This inventory and its execution smokes do not change that status. BDN `--list json` only discovers cases; it does not run a benchmark child, prove effective PGO or full diagnostics identity, validate result quality, or qualify timing evidence. Windows qualification remains a separate gate. All eight semantic families remain `Unqualified`; all cases outside them remain excluded from competitive claims.
