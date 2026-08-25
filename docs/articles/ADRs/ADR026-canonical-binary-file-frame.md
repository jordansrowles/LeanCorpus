---
adr: ADR026
title: Canonical binary files use the self-identifying LCCF Frame v1
date: 2026-08-09
status: Accepted
version-added: 3.0.0
summary: Frame current binary codec bodies with explicit identity, independent versions, bounded lengths and a fixed checksum footer.
areas: [codecs, store, integrity, recovery]
supersedes: ADR009
---

# ADR026: Canonical binary files use the self-identifying LCCF Frame v1

- **Date:** 2026-08-09
- **Status:** Accepted
- **Supersedes:** ADR009 for current writes; ADR009 framing remains readable as
  legacy data.

## Context

ADR009 made large codec bodies streamable by writing the version before the body
and the body length after it. That format has no positive identity. Readers guess
whether it is a trailer by interpreting the final eight bytes as a length, then
fall back to the older envelope. The frame version and body version are also the
same byte, so a framing-only change forced every affected codec version to move.

`CodecFileHeader` exposes its underlying seekable `IndexOutput`, writes a valid
length when its scope is disposed during exception unwinding, and provides
different framing capabilities for `IndexInput` and `BinaryReader`. Some reads
materialise the remaining physical file before format-specific limits can reject
it. The successful trailer branch of one `IndexInput` read path also reads the
version without first returning to the frame start.

The 3.0 frame must be positively identifiable, streamable in one pass, usable as
a bounded random-access body, deterministic across platforms and useful to
inspection and recovery without semantic body decoding.

## Decision

### Byte order and exact layout

Frame v1 uses little-endian fixed-width integers. Implementations must encode
them explicitly and must not depend on host endianness. A logical file contains
exactly one frame:

| Offset | Size | Field | Frame v1 value or rule |
| --- | ---: | --- | --- |
| `0` | 4 | Magic `UInt32` | `0x4643434C`, file bytes ASCII `LCCF` |
| `4` | 1 | Frame version `UInt8` | `1` |
| `5` | 1 | Format ID length `UInt8` | `1` to `64` |
| `6` | 4 | Format version `Int32` | positive |
| `10` | 4 | Flags `UInt32` | `0` |
| `14` | 1 | Checksum algorithm `UInt8` | identifier below |
| `15` | 1 | Reserved `UInt8` | `0` |
| `16` | `N` | Format ID | exact ASCII bytes, where `N` is the format ID length |
| `16 + N` | variable | Body | zero or more uninterpreted body bytes |
| `fileLength - 16` | 8 | Body length `Int64` | non-negative body byte count |
| `fileLength - 8` | 8 | Checksum `UInt64` | body checksum represented below |

The prefix is `16 + N` bytes and the footer is always 16 bytes. The smallest
valid frame is therefore 33 bytes. There is no padding, length backpatching or
trailing data.

The stored body length must equal:

```text
fileLength - (16 + formatIdLength) - 16
```

Equivalently, `bodyStart + bodyLength + 16` must equal the logical input length.
This equality is checked with overflow-safe arithmetic before a body view or
allocation is created.

### Format identity

Format IDs are stable catalogue identities, not filename extensions. Built-ins
use the `leancorpus.` namespace. Third-party IDs use a namespace controlled by
their owner. IDs are lowercase ASCII and match:

```text
[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+
```

An ID is at most 64 ASCII bytes, contains at least one namespace separator `.`,
is compared ordinally and is never normalised by a reader. A generated per-field filename carries the same format ID
as other files in that role. Separate coordinated roles such as stored-fields
data and index files have separate format IDs and share a catalogue family.

The catalogue rejects duplicate IDs and IDs whose encoded length is outside 1 to
64 bytes. Frame parsing validates the syntax before catalogue lookup.

### Version and flags

Frame version is independent of format version. LeanCorpus 3.0 writes Frame v1
with the existing current semantic body version declared by its descriptor. A
future framing change increments the frame version without changing an unchanged
body version.

Frame v1 requires flags and the reserved byte to be zero. A reader must reject
non-zero values as unsupported Frame v1 features rather than silently ignoring
them. Feature bits and the reserved byte can only acquire meaning through a
future decision with golden-byte coverage.

### Checksum representation

The checksum algorithm identifiers are:

| ID | Algorithm | Footer representation |
| ---: | --- | --- |
| `0` | None | all eight checksum bytes are zero |
| `1` | CRC32 | checksum in the low 32 bits, high 32 bits zero |
| `2` | xxHash32 | checksum in the low 32 bits, high 32 bits zero |
| `3` | xxHash64 | full 64-bit checksum |

The checksum field is stored as a little-endian `UInt64`. It covers the body
bytes only, including an empty body. It is not cryptographic authentication.

Current canonical binary descriptors default to xxHash64. Algorithm `0` is valid
only when the catalogue descriptor has an explicit documented opt-out. A writer
must reject a descriptor and algorithm mismatch. Unknown algorithm IDs are
reported structurally by inspection and rejected by normal reading and
validation.

