# LeanCorpus DevOps: unreleased

## Added

- Add managed `test` and `aot` apphost commands backed by the shared test planner and executor; route the PowerShell compatibility dispatcher and CI through the managed executor.
- Run the Build workflow on `issue-97` pushes so the migration branch receives exact-source CI qualification.
- Establish DevOps `1.0.0` independently of the LeanCorpus Core package version.
- Add managed backup/restore matrix qualification with Git source fingerprints, five legacy variants and linked child-run evidence.
- Add managed documentation preparation, metadata, build, static/browser validation and serve commands through the DevOps apphost.
- Add a framework-dependent .NET 10 tool distribution profile for the managed DevOps command, with package readme and licence metadata.
- Add managed RID-specific single-file DevOps release archives with source manifests, third-party notices, payload checksums and archive SHA-256 sidecars.
- Add an interactive Spectre.Console workbench for managed tests, benchmark suites and area/group selectors, DataForge qualification and recent run inspection.
- Exercise managed DevOps bootstrap and `report all` on Linux ARM64 and macOS ARM64 in Build CI.
- Add a managed, bounded sustained-workload command with versioned input and terminal evidence for ingestion, read-only search and mixed ingest/search refresh scenarios.
- Verify exact committed document ID sets for concurrent and sequential indexing benchmark cases.
- Add a directional FST build benchmark for sorted UTF-8 terms with setup-time serialized-index and exact-output verification.
- Add exact survivor checks to queued and committed deletion benchmarks, and a directional ten-per-cent replacement/commit/refresh case with published-generation verification.
- Verify plain, HNSW and deletion-heavy merge outputs against exact stored-ID sets and live counts.
- Verify the exact committed document count and stored-ID set after each high-cardinality flush benchmark iteration.
- Verify wide stored-fields writes and bounded/concurrent reads against exact decoded values.
- Verify every numeric, sorted, multi-valued and binary DocValues benchmark output before timing.
- Verify Packed BKD selective XY range results against the deterministic input point set.
- Add an unqualified shape-index case family and verify origin containment before timing across the existing shape and vertex-count parameters.
- Add bounded semantic benchmark selectors for profile, case family, engine and workload; intersect them with suite, area, group and method-filter selection, enforce registered parameter combinations across Core, Text and Compression hosts, count those combinations against the budget, expose metadata through `--list` and `--dry`, and fail on empty or over-budget plans.
- Add a separate fail-closed comparison assessment for paired v5 rows. It rejects duplicate jobs, historical v4 reports, incomplete or failed rows, unverified preflight, mismatched source/dataset/host/runtime/GC/PGO, and low or noisy samples; ratios appear only for fully qualified pairs.
- Record terminal benchmark setup-preflight evidence in each Core v5 row so the separate comparator can distinguish a passing preflight from missing or failed evidence.
- Replace the monolithic benchmark workflow with managed plan, build, smoke, four sequential bounded shards, evidence collection and a qualification summary. Missing, cancelled or failed shards and incomplete report counts now fail closed; no exhaustive or stress strategy is dispatched by this workflow.
- Consolidate the 3.1.1 sparse-HNSW and migration fixture generators behind `data fixtures`, with shared source, exact package and assembly pins, isolated SDK/NuGet restore and immutable archive baselines; retain `data migration-fixtures-311` as a deprecated alias.

## Changed

- Move the complete solution inventory and 11 focused build filters under `src/`, group DevOps projects by purpose, validate tracked solution/filter membership in managed DevOps, and resolve solution entries relative to the relocated master.

- Share Build concurrency groups across same-branch pushes and pull requests to avoid duplicate runs.

- Route every `devops.ps1` invocation through the fingerprint-validated managed host, retire the legacy `benchmarks docs` and `setup` command aliases, and align contributor examples with managed docs, DataForge, test, AOT and package commands.
- Validate solution inventory against tracked project files present in the working tree so intentional project removals can be checked before staging.
- Validate Compression benchmark round trips before timing, retain exact input/output bytes and DataForge identity, and register directional codec cases with explicit size metrics.

- Run Build, platform, stress and diagnostics tests and AOT through the managed host; bootstrap platform smoke before invocation.
- Retain raw BenchmarkDotNet result files and record terminal, failed, intermediate, missing and partial case counts with no-output, timeout, cancellation and failed-pair execution classifications.

