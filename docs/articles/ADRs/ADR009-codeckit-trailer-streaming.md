# ADR009: CodecKit trailer format replaces ADR008 custom headers

- **Date:** 2026-07-11
- **Status:** Accepted
- **Supersedes:** ADR008

## Context

ADR008 introduced v2 streaming formats for stored fields and postings that
bypassed CodecKit entirely, using a bare version byte with no body-length
prefix. This solved the buffering problem for those two codec types, but
left the remaining eleven codec types (DocValues, Norms, FieldLengths,
TermVectors, Int64 variants) stuck with the CodecKit envelope
`[version:byte][VarInt64 bodyLen][body]`. The envelope's variable-length
bodyLen forces writers to buffer the complete body before the first byte
hits disk.

The `IndexCodecMigrator.RewritePostings` materialised all terms and
posting rows into a `List<(string, List<PostingRow>)>` before writing.
Seven DocValues/FieldLengths rewrite methods each loaded full
`Dictionary<string, T[]>` via `WriteSingleFileAtomically` which buffered
the entire body in a CodecKit envelope. TermVectorsWriter and
TermVectorsStreamWriter buffered the complete `.tvd` body to compute the
envelope header size for `.tvx` offset rebasing.

The custom v2 headers created a split ecosystem: some codecs used
CodecKit, others used their own `*FileHeader` helpers. The migrator had
to special-case these formats.

## Decision

Add a **trailer-based format** to CodecKit that can coexist with the
existing envelope:

```
Trailer:  [version:byte][body][bodyLen:int64]
Envelope: [version:byte][VarInt64 bodyLen][body]
```

The 8-byte fixed `Int64` trailer is appended after the body is complete.
The writer calls `BeginStreamingWrite(output, version)` which writes the
version byte and returns a scope; callers write the body incrementally
to `scope.Output`; disposing the scope appends the trailer. Two reader
APIs handle detection:

- `ReadVersionAndSkipHeader`: returns the version and leaves the input
  positioned at body start.  Zero allocation, no materialisation.  Used by
  streaming readers (StoredFields, Postings).
- `ReadBody`: returns the full body as `byte[]`.  Used by callers that
  already materialise (TermDictionaryReader, RoaringBitmap, tests).

Trailer detection reads the last 8 bytes of the file and checks
`1 + bodyLen + 8 == fileLength`. For envelope files this check fails
(they have `1 + varIntSize + bodyLen` total), so the reader falls back
to the VarInt64-based envelope path. The false-positive risk is
vanishingly small and disambiguated by the version byte.

### Version bumps

All twelve codec types that use the trailer format have their version
constants bumped. Body formats are unchanged; only the outer wrapping
differs.

| Codec | Old | New |
|-------|-----|-----|
| StoredFields | 2 (custom) | 3 (trailer) |
| Postings | 2 (custom) | 3 (trailer) |
| Norms | 2 (envelope) | 3 (trailer) |
| TermVectors | 2 (envelope) | 3 (trailer) |
| NumericDocValues | 1 (envelope) | 2 (trailer) |
| SortedDocValues | 1 (envelope) | 2 (trailer) |
| SortedSetDocValues | 1 (envelope) | 2 (trailer) |
| SortedNumericDocValues | 1 (envelope) | 2 (trailer) |
| BinaryDocValues | 1 (envelope) | 2 (trailer) |
| FieldLengths | 1 (envelope) | 2 (trailer) |
| Int64DocValues | 1 (envelope) | 2 (trailer) |
| Int64SortedNumericDocValues | 1 (envelope) | 2 (trailer) |

Vectors, BKD, RoaringBitmap, and TermDictionary are unchanged.

### Codec migration

The index codec migrator now streams data through:

- `RewritePostings`: iterates terms lazily via `EnumerateTerms()`,
  writes each term's postings immediately via `BeginStreamingWrite` +
  `BlockPostingsWriter`. No materialisation of the full postings list.
- DocValues/FieldLengths rewrites: two-pass field enumeration via
  `EnumerateFields()`. First pass counts fields and determines max
  doc count; second pass serialises each field to a reusable
  `ArrayBufferWriter<byte>` and writes to the trailer scope. Memory is
  O(largest single field), not O(all fields).
- `WriteSingleFileAtomically` deleted; all seven rewrite methods
  manage their own temp-file lifecycle.

### ADR008 codecs folded into CodecKit

Stored fields, postings, and term vectors now use `CodecFileHeader`
instead of their custom `*FileHeader` write helpers:

- `StoredFieldsFileHeader.WriteV2FdtHeader` deleted; replaced by
  `BeginStreamingWrite` + block metadata. `.fdx` bumped to v3 via
  `WriteV3FdxHeader`.
- `PostingsFileHeader.WriteV2Header` deleted; replaced by
  `BeginStreamingWrite`. `WriteV1Header` was already dead code.
- TermVectors no longer buffers `.tvd` body; offsets are captured as
  file-absolute `scope.Output.Position` values during the write.
  `headerSize` rebasing removed.

Readers accept v1, v2, and v3: `StoredFieldsReader.Open` bumps version
guards to `> V3`; `PostingsEnum.ValidateFileHeader` uses the bumped
`CodecConstants.PostingsVersion`; `TermVectorsReader.Open` delegates to
the trailer-aware `ReadVersion`.

## Rationale

The trailer format fixes the root cause at the CodecKit level rather
than bypassing it per codec type. One pattern serves all writers.
Existing envelope files remain readable through automatic fallback
detection. The custom v2 headers from ADR008 are removed, consolidating
the codec ecosystem.

The 8-byte fixed trailer was chosen over a post-body VarInt64 to avoid
ambiguity: VarInt64 can be 1 to 9 bytes, making the exact file end
position of the bodyLen value unknown. A fixed 8-byte `Int64` trailer
can always be read by seeking to `length - 8`.

## Consequences

- `CodecFileHeader` gains `StreamingWriteScope`, `BeginStreamingWrite`,
  `ReadVersionAndSkipHeader`, and `ReadBody`. Twelve codec version
  constants are bumped.
- `PostingsFileHeader`, `StoredFieldsFileHeader` lose their write
  helpers; only `ReadVersion` and skip-data helpers remain.
- `StoredFieldsFileHeader.WriteV2FdtHeader`, `WriteV2FdxHeader` deleted;
  `WriteV3FdxHeader` added.
- `TermVectorsWriter` and `TermVectorsStreamWriter` no longer buffer
  the `.tvd` body; `headerSize` rebasing removed.
- `IndexCodecMigrator.WriteSingleFileAtomically` deleted; all eight
  rewrite methods manage their own temp-file output.
- `TermDictionaryReader.EnumerateTerms` added (lazy FST enumeration).
- Seven DocValues readers gain `EnumerateFields`; `NormsWriter` and
  `FieldLengthWriter` gain `WriteFieldBlock` helpers.
- `NumericDocValuesWriter.WriteFieldBlock(IBufferWriter<byte>,...)` and
  `SortedDocValuesWriter.WriteFieldBlock(IBufferWriter<byte>,...)`
  visibility changed from `private` to `internal`.
- `ReadVarInt64` (ZigZag-decoded) added to `CodecFileHeader` for
  envelope fallback parsing.
- `BinaryReader` overloads of `ReadVersionAndSkipHeader` and `ReadBody`
  remain envelope-only for now; BinaryReader internal buffering makes
  stream-seeking unsafe. Stored fields reader handles v3 by bumping
  version guards rather than using these overloads.
- Existing v1 and v2 indexes remain readable through version fallback.
