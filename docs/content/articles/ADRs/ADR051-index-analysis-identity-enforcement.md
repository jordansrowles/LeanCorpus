---
adr: ADR051
title: "Index analysis identity enforcement"
date: 2026-11-21
status: Accepted
version-added: v4.0.0
summary: "Each commit records the analysis identity of every analysed field, and IndexWriter refuses to write a field under a different identity, including into indexes written before identities existed, unless the change is explicitly accepted."
areas: [analysis, index, compatibility]
extends: [ADR050]
relates-to: [ADR021, ADR036]
---

# ADR051: Index analysis identity enforcement

- **Date:** 2026-11-21
- **Status:** Accepted

## Context

ADR050 gave every default language pipeline an identity that changes exactly when its output can
change. Nothing used it: an index silently kept terms from one analysis while new documents and
queries used another. Upgrading to 4.0 does exactly that, because the Snowball stemmers, the
`StemmedAnalyser` stemmer and stop-word position gaps all change indexed terms. A review of #131
asked for the identity to be part of the index contract, with an explicit policy for indexes
written before identities existed.

Analysis identity is a property of a field's content, not of a segment or a codec. Merges and codec
migration move terms without reanalysing them. Commits are the only visibility boundary
(ADR036).

## Decision

1. **Analysers declare identities.** `IAnalysisIdentityProvider.AnalysisId` (Rowles.Text, so also
   compiled into LeanCorpus under ADR021) reports an analyser's identity, or `null`.
   - The pipelines `AnalyserFactory.Create(code)` returns declare `GetDefaultAnalysisId(code)`.
   - `StandardAnalyser` with its default stop words declares `rowles-analysis/standard/...`, and
     `StemmedAnalyser` without a keyword marker declares `rowles-analysis/stemmed-en/...`. Both
     entries live in `analysis-versions.json` with fingerprints, so ADR050's rule applies to them
     too; `stemmed-en` follows the English Snowball code.
   - Anything whose output the caller chooses (a custom stemmer or stop words, a keyword marker,
     `Create(code, stemmer)`, a hand-built `Analyser`) declares none.
   - `AnalysisIdentity.Assign(analyser, id)` identifies any analyser without changing its output.
     Thread-local copies keep their identity.
2. **Each commit records per-field identities.** `CommitData.FieldAnalysisIdentities` maps every
   analysed field to its analyser's identity, or `unidentified` when the analyser declares none or
   writer-level character filters are configured (they change every field's output). The property
   is optional JSON: commits written before it have no map (`null`), and readers that predate it
   ignore it. No commit-format version changes.
3. **The writer enforces them.** Each field's current identity comes from
   `IndexWriterConfig.FieldAnalysers` or the effective default analyser.
   - A recorded field with a different identity throws `AnalysisIdentityMismatchException` when
     the writer opens, before anything is written.
   - A field the index has not recorded is checked before its first analysis, then recorded.
   - A commit with no segments clears constraints from fields the writer did not use.
4. **Pre-identity indexes are unknown, not assumed.** If the latest commit has no map but has
   segments, its terms come from an older analysis that matches no 4.0 identity. The writer
   refuses to analyse any field into it, while non-analysed fields (string, numeric, stored) stay
   writable. Search is unaffected.
5. **Changes are accepted explicitly.** `IndexWriterConfig.AcceptAnalysisIdentityChanges = true`
   records the current identity instead of throwing, for a mismatch or pre-identity content. It
   does not reanalyse existing terms. The safe path remains reindexing into a new index.
6. **Identities survive the index lifecycle.** Merges and later commits carry the map forward;
   codec migration copies it; backup and restore copy the commit file unchanged.
7. **Query analysis is checked on request.** `IndexAnalysisIdentities.Read(directory)` returns the
   map (`null` for a pre-identity index), and `EnsureCompatible(directory, field, analyser)` throws
   the same exception for a mismatched query analyser. A field recorded as `unidentified` cannot
   be verified and passes.

## Rationale

Recording identities per field in the commit puts them on the same visibility boundary as the
content they describe, and needs no change to segment or codec formats. Failing when the writer
opens catches a changed analyser before any mixed content exists. Treating pre-identity content as
unknown, rather than mapping it to `0.1.0`, is the only honest policy: 4.0 changed every pipeline's
positions, so no 4.0 identity describes 3.x output. Built-in identities are declared only where
the output is fully determined, because a wrong identity is worse than none.

## Consequences

- A LeanCorpus 3.x index opened for writing by 4.0 refuses analysed fields until it is reindexed
  or `AcceptAnalysisIdentityChanges` is set. This makes the 4.0 reindex requirement visible
  instead of silent.
- Changing a field's analyser, or the default analyser, on an existing index now fails unless
  accepted. Applications that deliberately mix analyses must opt in.
- Two different unidentified analysers cannot be told apart. Custom pipelines should use
  `AnalysisIdentity.Assign`.
- Writer-level character filters make every field `unidentified`, so changes behind them are not
  detected.
- Query-time enforcement is opt-in, because query analysers are not part of the index.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-11-21 | Accepted | Original decision accepted. | `#131` |