- Emit Core benchmark report schema v5 with stable BenchmarkDotNet case and result-row identity, preserve duplicate and intermediate rows, validate terminal statistics, and treat schema v4 as historical-only comparison evidence.
- Pin BenchmarkDotNet and its annotations to the NuGet.org preview, update Lucene.NET benchmark comparators, and lock their package graphs without the BDN MyGet feed.
- Reconcile every Core, Rowles.Text and Compression BDN case on .NET 10 and .NET 11 with a semantic family or expiring exclusion, and document the unqualified source-pinned inventory.
- Add separate `fuzzy-parity` and `fuzzy-cache` selectors while preserving `fuzzy` as a parity alias; record matched semantic parameters, compare exact expansion terms and logical document IDs, and preflight positive, near-miss, empty-expansion, mixed-cache and eviction cases.
- Report pinned suggester correction ground truth, actual ranked suggestions, accuracy@1, recall@5, MRR and coverage, and keep quality-mismatched timings directional.
- Replace unqualified historical benchmark comparisons with a clear no-results state and reproducibility guidance.
- Canonicalise imported guides into logical sections and preserve their former `/generated/` URLs with permanent redirects.
- Validate required DataForge profile IDs and versions before qualification begins, and restrict managed documentation manifests to owned regular-file output roots.
- Calculate Packed BKD output bytes per point after setup, and label new-reader traversal with its warmed page-cache state.
- Consolidate benchmark project paths, suite labels, aliases, dataset policy and text class selection in managed DevOps configuration; add light and manifest-backed validation.
- Check fuzzy benchmark expansion counts and no-hit results from both Core and Lucene.NET before measurement.
- Add a dictionary-verified uncached fuzzy-negative benchmark and a seeded mixed-cache hit-rate preflight.
- Register all 15 benchmark correctness defects with deterministic, fingerprinted fixtures and reject incomplete or altered audit manifests.
- Separate Gutenberg indexing comparisons into directional as-shipped and matched persistence profiles with explicit commit, close/reopen, and stored-ID verification.
- Give the benchmark English analyser independent per-thread instances so concurrent writer defaults can run the Gutenberg English workload.
- Run the Geo 50 km boundary fixture against both engines during benchmark setup and verify the included and excluded logical IDs before timing.
- Add numeric DocValues to the Lucene aggregation comparator and verify exact statistics across a deterministic two-segment fixture.
- Split MoreLikeThis benchmarking into cold extraction, prebuilt-query and explicit warm-cache paths; verify selected terms and logical IDs from a pinned fixture, and keep the differing DataForge term sets unqualified.
- Add raw IEEE-754 price DocValues to the Lucene function-score comparator and verify all four score modes and returned logical rankings across multiple segments, missing prices and negative values.
- Build the shared Lucene.NET standard benchmark index with explicit BM25 norms and query scoring; retain default Classic TF-IDF in its own as-shipped comparison group.
- Preserve external IDs and prices in the file-backed Lucene.NET pre-sorted index case, and classify it as directional rather than native index sorting.
- Collect all Lucene.NET collapse candidates and return the requested facet offset slice, with fixture checks for late groups, representatives and complete pages.
- Select reviewed Ubuntu 24.04 Docs browser baselines on a pinned Ubuntu 24.04 runner while retaining the strict 0.50% visual difference limit.
- Include all tracked C# projects in the managed code dependency graph, report solution and tracked project counts separately, and retain SDK-evaluated package metadata.
- Run CI product builds and the manual benchmark command through the checked-out managed DevOps apphost after setup.
- Bootstrap the checked-out managed DevOps host before package validation and consumer checks in Build CI.
- Upload linked MTP test-run artefacts separately from coverage-run manifests in Build CI.
- Use British spelling for Core serialisation source paths, namespaces, test selectors, affected-source selectors and benchmark area/group selectors.
- Keep repository-host, .NET tool and release-archive dependency graphs isolated with profile-specific NuGet lock files.
- Record managed test environment override names in run manifests without persisting their values.
- Fail setup early with the exact missing DevOps project or configuration path, before restore, build or publish, and verify that the previous apphost is preserved. Keep the optional root `Directory.Build.targets` out of the required-input list and correct the bootstrap smoke's command capture and solution-path assertions.
- Import managed DevOps release notes as an independent changelog stream and derive its displayed project version separately from Core and Server versions.
- Bootstrap the managed host before collecting hosted coverage and call its coverage command directly.
- Run `report all --json` through promoted Linux and Windows hosts in Build CI and retain the full report run evidence.
- Consolidate bootstrap, host fingerprint validation and argument forwarding in root `devops.ps1`; cover isolated failure, rollback, lease, manifest and dispatch cases in MTP integration tests, and update CI to use the explicit `bootstrap` verb.
- Add managed `benchmark qualification-summary` output with source identity, planned report counts, shard exit codes, deterministic schema-v1 JSON and fail-closed qualification results for the manual benchmark workflow.
- Resolve bootstrap checkout paths through filesystem links, fingerprint embedded consumer templates and source assets while excluding generated output, and preserve nested execution data in schema-v1 benchmark qualification summaries.

