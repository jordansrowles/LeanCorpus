---
adr: ADR040
title: "Field-length v3 bodies use bounded fixed-width values"
date: 2026-10-08
last-updated: 2026-10-08
status: Withdrawn
version-added: vNext
summary: "Bound field-length metadata against the owning segment and write fixed-width Int32 values while retaining checked legacy reads."
areas: [codecs, indexing, migration, recovery]
---

# ADR040: Field-length v3 bodies use bounded fixed-width values

- **Date:** 2026-10-08
- **Status:** Withdrawn.
## Context

Field-length v2 persists VarInt token counts. Its decoder allocated collections,
names and arrays from unchecked metadata before validating the body checksum.
GitLab issue 33 requires a byte containment check against the decoded Int32
array size. That check cannot be applied to a VarInt body: a valid value may
occupy one encoded byte. The user authorised a format revision and migration
as part of the existing major release work.

## Decision

The catalogue advances `leancorpus.field-lengths.data` to body version 3.
Keep the field count, UTF-8 name length, name and document count layout;
replace each VarInt value with a little-endian Int32. Frame v1 is unchanged.

One decoder handles reading, enumeration and validation. It bounds field and
name counts against the remaining bounded body, validates names and uniqueness,
and requires every v3 document count to equal the owning segment's `DocCount`.
Checked 64-bit arithmetic proves all v3 value bytes fit before array allocation.
Reject negative values, truncated records and trailing bytes. Verify checksums
on successful canonical reads. Invalid metadata raises `InvalidDataException`.

Versions 1 and 2 retain the existing VarInt interpretation. Bound the minimum
encoded bytes against the body, validate all encoded values before allocating
the decoded array, and enforce the same name invariants. Historical writers
persisted pooled-array capacity beyond the owning segment's document count.
Accept that legacy excess only when every extra value is zero. Require the
encoded count to cover the logical document range and fit the bounded body;
return an array sized to the owning segment, never to the padded count.
Migration discards this zero padding. Non-zero padding and all v3 count
mismatches remain invalid. The user explicitly authorised this compatibility
exception after immutable historical index fixtures demonstrated the padding.

Normal segment opening validates present field-length sidecars before publishing
reader state. Deep validation, merging and recovery use the same decoder.
Migration rewrites v1/v2 through the current v3 writer using authoritative segment
metadata, including logical members staged from compound files.

## Rationale

Fixed-width values make the required decoded-size containment check exact and
remove variable-length parsing from current reads. Legacy validation preserves
existing readable bodies without weakening allocation bounds.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-10-08 | Accepted | Original decision accepted. | `af6aacfa7` |
| 2026-10-08 | Withdrawn | Reverted the v3 field-length decision and restored v2 VarInt writes. | `53b537956` |

## Consequences

- Small token counts use four bytes per document and field instead of one.
- Older releases cannot read v3 bodies; mutation of legacy indexes requires migration.
- There is no arbitrary document-count limit and no change to Frame v1.
- Opening a segment scans its `.fln` metadata and values and validates its checksum.
- Corruption, legacy reads and migration require regression coverage on both frameworks.
