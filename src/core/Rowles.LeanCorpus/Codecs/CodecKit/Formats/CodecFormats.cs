using System.Collections.Generic;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// CodecKit format declarations for all codec file types.
/// Each wraps the existing body as opaque bytes in a <c>VersionEnvelope</c>
/// providing version dispatch and forward compatibility.
/// When dual-format read is wired, these replace the manual magic+version header.
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

        // All other formats are at v1.
        foreach (var ext in new[] { "fln","ndv","sdv","bdv","ssdv","sndv","fdt","pos","tim","hnsw","vec","qvec","bkd","rbm" })
            reg.Register(new CodecFormat(ext, [
                new CodecVersionStep(1, $"{ext}-v1", Codec.BytesOwnedRemaining())
            ]));
    }

    internal static readonly ICodec<byte[]> Norms = Create("nrm", CodecConstants.NormsVersion);
    internal static readonly ICodec<byte[]> FieldLengths = Create("fln", CodecConstants.FieldLengthVersion);
    internal static readonly ICodec<byte[]> NumericDocValues = Create("ndv", CodecConstants.NumericDocValuesVersion);
    internal static readonly ICodec<byte[]> SortedDocValues = Create("sdv", CodecConstants.SortedDocValuesVersion);
    internal static readonly ICodec<byte[]> BinaryDocValues = Create("bdv", CodecConstants.BinaryDocValuesVersion);
    internal static readonly ICodec<byte[]> SortedSetDocValues = Create("ssdv", CodecConstants.SortedSetDocValuesVersion);
    internal static readonly ICodec<byte[]> SortedNumericDocValues = Create("sndv", CodecConstants.SortedNumericDocValuesVersion);
    internal static readonly ICodec<byte[]> StoredFields = Create("fdt", CodecConstants.StoredFieldsVersion);
    internal static readonly ICodec<byte[]> Postings = Create("pos", CodecConstants.PostingsVersion);
    internal static readonly ICodec<byte[]> TermVectors = Create("tvx", CodecConstants.TermVectorsVersion);
    internal static readonly ICodec<byte[]> TermDictionary = Create("tim", CodecConstants.TermDictionaryVersion);
    internal static readonly ICodec<byte[]> Hnsw = Create("hnsw", CodecConstants.HnswVersion);
    internal static readonly ICodec<byte[]> Vectors = Create("vec", CodecConstants.VectorVersion);
    internal static readonly ICodec<byte[]> QuantisedVectors = Create("qvec", CodecConstants.QuantisedVectorVersion);
    internal static readonly ICodec<byte[]> Bkd = Create("bkd", CodecConstants.BKDVersion);
    internal static readonly ICodec<byte[]> RoaringBitmap = Create("rbm", CodecConstants.RoaringBitmapVersion);

    private static ICodec<byte[]> Create(string ext, byte currentVersion)
    {
        var cases = new List<VersionCaseDefinition<byte[]>>();

        var format = CodecMigrationRegistry.Default.Get(ext);
        if (format != null)
        {
            // Add newest first so the VersionEnvelope encodes the current version.
            for (int i = format.Steps.Count - 1; i >= 0; i--)
            {
                var step = format.Steps[i];
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

    // ─── Temporary-file cleanup ───────────────────────────────────────────

    /// <summary>
    /// On-disk file extensions (without leading dot) that codec writers use.
    /// Temp files matching <c>*.ext.tmp</c> or <c>*.ext.body.tmp</c> are
    /// recognised as safe to clean up. When adding a new codec that writes
    /// via <see cref="Store.IndexOutput"/> or <see cref="Store.IndexAtomicFileWriter"/>,
    /// add its extension here.
    /// </summary>
    private static readonly HashSet<string> CodecExtensions = new(StringComparer.Ordinal)
    {
        // Segment & commit
        "seg",
        // Term dictionary & postings
        "dic", "pos", "tim",
        // Norms & field lengths
        "nrm", "fln",
        // Stored fields
        "fdt", "fdx",
        // Doc values (on-disk conventions)
        "dvn", "dvs", "dss", "dsn", "dvb",
        // Term vectors
        "tvd", "tvx",
        // Vectors & HNSW
        "vec", "vq", "hnsw",
        // BKD tree & roaring bitmaps
        "bkd", "rbm",
        // Numeric index
        "num",
        // Parent bitset
        "pbs",
        // Deletions (also matched by _gen_ prefix below)
        "del",
    };

    /// <summary>
    /// Returns <c>true</c> when <paramref name="fileName"/> matches a known
    /// temporary-file pattern produced by codec atomic writes, commit staging,
    /// or migration. Safe to delete.
    /// </summary>
    internal static bool IsRecognisedTemporaryFile(string fileName)
    {
        // Exact match
        if (fileName == "migration_state.json.tmp")
            return true;

        // Commit staging: segments_N.tmp
        if (fileName.StartsWith("segments_", StringComparison.Ordinal) &&
            fileName.EndsWith(".tmp", StringComparison.Ordinal))
            return true;

        // Stats snapshot: stats_N.json.tmp (commit-level)
        if (fileName.StartsWith("stats_", StringComparison.Ordinal) &&
            fileName.EndsWith(".json.tmp", StringComparison.Ordinal))
            return true;

        // Segment-level stats: seg_X.stats.json.tmp
        if (fileName.EndsWith(".stats.json.tmp", StringComparison.Ordinal))
            return true;

        // Per-generation deletion bitmaps
        if (fileName.Contains("_gen_", StringComparison.Ordinal) &&
            fileName.EndsWith(".del.tmp", StringComparison.Ordinal))
            return true;

        // Codec data files: *.ext.tmp or *.ext.body.tmp
        foreach (var ext in CodecExtensions)
        {
            var suffix = "." + ext;
            if (fileName.EndsWith(suffix + ".tmp", StringComparison.Ordinal) ||
                fileName.EndsWith(suffix + ".body.tmp", StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