## Fixed

- Preserve UTC kind when validating managed bootstrap lease markers on Windows, and clear read-only checkout files during temporary-worktree test cleanup.
- Resolve release-archive documentation and licence inputs from the managed DevOps project catalog so distribution works from the current repository layout.
- Disable MSBuild node reuse for documentation version probes so Windows can remove temporary checkout directories after each child process exits.
- Use registered managed test and AOT option spellings in CI and report unrecognised managed arguments with test-runner separator guidance.
- Remove the retired PowerShell DevOps command application and shadow `.psd1` configuration; keep managed run, package, CI and bootstrap workflows on their current owners.
- Run locked bootstrap restores on one MSBuild node to prevent project-graph restore hangs.

- Read v5 benchmark rows when provenance is absent while leaving comparison qualification fail-closed.

- Reconcile bounded/default similarity and existing MoreLikeThis fixture cases with actual benchmark discovery while retaining unqualified comparison status.

- Give Unix release archives deterministic executable and data-file permissions when built on any host.

- Enable TRX evidence for real managed DevOps one-off test probes and distinguish historical benchmark inventory from current coverage.

- Respect MTP session exit codes and preserve failed, cancelled and timed-out test outcomes and raw coverage without launching HTML report generation.

- Preserve pinned benchmark correctness fixture bytes on Windows checkouts and make malformed UTF-16 and process heartbeat regression inputs deterministic.

- Record source, host, and dataset identity in benchmark runs; keep Core schema-v4 and August 2026 results historical and prevent unqualified reports from appearing as 4.0 comparisons.
- Make the approximation benchmark class and methods pass BenchmarkDotNet preview validation.
- Exclude maintainer-only documentation and historical Server evidence from the public site, and fail the docs build if those routes reappear.
- Emit TRX case evidence for one-off managed MTP test runs and distinguish empty discovery, failed assertions, failures before test execution and incomplete results.
- Exclude ignored build output from repository report dirty-state and status entries while retaining untracked source changes.
- Correct the Lucene.NET Geo distance benchmark radius units, disable its default spatial query tolerance for fixture parity, and use unique fuzzy no-hit terms to avoid cached-empty-result bias.
- Remove Packed BKD file-size metadata lookup from timed work and replace the unsupported cold-open claim with an explicit warm-page-cache reader profile.
- Build benchmark targets with one MSBuild node before launching the compiled app, disable compiler servers, and report the benchmark target, lease stage, elapsed time, and latest child output in progress updates.
- Keep five documentation categories visible at desktop widths, use compact navigation below 1200px, and stabilise Linux font metrics.
- Make the generated ADR relationship idempotence assertion recognise Windows line endings.
- Allow CI jobs to report bootstrap failures when no product-build diagnostics exist.
- Preserve managed coverage and documentation test run evidence when their CI jobs fail.
- Capture Chromium platform-font and viewport details with failed docs-site browser evidence.
- Give interactive workbench users a documented-command fallback when their terminal does not support ANSI selections.
- Handle Ctrl+C during interactive prompts as a graceful cancellation and restore terminal state.
- Point generated multi-machine benchmark charts at their machine-prefixed JSON results.
- Handle nullable generated-dataset version fields when validating benchmark identity so managed runs finish with terminal evidence.
- Remove fuzzy benchmark exclusions superseded by semantic case families so deep inventory validation remains one-to-one.
- Build similarity-specific Lucene.NET indexes for BM25, Classic TF-IDF, Dirichlet and Jelinek-Mercer, and validate writer/searcher profiles, norms, scores and rankings before timing.
- Compare HNSW and quantised vector results with exact cosine ground truth at K=1, 10 and 100; enforce the fixture-pinned 0.9 recall floor and record score, rank and quantisation errors before timing.
- Record accepted documents and indexed tokens for each token-budget mode, and limit baseline ratios to the same-work variants.
- Validate equivalent query ASTs at the parser guard limit and reject limit-plus-one input before timing, with valid parsing and rejection kept as separate benchmark operations.

## Removed

- Retire the JSON API, Newsgroups, telemetry and Rowles.Text tokenisation demo applications, the legacy news downloader and machine-specific nightly benchmark runner; keep the Linux kernel code-search workload and its manual reproduction guide.

## Deprecated

## Security
- Restrict production docs deployments to successful `main` Build artefacts for automatic and manual admission, while retaining pull-request rejection and exact run/artifact/digest checks.
