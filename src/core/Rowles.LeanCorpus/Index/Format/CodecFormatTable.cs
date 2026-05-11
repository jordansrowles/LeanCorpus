using Rowles.LeanCorpus.Codecs;

namespace Rowles.LeanCorpus.Index.Format;

internal static class CodecFormatTable
{
    private static readonly Dictionary<string, CodecFormatDescriptor> Descriptors = new(StringComparer.OrdinalIgnoreCase)
    {
        [".dic"] = new("Term dictionary", CodecConstants.TermDictionaryVersion, HasHeader: true),
        [".pos"] = new("Postings", CodecConstants.PostingsVersion, HasHeader: true),
        [".nrm"] = new("Norms", CodecConstants.NormsVersion, HasHeader: true),
        [".vec"] = new("Vectors", CodecConstants.VectorVersion, HasHeader: true),
        [".hnsw"] = new("HNSW", CodecConstants.HnswVersion, HasHeader: true),
        [".fdt"] = new("Stored fields data", CodecConstants.StoredFieldsVersion, HasHeader: true),
        [".fdx"] = new("Stored fields index", CodecConstants.StoredFieldsVersion, HasHeader: true),
        [".tvd"] = new("Term vectors data", CodecConstants.TermVectorsVersion, HasHeader: true),
        [".tvx"] = new("Term vectors index", CodecConstants.TermVectorsVersion, HasHeader: true),
        [".dvn"] = new("Numeric DocValues", CodecConstants.NumericDocValuesVersion, HasHeader: true),
        [".dvs"] = new("Sorted DocValues", CodecConstants.SortedDocValuesVersion, HasHeader: true),
        [".dss"] = new("Sorted-set DocValues", CodecConstants.SortedSetDocValuesVersion, HasHeader: true),
        [".dsn"] = new("Sorted-numeric DocValues", CodecConstants.SortedNumericDocValuesVersion, HasHeader: true),
        [".dvb"] = new("Binary DocValues", CodecConstants.BinaryDocValuesVersion, HasHeader: true),
        [".bkd"] = new("BKD tree", CodecConstants.BKDVersion, HasHeader: true),
        [".fln"] = new("Field lengths", CodecConstants.FieldLengthVersion, HasHeader: true),
        [".del"] = new("Live docs", CodecConstants.RoaringBitmapVersion, HasHeader: true),
        [".num"] = new("Numeric field index", null, HasHeader: false),
        [".pbs"] = new("Parent bitset", null, HasHeader: false),
        [".seg"] = new("Segment metadata", null, HasHeader: false),
        [".stats"] = new("Segment statistics", null, HasHeader: false),
    };

    public static bool TryGet(string extension, out CodecFormatDescriptor descriptor)
        => Descriptors.TryGetValue(extension, out descriptor);

    public static bool IsRecognisedExtension(string extension)
        => Descriptors.ContainsKey(extension);
}

internal readonly record struct CodecFormatDescriptor(string CodecName, byte? CurrentVersion, bool HasHeader);
