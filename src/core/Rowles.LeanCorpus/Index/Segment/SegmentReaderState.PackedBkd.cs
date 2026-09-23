using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

namespace Rowles.LeanCorpus.Index.Segment;

internal sealed partial class SegmentReaderState
{
    private PackedBkdReader? _packedBkdReader;
    private bool _packedBkdReaderLoaded;

    internal IReadOnlyList<string> GetPackedBkdFieldNames()
    {
        PackedBkdReader? reader = EnsurePackedBkdReader();
        return reader is null ? Array.Empty<string>() : reader.FieldNames.ToArray();
    }

    internal bool TryGetPackedBkdFieldMetadata(string field, out PackedBkdFieldMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(field);
        PackedBkdReader? reader = EnsurePackedBkdReader();
        if (reader is null || !reader.HasField(field))
        {
            metadata = default;
            return false;
        }

        metadata = reader.GetFieldMetadata(field);
        return true;
    }

    internal bool IntersectPackedBkd<TVisitor>(string field, ref TVisitor visitor)
        where TVisitor : struct, IPackedBkdIntersectVisitor
    {
        ArgumentNullException.ThrowIfNull(field);
        PackedBkdReader? reader = EnsurePackedBkdReader();
        return reader is not null && reader.Intersect(field, ref visitor);
    }

    internal void DeepValidatePackedBkd()
        => EnsurePackedBkdReader()?.DeepValidate();

    internal void ValidatePackedBkdChecksum()
        => EnsurePackedBkdReader()?.ValidateChecksum();

    private PackedBkdReader? EnsurePackedBkdReader()
    {
        if (Volatile.Read(ref _packedBkdReaderLoaded))
            return _packedBkdReader;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_packedBkdReaderLoaded)
                return _packedBkdReader;

            if (!_files.Exists(".pbkd"))
            {
                Volatile.Write(ref _packedBkdReaderLoaded, true);
                return null;
            }

            PackedBkdReader reader = PackedBkdReader.Open(_files.OpenInput(".pbkd"));
            _packedBkdReader = reader;
            Volatile.Write(ref _packedBkdReaderLoaded, true);
            return reader;
        }
    }
}
