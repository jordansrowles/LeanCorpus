# ADR021: Package Analysis independently while preserving LeanCorpus source inclusion

- **Date:** 2026-07-31
- **Status:** Accepted

## Context

The Analysis pipeline is useful without the search engine, but LeanCorpus
already exposes these types and its indexing and search paths use them. A
separate package must not change the public namespaces or add a runtime
dependency from `Rowles.Text` to the search engine.

Japanese analysis is the exception to a simple source move. The LeanCorpus
implementation uses `IndexInput`, the existing FST reader and `Crc32`, while
those types belong to the search engine assembly. Chinese, Thai, Hunspell and
KStem also have file-loading factories that use LeanCorpus's retrying Store
I/O in the main assembly.

## Decision

`src/core/Rowles.Text/Analysis` is the canonical source location. The
`Rowles.Text` project compiles it with the existing
`Rowles.LeanCorpus.Analysis.*` namespaces and defines `ROWLES_TEXT`.

`Rowles.LeanCorpus` source-includes the same Analysis files and does not add a
project reference to `Rowles.Text`. Its build therefore keeps the existing
Store retry wrappers and Japanese `IndexInput`, FST and CRC32 paths.

The `ROWLES_TEXT` build uses BCL file access for standalone lexicon loading
and private Japanese FST and CRC32 implementations over the same `.jlc`
format. The public API and dictionary formats remain unchanged.

Applications choose either `LeanCorpus` or `Rowles.Text`. They must not
reference both packages because both intentionally define the same public
Analysis namespaces and types.

## Rationale

Source inclusion keeps the LeanCorpus hot path on the same implementation and
avoids a package dependency cycle or runtime indirection. Conditional CJK
support is limited to the internal storage boundary: the standalone package
gets no search engine types, while LeanCorpus retains its existing readers and
retry behaviour.

The package does not embed optional dictionary data. Japanese, Chinese, Thai
and KStem dictionary files remain application-supplied inputs.

## Consequences

- `Rowles.Text` targets the same supported frameworks and Native AOT contract.
- LeanCorpus retains the Analysis API without depending on the package.
- New Analysis code must compile in both projects and must not depend on
  `Search` or `Index` namespaces.
- Changes to the `.jlc` format or Japanese tokeniser behaviour must update
  both conditional paths and their standalone tests.
- Architecture tests enforce the source-inclusion bridge, project-reference
  boundary, assembly references and Analysis layering.
