namespace Rowles.LeanCorpus.Tests.Shared.Fixtures;

/// <summary>
/// Literal fixtures from LeanCorpus's pre-canonical layouts. The arrays are deliberately not
/// produced by current codec writers, so compatibility coverage cannot drift with writer changes.
/// </summary>
public static class HistoricalCodecFixtures
{
    // .dvn v1: [version][zig-zag VarInt body length][body]. Body contains one dense
    // field named "count", one document, and the constant IEEE-754 value 42.
    public static ReadOnlySpan<byte> NumericEnvelopeV1 =>
    [
        0x01, 0x36,
        0x01, 0x00, 0x00, 0x00,
        0x05, 0x63, 0x6f, 0x75, 0x6e, 0x74,
        0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x45, 0x40,
        0x00,
    ];

    // .dvn v2: [version][body][little-endian int64 body length]. The body is the
    // same hand-encoded semantic record as the v1 fixture.
    public static ReadOnlySpan<byte> NumericTrailerV2 =>
    [
        0x02,
        0x01, 0x00, 0x00, 0x00,
        0x05, 0x63, 0x6f, 0x75, 0x6e, 0x74,
        0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x45, 0x40,
        0x00,
        0x1b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    // .pos v2: custom streaming header followed by a two-document posting list.
    // Delta document IDs 3 and 4 decode to absolute document IDs 3 and 7.
    public static ReadOnlySpan<byte> PostingsCustomHeaderV2 =>
    [
        0x02,
        0x02, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00,
        0x03, 0x04,
    ];

    // .del v1: headerless live-docs file containing a historical Roaring envelope.
    // Its single array container marks document 1 deleted; the final int32 is the
    // empty soft-delete timestamp section. CRC32 0x0BAE3808 covers the literal payload.
    public static ReadOnlySpan<byte> LiveDocsHeaderlessV1 =>
    [
        0x01, 0x26,
        0x0b, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00,
        0x01, 0x00,
        0x01, 0x00,
        0x08, 0x38, 0xae, 0x0b,
        0x00, 0x00, 0x00, 0x00,
    ];
}
