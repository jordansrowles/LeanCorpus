using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;

namespace Rowles.LeanCorpus.Index.Format;

internal static class CodecFormatTable
{
    private static readonly Dictionary<string, CodecFormatDescriptor> Descriptors = new(StringComparer.OrdinalIgnoreCase)
    {
        [".dic"] = new("Term dictionary", CodecConstants.TermDictionaryVersion, HasHeader: true, HeaderFormat: CodecFormats.TermDictionary),
        [".pos"] = new("Postings", CodecConstants.PostingsVersion, HasHeader: true, HeaderFormat: CodecFormats.Postings),
        [".nrm"] = new("Norms", CodecConstants.NormsVersion, HasHeader: true, HeaderFormat: CodecFormats.Norms),
        [".vec"] = new("Vectors", CodecConstants.VectorVersion, HasHeader: true, HeaderFormat: CodecFormats.Vectors),
        [".vq"]  = new("Quantised vectors", CodecConstants.QuantisedVectorVersion, HasHeader: true, HeaderFormat: CodecFormats.QuantisedVectors),
        [".hnsw"]= new("HNSW", CodecConstants.HnswVersion, HasHeader: true, HeaderFormat: CodecFormats.Hnsw),
        [".fdt"] = new("Stored fields data", CodecConstants.StoredFieldsVersion, HasHeader: true, HeaderFormat: CodecFormats.StoredFields),
        [".fdx"] = new("Stored fields index", CodecConstants.StoredFieldsVersion, HasHeader: true, HeaderFormat: CodecFormats.StoredFields),
        [".tvd"] = new("Term vectors data", CodecConstants.TermVectorsVersion, HasHeader: true, HeaderFormat: CodecFormats.TermVectors),
        [".tvx"] = new("Term vectors index", CodecConstants.TermVectorsVersion, HasHeader: true, HeaderFormat: CodecFormats.TermVectors),
        [".dvn"] = new("Numeric DocValues", CodecConstants.NumericDocValuesVersion, HasHeader: true, HeaderFormat: CodecFormats.NumericDocValues),
        [".dvs"] = new("Sorted DocValues", CodecConstants.SortedDocValuesVersion, HasHeader: true, HeaderFormat: CodecFormats.SortedDocValues),
        [".dss"] = new("Sorted-set DocValues", CodecConstants.SortedSetDocValuesVersion, HasHeader: true, HeaderFormat: CodecFormats.SortedSetDocValues),
        [".dsn"] = new("Sorted-numeric DocValues", CodecConstants.SortedNumericDocValuesVersion, HasHeader: true, HeaderFormat: CodecFormats.SortedNumericDocValues),
        [".dvb"] = new("Binary DocValues", CodecConstants.BinaryDocValuesVersion, HasHeader: true, HeaderFormat: CodecFormats.BinaryDocValues),
        [".dvnl"] = new("Int64 DocValues", CodecConstants.Int64DocValuesVersion, HasHeader: true, HeaderFormat: CodecFormats.Int64DocValues),
        [".dsnl"] = new("Int64 sorted-numeric DocValues", CodecConstants.Int64SortedNumericDocValuesVersion, HasHeader: true, HeaderFormat: CodecFormats.Int64SortedNumericDocValues),
        [".bkd"] = new("BKD tree", CodecConstants.BKDVersion, HasHeader: true, HeaderFormat: CodecFormats.Bkd),
        [".bkdl"] = new("Int64 BKD tree", CodecConstants.Int64BKDVersion, HasHeader: true, HeaderFormat: CodecFormats.Int64Bkd),
        [".fln"] = new("Field lengths", CodecConstants.FieldLengthVersion, HasHeader: true, HeaderFormat: CodecFormats.FieldLengths),
        [".del"] = new("Live docs", CodecConstants.RoaringBitmapVersion, HasHeader: true, HeaderFormat: CodecFormats.RoaringBitmap),
        [".num"] = new("Numeric field index", null, HasHeader: false, HeaderFormat: null),
        [".numl"] = new("Int64 numeric field index", null, HasHeader: false, HeaderFormat: null),
        [".pbs"] = new("Parent bitset", null, HasHeader: false, HeaderFormat: null),
        [".seg"] = new("Segment metadata", null, HasHeader: false, HeaderFormat: null),
        [".stats"] = new("Segment statistics", null, HasHeader: false, HeaderFormat: null),
    };

    public static bool TryGet(string extension, out CodecFormatDescriptor descriptor)
        => Descriptors.TryGetValue(extension, out descriptor);

    public static bool IsRecognisedExtension(string extension)
        => Descriptors.ContainsKey(extension);
}

internal readonly record struct CodecFormatDescriptor(string CodecName, byte? CurrentVersion, bool HasHeader, ICodec<byte[]>? HeaderFormat);