The fixed checksum field keeps the footer addressable without decoding the body
and lets a future catalogue select among the existing providers without changing
footer size.

### Writer lifecycle

`CodecFileWriter.Begin` writes and validates the complete prefix, records the body
start and initialises incremental checksum state. It returns a
`CodecWriteSession` whose body output is append-only. Position reports the body
position required by streaming formats. Arbitrary seek is not exposed.

`Complete()` finalises the checksum, writes body length followed by the checksum
field, and marks the session complete. It is idempotence-protected and cannot be
called after disposal. Disposal without successful completion does not write a
footer and cannot turn a partial body into a syntactically valid frame.

The normal atomic helper creates a catalogue-recognised temporary file, runs the
body writer, completes and disposes the session and output, optionally flushes
durably, then publishes by atomic rename. It deletes the temporary file after a
failure. The output handle is always closed before rename as required by ADR010.

Direct, flush, merge and migration writers use this lifecycle. No 3.0 current
writer calls the legacy envelope or ADR009 trailer writer.

### Reader lifecycle

The canonical parser consumes a bounded logical `IndexInput`, whether that input
is a loose file or an ADR024 compound-file slice. `Open` performs no body
materialisation and validates, in order:

1. minimum frame length and exact magic;
2. supported frame version;
3. format ID length, syntax and catalogue identity;
4. positive supported format version;
5. flags, checksum algorithm and reserved byte;
6. non-negative body length and exact containment of prefix, body and footer;
7. any expected descriptor identity supplied by the caller.

It returns immutable metadata and a bounded body input whose lifetime is tied to
the read session. The body view cannot read the footer or another compound
member.

Fast open does not scan the body checksum. `Read<T>` and bounded `ReadBody`
verify it while consuming all body bytes. Deep validation explicitly scans and
verifies large random-access bodies. A failed checksum is a structured
`ChecksumMismatch` error containing file, format and byte-location context.

`ReadBody` checks `MaxMaterialisedBodyBytes` and `int` representation before
allocating. `Open` checks the logical input against `MaxCodecFileBytes` but does
not apply the nested `MaxFrameBytes` limit to the body. Large vector, HNSW, BKD,
postings and stored-field files remain bounded random access or streaming.

### Current and legacy dispatch

A storage reader examines the first four bytes. Exact `LCCF` magic selects only
the canonical parser. Invalid fields, unknown versions, truncation or checksum
failure after matching magic are canonical-frame errors and never trigger legacy
fallback.

When magic does not match, the catalogue descriptor may select an explicit
read-only legacy envelope, ADR009 trailer, stored-fields or postings reader. The
legacy trailer length heuristic exists only in that layer. Normal reads reject
future frame and format versions centrally. Inspection may report recognisable
future metadata without semantic decoding, and forensic recovery may request an
explicit raw body view.

### Golden and corruption tests

The byte contract is locked with checked-in golden vectors for empty, one-byte,
small and long bodies, all four checksum identifiers and a 64-byte format ID.
Tests compare exact bytes, not only round trips.

Negative and fuzz coverage includes each prefix and footer field, all structural
boundaries, truncation, inserted or removed bytes, absurd lengths, trailing data,
unknown IDs and algorithms, unsupported versions, body bit flips and checksum
mismatches. Tests assert bounded allocation, deterministic structured errors and
valid reader position guarantees. The same vectors run on Windows and Linux.

## Rationale

A fixed positive magic removes heuristic discrimination from current files. The
separate frame and format versions prevent a physical wrapper change from
creating artificial semantic body versions.

The variable-length format ID keeps the common prefix small while giving
third-party formats collision-resistant names. A 64-byte ASCII limit is ample for
namespaced IDs and makes validation and recovery scanning bounded.

The footer permits one-pass sequential writes without seeking or full-body
buffering. Fixed-size length and checksum fields allow constant-time structural
open and direct body slicing. xxHash64 reuses the existing CodecKit provider and
offers a wider corruption check than the existing 32-bit providers at the same
fixed footer cost.

Checksumming only the body allows incremental calculation through the restricted
body output. Prefix fields are protected by strict syntax, catalogue, version and
range validation. This is corruption detection, not an adversarial security
boundary.

## Consequences

- Frame v1 has a stable, platform-independent byte contract suitable for golden
  tests and recovery scanning.
- Every current binary file identifies its frame, format, semantic version and
  checksum algorithm without consulting its filename.
- The 16-byte footer adds a fixed storage cost, and the prefix costs 16 bytes plus
  the format ID.
- Ordinary random-access open remains constant-time and does not scan the body.
- A syntactically complete frame exists only after explicit successful
  completion.
- Legacy envelopes, ADR009 trailers and custom headers remain read-only behind
  descriptor-specific adapters.
- A 3.0 file cannot be read by a 2.x release. Supported old files remain readable
  by 3.0 and can be reframed or rewritten through catalogue migration.
- Any change to the byte layout, flag semantics or checksum representation
  requires a new frame version or a separately specified compatible extension
  and corresponding golden vectors.
