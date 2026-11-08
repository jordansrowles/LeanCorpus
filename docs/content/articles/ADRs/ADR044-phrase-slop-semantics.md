---
adr: ADR044
title: "Phrase slop semantics and phrase frequency"
date: 2026-11-08
status: Accepted
version-added: v4.0.0
summary: "Define phrase-wide slop, phrase frequency and phrase scoring for PhraseQuery."
areas: [Search, Scoring]
---

# ADR044: Phrase slop semantics and phrase frequency

- **Date:** 2026-11-08
- **Status:** Accepted

## Context

Phrase execution applied the slop limit independently between adjacent query
terms. That allowed the total displacement across a phrase to exceed the
requested slop. Repeated query terms could also use one physical token for
more than one query slot. Phrase scoring used the rarest term's frequency as a
proxy for phrase frequency, so exact occurrence counts and sloppy match
distances did not affect scores.

Sloppy phrase frequency can be fractional. The existing public precomputed
similarity methods accepted an integer term frequency, so they could not
receive the phrase frequency required by this decision.

## Decision

A phrase with terms `t_1..t_n`, query positions `q_1..q_n` and slop `s` matches
when there are document positions `p_1..p_n` for those terms such that every
pair of positions is distinct and

```text
max(p_i - q_i) - min(p_i - q_i) <= s
```

This gives the phrase one shared slop budget. Transpositions use the same
measure; for example, `"said government"` requires slop 2 when the document
contains `government said`.

Phrase frequency is calculated per document. At slop 0, it is the number of
exact phrase start positions. Overlapping occurrences count, so `aa aa aa`
has frequency 2 for `"aa aa"`.

At slop greater than 0, phrase frequency is the sum of Lucene-style sloppy
frequencies, each weighted by `1 / (matchLength + 1)`. Calculate it with this
sweep over one position per query slot:

1. Start each slot at its first physical position `p_i`. Its offset position
   is `p_i - q_i`; `end` is the largest offset position.
2. Select the slot with the smallest offset position, breaking ties by the
   smaller query position. Let `next` be the second-smallest offset position
   and set `matchLength` to `end` minus the selected offset position.
3. Advance the selected slot. Update `end`. While its offset position is
   less than or equal to `next`, retain the smallest `matchLength` seen and
   advance it again. When it passes `next`, or runs out of positions, add
   `1 / (matchLength + 1)` to the frequency if `matchLength <= s`, then
   continue from the new smallest slot.
4. Stop when any slot runs out of positions, after adding the final
   `matchLength` if it is within `s`.
5. For repeated terms, slots with the same term cannot hold the same physical
   position. If an advance creates a collision, advance the colliding slot
   with the larger query position and repeat until the positions are distinct
   or a slot runs out. A slot running out stops the sweep.

A document matches when its phrase frequency is greater than zero. Phrase
scoring keeps the existing sum of per-term similarity scores, factors, boosts
and field boosts, but supplies phrase frequency to each term score instead of
the leader term frequency.

Change `ISimilarity.ScorePrecomputed`,
`ISimilarity.ScoreLmPrecomputed`, their implementations, and
`Bm25Scorer.ScorePrecomputed` to accept `float termFreq`. Sloppy phrase
frequency is fractional, so the scoring API must preserve its fractional
value. `Bm25Scorer.ScorePrecomputedBatch` retains integer frequency arrays.
Every integer frequency used by term queries converts exactly to `float`, so
term query scores remain bit-identical.

Lucene.NET is the behavioural reference for the listed phrase matching and
frequency cases. Its source implementation is not copied. `MultiPhraseQuery`
and its helpers, constant-boost scoring, and benchmark remain unchanged.

## Rationale

The offset range applies the specified slop once to the phrase as a whole and
allows positions to be selected independently of document order, including
transpositions. Requiring distinct physical positions prevents one occurrence
from satisfying repeated query slots. Counting exact starts and weighting
sloppy matches by their displacement makes phrase frequency reflect the
matches that caused the document to qualify.

The existing per-term score contributions remain intact. Passing the computed
phrase frequency through the existing scoring API changes only the frequency
input and permits each configured similarity to score fractional sloppy
matches consistently.

## Consequences

- Some phrases that matched under per-gap slop or reused positions no longer
  match.
- Exact phrases count all occurrences, including overlapping occurrences.
- Sloppy phrase scores and rankings change according to phrase frequency.
- Custom `ISimilarity` implementations must change the two precomputed method
  signatures to accept `float`.
- Term query scores remain unchanged, and no analysis, stored-position,
  on-disk format or `MultiPhraseQuery` change is required.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-11-08 | Accepted | Recorded phrase-wide slop, phrase frequency and fractional-frequency scoring semantics. | Sprint 2 PR 2 Phrase Matcher specification |
