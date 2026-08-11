using System.Collections.Generic;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// CodecKit format declarations for codec file types.
/// Most formats wrap the body as opaque bytes in a <c>VersionEnvelope</c>
/// providing version dispatch and forward compatibility.
/// Stored fields (.fdt/.fdx) are registered for migration but are written
/// directly via <see cref="StoredFieldsFileHeader"/> without a CodecKit envelope.
/// </summary>
internal static class CodecFormats
{
    /// <summary>
    /// Registers all built-in codec formats with the migration registry.
    /// Formats are registered by <see cref="CodecMigrationRegistry.Register"/> during static
    /// initialisation. Adding a new version to an existing format means inserting a new
    /// <see cref="CodecVersionStep"/> into the format's constructor call here and bumping
    /// the corresponding constant in <see cref="CodecConstants"/>.
    /// </summary>
    static CodecFormats()
    {
        var reg = CodecMigrationRegistry.Default;

        // Term vectors — v1 has no offsets; v2 adds hasOffsets + conditional offset arrays.
        reg.Register(new CodecFormat("tvx", [
            new CodecVersionStep(1, "tvx-v1", Codec.BytesOwnedRemaining()),
            new CodecVersionStep(2, "tvx-v2", Codec.BytesOwnedRemaining())
        ]));

        // Norms — v1 uses Int32LE for length fields; v2 uses VarInt.
        reg.Register(new CodecFormat("nrm", [
            new CodecVersionStep(1, "nrm-v1", Codec.BytesOwnedRemaining()),
            new CodecVersionStep(2, "nrm-v2", Codec.BytesOwnedRemaining())
        ]));

        // Stored fields — v1 used a CodecKit envelope; v2 streams without a body-length prefix.
        foreach (var ext in new[] { "fdt", "fdx" })
            reg.Register(new CodecFormat(ext, [
                new CodecVersionStep(1, $"{ext}-v1", Codec.BytesOwnedRemaining()),
                new CodecVersionStep(2, $"{ext}-v2", Codec.BytesOwnedRemaining())
            ]));

        // Postings — v1 used CodecKit envelope; v2 streams directly without body-length prefix.
        reg.Register(new CodecFormat("pos", [
            new CodecVersionStep(1, "pos-v1", Codec.BytesOwnedRemaining()),
            new CodecVersionStep(2, "pos-v2", Codec.BytesOwnedRemaining()),
            new CodecVersionStep(3, "pos-v3", Codec.BytesOwnedRemaining()),
            new CodecVersionStep(4, "pos-v4", Codec.BytesOwnedRemaining())
        ]));

        // All other formats are at v1.
        foreach (var ext in new[] { "fln","ndv","sdv","bdv","ssdv","sndv","tim","hnsw","vec","qvec","bkd","rbm","ldv","lsdv","lbkd" })
            reg.Register(new CodecFormat(ext, [
                new CodecVersionStep(1, $"{ext}-v1", Codec.BytesOwnedRemaining())
            ]));
    }

    // Postings is a legacy v1-only codec for tests and backward compatibility.
    // v2 postings bypass the CodecKit envelope entirely — use PostingsFileHeader instead.
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

        var format = CodecMigrationRegistry.Default.Get(ext);
        if (format != null)
        {
            // The catalogue-backed current version remains authoritative while this
            // compatibility envelope is retained for legacy reads.
            var currentStep = format.Steps.FirstOrDefault(step => step.Version == currentVersion);
            cases.Add(Codec.VersionCase<byte[], byte[]>(
                currentVersion,
                currentStep?.Label ?? $"{ext}-v{currentVersion}",
                currentStep?.Reader ?? Codec.BytesOwnedRemaining()));

            for (int i = format.Steps.Count - 1; i >= 0; i--)
            {
                var step = format.Steps[i];
                if (step.Version == currentVersion)
                    continue;
                cases.Add(Codec.VersionCase<byte[], byte[]>(
                    (byte)step.Version, step.Label, step.Reader));
            }
        }
        else
        {
            // Fallback: single-case when no registry entry exists.
            cases.Add(Codec.VersionCase<byte[], byte[]>(
                currentVersion, $"{ext}-v{currentVersion}",
                Codec.BytesOwnedRemaining()));
        }

        return Codec.VersionEnvelope<byte[], byte>(
            versionCodec: Codec.UInt8,
            bodyLengthCodec: Codec.VarInt64,
            unknown: (ver, body) => body,
            cases: cases.ToArray());
    }

}
