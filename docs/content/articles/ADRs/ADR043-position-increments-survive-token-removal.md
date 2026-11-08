---
adr: ADR043
title: "Position increments survive token removal"
date: 2026-11-07
status: Accepted
version-added: 4.0.0
summary: "Stop-word removal transfers skipped position increments to the next retained token, and segments record whether they were written under that contract."
areas: [analysis, indexing, search, migration]
relates-to: [ADR021, ADR025, ADR028, ADR039, ADR042]
---

# ADR043: Position increments survive token removal

- **Date:** 2026-11-07
- **Status:** Accepted

## Context

`StandardAnalyser` and `StopWordFilter` discard a stop word without passing its
position increment on. The next retained token is emitted with the default
increment of one, so the removed word leaves no gap in the indexed positions.
Lucene.NET keeps the gap. For the text `said aspernatur at fugit government`,
Lucene.NET indexes `said`, `aspernatur`, `fugit` and `government` at positions 0,
1, 3 and 4, while LeanCorpus indexed them at 0, 1, 2 and 3. Across 5,000 corpus
documents the position increments differ in 3,627.

The effect reaches phrase matching. A phrase for `said government` matches the
text `said the government`, because the removed `the` left no gap. A sloppy phrase
accepts a document that Lucene.NET rejects, and slop budgets depend on whether a
stop word sat between the terms.

Positions are written into `.pos` as absolute values. A removed word's position
is not stored anywhere else. An index written before this decision therefore
cannot be corrected from its own data.

## Decision

A token removed by an analysis stage transfers its position increment to the next
retained token. The retained token is emitted with its own increment plus the
total of the increments of the tokens removed since the previous emitted token.
Removed tokens at the end of the input emit nothing, and the total is discarded.
This applies to `StandardAnalyser`, `StopWordFilter` and every analyser that is
built from them, including `StemmerAnalyser`, `StemmedAnalyser`,
`LanguageAnalyser` and `IcuAnalyser`.

Query analysis uses the same analysers, so a quoted phrase such as
`"said the government"` produces explicit positions 0 and 2. The query parser
already derives positions from the increments it receives and needs no separate
rule.

`StopWordFilter` carries the pending total as per-call state. It resets the
total in `Finish`, and an analysis that fails is discarded rather than reused, so
no total survives into a later document. `Clone` returns an independent instance,
because the filter is no longer stateless.

Each segment records whether it was written under this contract. The
segment-info JSON in the `.seg` file gains an additive nullable field,
`PositionContract`. An absent value means the segment is legacy. The value says
the segment was written under the preserved-gap contract. It says nothing about
which analyser was used, so every message that depends on it says an index "may
be affected".

- Flush stamps the field. Nothing else stamps it.
- A merged segment is stamped only when every source segment is stamped. A merge
  never marks collapsed positions as corrected.
- Migration and `DeepCopy` carry the value. They never stamp it.

An unmarked segment is reported by the new warning `LLIDX052`, with the
recommendation to reindex from the original text. The index status stays
`Compatible`, `CanExecute` stays true, and writers may append to and mix marked
and unmarked segments. `Strict` does not refuse.

Corrected positions need reindexing from source text. There is no codec migration.
`.pos` keeps its version, the codec catalogue is unchanged, and the migrator does
not claim to repair the data.

## Rationale

Transferring the increment is the behaviour of Lucene's `StopFilter` with position
increments enabled. It is the smallest change that makes exact and sloppy phrases
see the same distance as the text.

A `.seg` marker is the only option that makes the difference visible without
changing how old indexes open:

- **Bump the `.pos` version and mark the old version unsupported.** A version bump
  is for a change to the encoded bytes, and the bytes here are unchanged. Because
  `CanExecute` requires every action to be executable, an unsupported old version
  would block all 3.1.1 migration, including the stored-field and sorted DocValues
  rewrites those indexes need. It would force a rebuild on every upgrader, whether
  or not they use stop words or phrase queries.
- **Add a codec lifecycle policy, as for retired vector data.** The policy needs a
  tag inside the body to identify the representation, and `.pos` has none. The
  retired vector formats are read-only because a writer cannot produce them,
  while new corrected segments are valid here.
- **Report through `MigrationRecommended` or a lifecycle issue.** The first makes
  the index non-writable and sends the operator to a migrator that cannot help.
  The second makes migration non-executable.
- **Document the change in release notes only.** A missed note loses matches
  silently.
- **Add a per-segment query shim that drops gaps on unmarked segments.** It keeps
  the old false positives, cannot tell parser gaps from deliberate user gaps, and
  touches every phrase and span path. It stays in reserve and is not part of this
  decision.

The marker is sticky through merges so that a merge cannot launder collapsed
positions into a segment that looks corrected.

## Consequences

- Quoted phrases that contain stop words can miss unmarked segments until the
  index is rebuilt, because the parser now sends gapped positions to collapsed
  data. Unmarked segments also keep their false positives for phrases without
  stop words. The warning makes this visible and does not remove it.
- Mixed indexes behave differently per segment until the unmarked segments are
  replaced.
- The change is in the analysis source that is compiled into both `Rowles.Text`
  and `Rowles.LeanCorpus` under ADR021, so both packages change. No dependency is
  added.
- Token graphs keep their ADR028 rules. Removal changes increments only, so
  position lengths are unaffected.
- `.pos` bytes for text without stop words are unchanged, and the codec catalogue
  versions are unchanged.
- The matcher's slop semantics are a separate decision and are not changed here.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-11-07 | Accepted | Original decision accepted. | `4.0/perf` |
