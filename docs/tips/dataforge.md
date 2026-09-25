# DataForge datasets

DataForge has two explicit data paths. Synthetic profiles support repeatable development and CI. Imported references support release and deep investigation against a frozen external source. Benchmark commands use synthetic data by default.

## Generate and replay synthetic data

List the available profiles and defaults:

```powershell
./devops dataforge profiles
```

Generate a named dataset, then inspect, verify or reproduce it from its manifest:

```powershell
./devops dataforge generate -Profile leancorpus-search -Version 1 -Seed 42 -Count 20000
./devops dataforge inspect ./artifacts/dataforge/generated/leancorpus-search-v1-seed-42-count-20000
./devops dataforge verify ./artifacts/dataforge/generated/leancorpus-search-v1-seed-42-count-20000
./devops dataforge reproduce ./artifacts/dataforge/generated/leancorpus-search-v1-seed-42-count-20000
```

The manifest carries the profile, version, seed, parameters, record count and canonical content hash. Keep these fields with any benchmark result. Reproduction checks that the current profile generates the same identity and bytes.

## Build the frozen Wikipedia reference

The `leancorpus-wikipedia-en` v1 reference is based on the `enwiki` dump dated 2026-09-01. It is a fixed 20,000-record release and deep-investigation dataset. It is never fetched automatically by benchmark or build commands.

The source contract pins the main file:

| Field | Value |
|---|---|
| Filename | `enwiki-20260901-pages-articles-multistream.xml.bz2` |
| Expected size | 26,797,495,184 bytes |
| Official SHA-1 | `e0a53c30c3a3b444018df95704d3d101cd618440` |
| Companion index | `enwiki-20260901-pages-articles-multistream-index.txt.bz2` |

Download the pinned checksum/status files, primary dump and index with the DataForge Tool. Interrupted primary/index transfers keep a `.partial` file and resume from its byte length. The published SHA-1 is checked before either large file receives its final filename. A mismatch stops the operation and is not a reason to change the pin.

```powershell
./devops dataforge reference download
./devops dataforge reference build
./devops dataforge reference inspect
./devops dataforge reference verify
```

`build` requires the verified source files to be present locally and does not download them. The immutable reference is written under `artifacts/dataforge/reference/leancorpus-wikipedia-en-v1/`. `verify` checks its records, manifest and support files without the source dump or network access.

## Selection and text transformation

The builder reads the compressed multistream index in order, then ranks candidate page IDs by the pinned `page-id-sha256-v1` algorithm and salt `leancorpus-wikipedia-en-v1`. It keeps a bounded Top-K heap and increases the candidate limit until it has 20,000 eligible pages. Index offsets are range-checked; each selected bzip2 member is read through a bounded stream. XML parsing prohibits DTDs and external entity resolution.

Eligibility v1 accepts namespace-zero pages with a page ID, a current revision and text, and no redirect. It rejects malformed or oversized source text, normalised text outside 256 bytes to 2 MiB, and text with fewer than 40 whitespace-delimited tokens. Stable rejection reasons and the selection limit are recorded as build evidence.

The visible-text normaliser v1 removes bounded comments, references, tables and templates; applies deterministic link, tag, heading and emphasis rules; decodes HTML entities; and normalises whitespace. It does not fetch media or templates and does not render MediaWiki HTML. The result is a deterministic text approximation, not a rendered article.

Each canonical NDJSON record stores the page and revision IDs, revision timestamp, revision URL, raw-wikitext SHA-256, normalised-text SHA-256 and normalised text. The records stream determines `ContentSha256` and `ArtefactSha256`. Build timing, candidate counts, rejection counts and resource measurements live separately in `build-evidence.json` and do not change the dataset identity.

## Attribution, licence and withdrawal

The reference contains the [Creative Commons Attribution-ShareAlike 4.0 International legal text](https://creativecommons.org/licenses/by-sa/4.0/legalcode.en) and `LICENSES/attribution.txt`. Per-record revision attribution is the `SourceUrl` field in `records.ndjson`; it points to the exact English Wikipedia revision used. The transformation is named in the attribution file.

Once v1 is accepted, do not overwrite it with a later dump or a changed normaliser. A future refresh is `leancorpus-wikipedia-en` v2. If a selected revision later must not be redistributed, retain the historical v1 identity in records and benchmark evidence, withdraw public distribution of its bytes where required, and publish a cleaned v2 if a successor is needed. Never repair v1 by querying live Wikipedia during replay.

## Benchmark identity and comparability

Synthetic remains the normal benchmark dataset. Wikipedia must be selected with `-Dataset wikipedia`, and the runner verifies the reference before starting. Only suites listed by `./devops benchmark -List` for Wikipedia mode are accepted. Text-only merge and flush methods are selected explicitly; generated prices, categories, vectors and other structured fields are not added to the imported pages.

The benchmark sidecar labels the imported source kind, dataset ID/version, full record count and content hash with no seed. That identity and its data fingerprint keep Wikipedia results in a separate history from synthetic `leancorpus-search` results. Compare timings only when the complete dataset identity, benchmark method, workload parameters and runtime match.

## Release qualification

Run the full DataForge release qualification after building the frozen reference:

```powershell
./devops dataforge qualify
```

The command runs the solution build, DataForge tests on both supported frameworks, architecture and Server integration tests, and synthetic search/vector/hybrid and multilingual analysis benchmark smokes. It then runs fast Wikipedia indexing, term, phrase, prefix, wildcard and regexp smoke suites before the 20,000-record default Wikipedia indexing, search, highlighting and plain-text merge benchmarks. Wikipedia qualification requires a locally verified v1 artefact; it does not download the source dump or build the reference. Use `-ReferencePath` to select another local copy, or `-Dry` to print the command matrix.

The qualification summary is written under `artifacts/dataforge/qualification/`. It records the host and source commit, verified dataset identities, blockers and the run IDs of the ordinary build, test and benchmark artefacts. Benchmark results stay in their normal run directories.
