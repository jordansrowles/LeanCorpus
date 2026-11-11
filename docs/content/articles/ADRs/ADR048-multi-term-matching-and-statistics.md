---
adr: ADR048
title: "Multi-term matching and statistics"
date: 2026-11-11
status: Accepted
version-added: v4.0.0
summary: "RegexpQuery matches whole terms on every route, multi-term queries return each document once with scores summed across terms, and expansions score with global document frequency."
areas: [search, scoring]
relates-to: [ADR013, ADR038]
---

# ADR048: Multi-term matching and statistics

- **Date:** 2026-11-11
- **Status:** Accepted

## Context

A review of the multi-term query families on the 100,000-document benchmark
corpus found three result defects:

- **Inconsistent regexp matching.** `RegexpQuery` compiled its pattern with
  .NET search semantics, so `IsMatch` found the pattern anywhere inside a term,
  while the literal-prefix route assumed the pattern started at the term's
  first character. `gov.*ment` and `(gov).*ment` are the same expression but
  took different routes and returned different documents. On the corpus
  `gov.*ment` matched 20,598 documents where Lucene.NET, which matches whole
  terms, matched 18,946.
- **Duplicate regexp documents.** The regexp candidate route collected each
  posting of each matching term, so a document containing two matching terms
  appeared twice: `TotalHits` was 20,937 for 20,598 unique documents.
- **Segment-dependent expansion scoring.** Prefix, wildcard, regexp and fuzzy
  expansions, and span queries over them, scored each expanded term with the
  document frequency of the segment being searched, combined with the global
  document count. Ranking therefore changed when segments merged, with no
  change to the documents. Term, Boolean and phrase queries already used
  global document frequency.

## Decision

- **Whole-term regular expressions.** `RegexpQuery` matches the whole term on
  every route, like Lucene.NET. The pattern is compiled as `\A(?:pattern)\z`
  within the ADR038 compilation bounds, and the literal, prefix, contains and
  fallback routes all apply that one rule. `^gov.*ment$` and `gov.*ment` are
  the same query, and `ment` does not match `government`. Search-within
  behaviour needs explicit wildcards, such as `.*ment.*`.
- **One result per document.** A document matching several expanded terms
  appears once, with its scores summed across the terms, as prefix expansion
  already did. `TotalHits` counts unique documents. Expansion limits stay
  enforced.
- **Global document frequency.** Every expansion query scores each expanded
  term with its document frequency summed across all segments of the searcher,
  collected once per search alongside the expansion-limit check, and looked up
  once per term, never per posting.
- Prefix queries remain scored; they are not made constant-score.

## Rationale

Whole-term matching is what makes literal-prefix extraction valid, matches the
query type users know from Lucene, and gives every route the same answer.
Summing per document matches the prefix path and the meaning of a disjunction
over expanded terms. Global document frequency is already the contract for
term queries; extending it to expansions makes scores and order independent of
segmentation, which merges and commits must not change.

## Consequences

- Results and scores change on existing indexes without reindexing: regexp
  queries relying on search-within behaviour return fewer documents, regexp
  `TotalHits` drops to the unique count, and expansion scores on multi-segment
  indexes change to their merged-index values. No index format, codec or
  catalogue version changes.
- Multi-segment expansion queries pay one extra term-dictionary walk per
  segment to collect document frequencies. Single-segment searches are
  unchanged, because their segment frequency is already global.
- MoreLikeThis also selects terms with segment statistics; its segment
  invariance is tracked separately.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-11-11 | Accepted | Original decision recorded. | Sprint 6 PR 1 |
