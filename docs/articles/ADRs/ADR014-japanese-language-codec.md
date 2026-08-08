---
adr: ADR014
title: Japanese dictionaries use a LeanCorpus language codec
date: 2026-07-27
status: Accepted
version-added: vNext
summary: Use a checksummed LeanCorpus language codec for Japanese dictionaries.
areas: [analysis, codecs, storage]
---

# ADR014: Japanese dictionaries use a LeanCorpus language codec

- **Date:** 2026-07-27
- **Status:** Accepted

## Context

Lucene.NET Kuromoji distributes its Japanese dictionary across eight
CodecUtil and FST files. Reading those files directly would bring Lucene's
codec and FST formats into the runtime analyser pipeline. Shipping the files
unchanged would also retain headers, lookup structures and linguistic data
that LeanCorpus does not use when producing its current token surface,
offset and type.

The existing LeanCorpus FST maps UTF-8 byte keys to integer outputs. Kuromoji
needs the same mapping from a surface form to a source ordinal, but it walks
every accepting prefix from each input position rather than performing one
exact lookup.

## Decision

Japanese dictionary data is converted offline into one versioned `.jlc`
Japanese Language Codec file. The file has a fixed table of contents and
independently checksummed sections for:

- a LeanCorpus FST mapping UTF-8 surface forms to source ordinals;
- known and unknown word target maps and compact cost records;
- character categories with invoke and grouping flags;
- a flat connection-cost matrix.

The runtime does not read Lucene codec or FST formats. It memory-maps the
`.jlc`, validates its table before exposing sections, and copies only the
LeanCorpus FST into its existing reader representation.

`FstReader` gains an additive value-type prefix cursor. Existing exact,
prefix, wildcard and fuzzy operations and the `FST1` serialised format are
unchanged.

Japanese tokenisation uses a least-cost Viterbi path over known dictionary
matches and character-class unknown words. Per-call lattice storage is
thread-local and reused. Applications that do not construct a Japanese
analyser do not open or load the codec.

## Rationale

Reusing the current FST avoids maintaining a second automaton implementation.
UTF-8 requires more arcs than a UTF-16 Japanese-specific FST for common BMP
characters, but that cost is isolated to Japanese analysis and can be
measured before another format is introduced.

Converting once keeps Lucene format compatibility and its dependencies out
of the Native AOT runtime. Compact cost records remove unused dictionary
metadata, while a section table leaves room for later linguistic attributes
without changing unrelated sections.

A single checksummed file is simpler to download, validate and version than
eight independently versioned files. Memory mapping keeps large connection
and target tables out of managed allocations and allows concurrent readers
to share operating-system pages.

## Consequences

- Japanese dictionary downloads use the `.jlc` extension.
- Loose Kuromoji `.dat` files are conversion inputs, not runtime assets.
- The conversion output is tied to the documented `.jlc` version rather than
  Lucene's codec versions.
- The Japanese analyser pays dictionary mapping and Viterbi costs only when
  it is constructed and used.
- Existing search FST callers keep their current code paths and performance
  characteristics.
- A UTF-16 FST remains an option only if Japanese benchmarks show a material
  end-to-end benefit.
