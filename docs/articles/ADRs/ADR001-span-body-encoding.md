# ADR001: Span-based body encoding for segment serialisation

- **Date:** 2026-06-16
- **Status:** Accepted

## Context

Every segment writer built the file body in an `ArrayBufferWriter<byte>`,
called `.WrittenSpan.ToArray()` to allocate a `byte[]` copy, and passed it
to `CodecFileHeader.Write`. That method then fed the `byte[]` to
`VersionEnvelopeCodec.Encode`, which immediately copied it again into a
scratch buffer to measure the body length before writing the version byte
and VarInt length prefix. Each segment flush produced two copies of the
body data before it reached the `IndexOutput`.

## Decision

Add a `ReadOnlySpan<byte>` overload of `CodecFileHeader.Write` and a
`EncodeSpan` fast path on `VersionEnvelopeCodec` that writes the version
byte, VarInt body length, and body bytes directly to the output buffer.
Body length is known from `span.Length`, so scratch-buffer staging is
unnecessary.

## Rationale

Eliminates one `byte[]` allocation and one copy per writer invocation.
The fast path is valid because all format codecs in LeanCorpus are
`VersionEnvelopeCodec<byte[], byte>` instances. The `ICodec<byte[]>`
interface is unchanged; the span path dispatches via a type check and
falls back to `byte[]` allocation for non-envelope codecs.

## Consequences

- Seventeen writer files updated to pass `bodyBuf.WrittenSpan` instead of
  `.WrittenSpan.ToArray()`.
- `VersionEnvelopeCodec.EncodeSpan` uses the first (newest) version case
  to write the envelope header, then copies the body span directly.
- `StoredFieldsWriter` and `TermVectorsWriter` header-size computations
  now use `WrittenCount` instead of `.Length` on the deleted `byte[]`.
- The `BinaryWriter` path in `CodecFileHeader` also gained a span overload
  for `RoaringBitmap` serialisation.
- Future writers must use the span overload. The old `byte[]` overload
  remains for backward compatibility but callers should migrate.
