using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// CodecKit format declarations for codec file types.
/// Most formats wrap the body as opaque bytes in a <c>VersionEnvelope</c>
/// providing version dispatch and forward compatibility.
/// Stored fields (.fdt/.fdx) are written directly via
/// <see cref="StoredFieldsFileHeader"/> without a CodecKit envelope.
/// </summary>
internal static class CodecFormats
{
    // Postings is a legacy v1-only codec for tests and backward compatibility.
    // v2 postings bypass the CodecKit envelope entirely. Use PostingsFileHeader instead.
    internal static readonly ICodec<byte[]> Postings = Codec.VersionEnvelope<byte[], byte>(
        versionCodec: Codec.UInt8,
        bodyLengthCodec: Codec.VarInt64,
        unknown: (ver, body) => body,
        cases:
        [
            Codec.VersionCase<byte[], byte[]>((byte)1, "pos-v1", Codec.BytesOwnedRemaining()),
        ]);

    internal static readonly ICodec<byte[]> Norms = Create("nrm", CodecConstants.NormsVersion);
    internal static readonly ICodec<byte[]> FieldLengths = Create("fln", CodecConstants.FieldLengthVersion);
    internal static readonly ICodec<byte[]> NumericDocValues = Create("ndv", CodecConstants.NumericDocValuesVersion);
    internal static readonly ICodec<byte[]> SortedDocValues = Create("sdv", CodecConstants.SortedDocValuesVersion);
    internal static readonly ICodec<byte[]> BinaryDocValues = Create("bdv", CodecConstants.BinaryDocValuesVersion);
    internal static readonly ICodec<byte[]> SortedSetDocValues = Create("ssdv", CodecConstants.SortedSetDocValuesVersion);
    internal static readonly ICodec<byte[]> SortedNumericDocValues = Create("sndv", CodecConstants.SortedNumericDocValuesVersion);
    internal static readonly ICodec<byte[]> TermVectors = Create("tvx", CodecConstants.TermVectorsVersion);
    internal static readonly ICodec<byte[]> TermDictionary = Create("tim", CodecConstants.TermDictionaryVersion);
    internal static readonly ICodec<byte[]> Hnsw = Create("hnsw", CodecConstants.HnswVersion);
    internal static readonly ICodec<byte[]> Vectors = Create("vec", CodecConstants.VectorVersion);
    internal static readonly ICodec<byte[]> QuantisedVectors = Create("qvec", CodecConstants.QuantisedVectorVersion);
    internal static readonly ICodec<byte[]> Bkd = Create("bkd", CodecConstants.BKDVersion);
    internal static readonly ICodec<byte[]> Int64DocValues = Create("ldv", CodecConstants.Int64DocValuesVersion);
    internal static readonly ICodec<byte[]> Int64SortedNumericDocValues = Create("lsdv", CodecConstants.Int64SortedNumericDocValuesVersion);
    internal static readonly ICodec<byte[]> Int64Bkd = Create("lbkd", CodecConstants.Int64BKDVersion);
    internal static readonly ICodec<byte[]> RoaringBitmap = Create("rbm", CodecConstants.RoaringBitmapVersion);

    private static ICodec<byte[]> Create(string ext, byte currentVersion)
    {
        var cases = new List<VersionCaseDefinition<byte[]>>();

        // This envelope is retained only for supported legacy files and CodecKit
        // compatibility tests. The immutable CodecCatalog is the sole authority for
        // persistent format policy and current writable versions.
        for (byte version = currentVersion; version >= 1; version--)
            cases.Add(Codec.VersionCase<byte[], byte[]>(
                version, $"{ext}-v{version}",
                Codec.BytesOwnedRemaining()));

        return Codec.VersionEnvelope<byte[], byte>(
            versionCodec: Codec.UInt8,
            bodyLengthCodec: Codec.VarInt64,
            unknown: (ver, body) => body,
            cases: cases.ToArray());
    }

}
