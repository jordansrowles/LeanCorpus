namespace Rowles.LeanCorpus.Codecs;

/// <summary>
/// Format version constants for all codec file types.
/// Most codec files use the CodecKit envelope: [byte version][VarInt64 bodyLen][body]
/// and are being migrated to a trailer format: [byte version][body][bodyLen:int64].
/// Stored fields and postings previously used a streaming v2 layout without a
/// body-length prefix; these are now folded into the CodecKit trailer format.
/// </summary>
internal static class CodecConstants
{
    // v2 -> v3 bumps for streaming trailer: postings, norms, stored fields, term vectors.
    // v1 -> v2 bumps for streaming trailer: all DocValues, field lengths, Int64 variants.
    public const byte TermDictionaryVersion = 1;
    public const byte PostingsVersion = 3;
    public const byte NormsVersion = 3;
    public const byte VectorVersion = 1;
    public const byte QuantisedVectorVersion = 1;
    public const byte HnswVersion = 1;
    public const byte StoredFieldsVersion = 3;
    public const byte TermVectorsVersion = 3;
    public const byte NumericDocValuesVersion = 2;
    public const byte SortedDocValuesVersion = 2;
    public const byte SortedSetDocValuesVersion = 2;
    public const byte SortedNumericDocValuesVersion = 2;
    public const byte BinaryDocValuesVersion = 2;
    public const byte BKDVersion = 1;
    public const byte Int64DocValuesVersion = 2;
    public const byte Int64SortedNumericDocValuesVersion = 2;
    public const byte Int64BKDVersion = 1;
    public const byte FieldLengthVersion = 2;
    public const byte RoaringBitmapVersion = 1;
}
