---
adr: ADRnnn
title: "[title]"
date: YYYY-MM-DD
status: Accepted
version-added: vNext
summary: "[one sentence summary]"
areas: [area]
# Add last-updated only after a material clarification, amendment or lifecycle change.
# Relationship values are lists and may reference core ADRnnn or Server ADR-Snnn IDs.
# Omit relationship fields that have no entries. Do not author superseded-by.
# supersedes: [ADRnnn]
# depends-on: [ADRnnn]
# extends: [ADRnnn]
# relates-to: [ADRnnn]
---

# ADRnnn: [title]

- **Date:** YYYY-MM-DD
- **Status:** Accepted | Superseded | Deprecated | Withdrawn

## Context

What is the issue that motivated this decision? Describe the forces at play:
technical constraints, performance data, maintenance burden or user
requirements. Keep it factual and grounded.

## Decision

What was decided? State the chosen approach in plain terms. Include enough
detail that someone reading this later understands what changed.

## Rationale

Why this option over the alternatives? Reference benchmarks, code citations or
design principles. Avoid hand-waving; the next maintainer should be able to
follow the reasoning.

## Consequences

What changes as a result of this decision? List concrete outcomes, limitations
and follow-up work.

## Changes

Keep `date` as the immutable original decision date. Add `last-updated` only
when a material architectural change is recorded below. Do not add history for
copy edits, formatting, link repairs or metadata refreshes. The first row must
record the original acceptance; later rows use `Clarified`, `Amended`,
`Deprecated`, `Superseded` or `Withdrawn`. `last-updated` must equal the date of
the latest material row.

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| YYYY-MM-DD | Accepted | Original decision accepted. | `commit` |

Core records use the next reserved `ADRnnn` identifier. Community Server
records use the separate `ADR-Snnn` namespace and the same fields, lifecycle,
relationship vocabulary and change-history rules. The site derives
`superseded-by` and reverse `relates-to` links from the complete ADR graph.
