using System.Security.Cryptography;
using System.Text;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Searcher;

internal sealed record SearchCursorData(string SessionId, string IndexIdentity, int Generation,
    string QueryIdentity, string SortIdentity, string RankingIdentity, ScoreDoc After,
    IReadOnlyList<CursorSortValue> SortValues);

internal readonly record struct CursorSortValue(SortFieldType Type, double Numeric, long Int64, string? String)
{
    internal static CursorSortValue FromNumeric(SortFieldType type, double value) => new(type, value, 0, null);
    internal static CursorSortValue FromInt64(SortFieldType type, long value) => new(type, 0, value, null);
    internal static CursorSortValue FromString(string value) => new(SortFieldType.String, 0, 0, value);
}

internal sealed class SearchCursorCodec
{
    private const int Version = 1;
    private readonly int _maximumBytes;
    private readonly byte[]? _key;

    internal SearchCursorCodec(int maximumBytes, byte[]? key) { _maximumBytes = maximumBytes; _key = key?.ToArray(); }

    internal string Encode(SearchCursorData cursor)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x4353434c); writer.Write(Version);
            WriteString(writer, cursor.SessionId); WriteString(writer, cursor.IndexIdentity);
            writer.Write(cursor.Generation); WriteString(writer, cursor.QueryIdentity);
            WriteString(writer, cursor.SortIdentity); WriteString(writer, cursor.RankingIdentity);
            writer.Write(cursor.After.DocId); writer.Write(BitConverter.SingleToInt32Bits(cursor.After.Score));
            writer.Write(cursor.SortValues.Count);
            foreach (var value in cursor.SortValues)
            {
                writer.Write((byte)value.Type);
                switch (value.Type)
                {
                    case SortFieldType.Score or SortFieldType.Numeric: writer.Write(BitConverter.DoubleToInt64Bits(value.Numeric)); break;
                    case SortFieldType.DocId or SortFieldType.Int64: writer.Write(value.Int64); break;
                    case SortFieldType.String: WriteString(writer, value.String ?? string.Empty); break;
                    default: throw Invalid("Unsupported cursor sort value.");
                }
            }
        }
        var payload = stream.ToArray();
        if (payload.Length > _maximumBytes) throw Invalid("Cursor payload exceeds the configured size limit.");
        string encoded = Base64Url(payload);
        if (_key is null) return encoded;
        return encoded + "." + Base64Url(HMACSHA256.HashData(_key, payload));
    }

    internal SearchCursorData Decode(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > checked(_maximumBytes * 2)) throw Invalid("Cursor token is empty or oversized.");
        var parts = token.Split('.');
        if (parts.Length != (_key is null ? 1 : 2)) throw Invalid("Cursor integrity format is invalid.");
        byte[] payload;
        try { payload = FromBase64Url(parts[0]); } catch (FormatException) { throw Invalid("Cursor encoding is invalid."); }
        if (payload.Length > _maximumBytes) throw Invalid("Cursor payload exceeds the configured size limit.");
        if (_key is not null)
        {
            byte[] supplied;
            try { supplied = FromBase64Url(parts[1]); } catch (FormatException) { throw Integrity(); }
            var expected = HMACSHA256.HashData(_key, payload);
            if (supplied.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(supplied, expected)) throw Integrity();
        }
        try
        {
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadInt32() != 0x4353434c || reader.ReadInt32() != Version) throw Invalid("Cursor version is unsupported.");
            string session = ReadString(reader), index = ReadString(reader); int generation = reader.ReadInt32();
            string query = ReadString(reader), sort = ReadString(reader), ranking = ReadString(reader);
            int docId = reader.ReadInt32(); float score = BitConverter.Int32BitsToSingle(reader.ReadInt32());
            if (docId < 0 || !float.IsFinite(score)) throw Invalid("Cursor boundary is invalid.");
            int count = reader.ReadInt32(); if (count < 1 || count > 32) throw Invalid("Cursor sort value count is invalid.");
            var values = new CursorSortValue[count];
            for (int i = 0; i < count; i++)
            {
                var type = (SortFieldType)reader.ReadByte();
                values[i] = type switch
                {
                    SortFieldType.Score or SortFieldType.Numeric => ReadNumeric(reader, type),
                    SortFieldType.DocId or SortFieldType.Int64 => CursorSortValue.FromInt64(type, reader.ReadInt64()),
                    SortFieldType.String => CursorSortValue.FromString(ReadString(reader)),
                    _ => throw Invalid("Cursor sort value type is invalid.")
                };
            }
            if (stream.Position != stream.Length) throw Invalid("Cursor contains trailing data.");
            return new SearchCursorData(session, index, generation, query, sort, ranking, new ScoreDoc(docId, score), values);
        }
        catch (SearchSessionException) { throw; }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or DecoderFallbackException or ArgumentException)
        { throw Invalid("Cursor payload is malformed."); }
    }

    private static CursorSortValue ReadNumeric(BinaryReader reader, SortFieldType type)
    { double value = BitConverter.Int64BitsToDouble(reader.ReadInt64()); if (!double.IsFinite(value)) throw Invalid("Non-finite cursor sort values are unsupported."); return CursorSortValue.FromNumeric(type, value); }
    private static void WriteString(BinaryWriter writer, string value) { if (Encoding.UTF8.GetByteCount(value) > 2048) throw Invalid("Cursor string value is oversized."); writer.Write(value); }
    private static string ReadString(BinaryReader reader) { string value = reader.ReadString(); if (Encoding.UTF8.GetByteCount(value) > 2048) throw Invalid("Cursor string value is oversized."); return value; }
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromBase64Url(string value) { string padded = value.Replace('-', '+').Replace('_', '/'); padded += new string('=', (4 - padded.Length % 4) % 4); return Convert.FromBase64String(padded); }
    private static SearchSessionException Invalid(string message) => new(SearchSessionFailureReason.InvalidCursor, message);
    private static SearchSessionException Integrity() => new(SearchSessionFailureReason.IntegrityFailure, "Cursor integrity validation failed.");
}
