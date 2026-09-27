namespace Rowles.LeanCorpus.Index.Segment;

internal readonly record struct SegmentReaderCacheResourceUsage(
    long PostingsAndTermsBytes,
    long NormsAndLengthsBytes,
    long StoredFieldsBytes,
    long TermVectorsBytes,
    long DocValuesBytes,
    long NumericIndexesBytes,
    long SpatialIndexesBytes,
    long VectorsBytes,
    long LiveDocsAndParentsBytes,
    long OtherBytes)
{
    internal long TotalBytes => PostingsAndTermsBytes + NormsAndLengthsBytes + StoredFieldsBytes
        + TermVectorsBytes + DocValuesBytes + NumericIndexesBytes + SpatialIndexesBytes
        + VectorsBytes + LiveDocsAndParentsBytes + OtherBytes;

    public static SegmentReaderCacheResourceUsage operator +(
        SegmentReaderCacheResourceUsage left,
        SegmentReaderCacheResourceUsage right)
        => new(
            left.PostingsAndTermsBytes + right.PostingsAndTermsBytes,
            left.NormsAndLengthsBytes + right.NormsAndLengthsBytes,
            left.StoredFieldsBytes + right.StoredFieldsBytes,
            left.TermVectorsBytes + right.TermVectorsBytes,
            left.DocValuesBytes + right.DocValuesBytes,
            left.NumericIndexesBytes + right.NumericIndexesBytes,
            left.SpatialIndexesBytes + right.SpatialIndexesBytes,
            left.VectorsBytes + right.VectorsBytes,
            left.LiveDocsAndParentsBytes + right.LiveDocsAndParentsBytes,
            left.OtherBytes + right.OtherBytes);

    public static SegmentReaderCacheResourceUsage operator -(
        SegmentReaderCacheResourceUsage left,
        SegmentReaderCacheResourceUsage right)
        => new(
            left.PostingsAndTermsBytes - right.PostingsAndTermsBytes,
            left.NormsAndLengthsBytes - right.NormsAndLengthsBytes,
            left.StoredFieldsBytes - right.StoredFieldsBytes,
            left.TermVectorsBytes - right.TermVectorsBytes,
            left.DocValuesBytes - right.DocValuesBytes,
            left.NumericIndexesBytes - right.NumericIndexesBytes,
            left.SpatialIndexesBytes - right.SpatialIndexesBytes,
            left.VectorsBytes - right.VectorsBytes,
            left.LiveDocsAndParentsBytes - right.LiveDocsAndParentsBytes,
            left.OtherBytes - right.OtherBytes);
}

internal readonly record struct SegmentReaderCacheResourceMetrics(
    int EntryCount,
    long EvictionCount,
    long RetainedBytes,
    long MaximumRetainedBytes,
    SegmentReaderCacheResourceUsage Resources);
