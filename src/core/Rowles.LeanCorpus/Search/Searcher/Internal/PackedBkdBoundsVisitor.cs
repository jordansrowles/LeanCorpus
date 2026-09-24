using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Search.Searcher.Internal;

/// <summary>Collects unique documents whose 2D packed values lie inside inclusive encoded bounds.</summary>
internal struct PackedBkdBoundsVisitor : IPackedBkdIntersectVisitor
{
    private const int PackedPointLength = 2 * PackedBkdConfig.FixedBytesPerDimension;
    private readonly byte[] _minimum;
    private readonly byte[] _maximum;
    private readonly RoaringBitmap _matchedDocuments;
    private readonly List<int> _documents;

    internal PackedBkdBoundsVisitor(
        ReadOnlySpan<byte> minimum,
        ReadOnlySpan<byte> maximum,
        RoaringBitmap matchedDocuments,
        List<int> documents)
    {
        if (minimum.Length != PackedPointLength)
            throw new ArgumentException("Packed point bounds must contain exactly two dimensions.", nameof(minimum));
        if (maximum.Length != PackedPointLength)
            throw new ArgumentException("Packed point bounds must contain exactly two dimensions.", nameof(maximum));
        for (int dimensionOffset = 0; dimensionOffset < PackedPointLength; dimensionOffset += PackedBkdConfig.FixedBytesPerDimension)
        {
            if (minimum.Slice(dimensionOffset, PackedBkdConfig.FixedBytesPerDimension)
                .SequenceCompareTo(maximum.Slice(dimensionOffset, PackedBkdConfig.FixedBytesPerDimension)) > 0)
                throw new ArgumentException("Each packed minimum must not exceed its corresponding maximum.", nameof(minimum));
        }

        ArgumentNullException.ThrowIfNull(matchedDocuments);
        ArgumentNullException.ThrowIfNull(documents);
        _minimum = minimum.ToArray();
        _maximum = maximum.ToArray();
        _matchedDocuments = matchedDocuments;
        _documents = documents;
    }

    public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
    {
        if (minimum.Length != PackedPointLength || maximum.Length != PackedPointLength)
            throw new InvalidDataException("A 2D packed point query received incompatible cell bounds.");

        bool containsCell = true;
        for (int dimensionOffset = 0; dimensionOffset < PackedPointLength; dimensionOffset += PackedBkdConfig.FixedBytesPerDimension)
        {
            ReadOnlySpan<byte> queryMinimum = _minimum.AsSpan(dimensionOffset, PackedBkdConfig.FixedBytesPerDimension);
            ReadOnlySpan<byte> queryMaximum = _maximum.AsSpan(dimensionOffset, PackedBkdConfig.FixedBytesPerDimension);
            ReadOnlySpan<byte> cellMinimum = minimum.Slice(dimensionOffset, PackedBkdConfig.FixedBytesPerDimension);
            ReadOnlySpan<byte> cellMaximum = maximum.Slice(dimensionOffset, PackedBkdConfig.FixedBytesPerDimension);

            if (cellMaximum.SequenceCompareTo(queryMinimum) < 0
                || cellMinimum.SequenceCompareTo(queryMaximum) > 0)
                return PackedBkdCellRelation.Outside;

            if (queryMinimum.SequenceCompareTo(cellMinimum) > 0
                || queryMaximum.SequenceCompareTo(cellMaximum) < 0)
                containsCell = false;
        }

        return containsCell ? PackedBkdCellRelation.Inside : PackedBkdCellRelation.Crosses;
    }

    public void Visit(int docId)
        => AddDocument(docId);

    public void Visit(int docId, ReadOnlySpan<byte> packedValue)
    {
        if (packedValue.Length != PackedPointLength)
            throw new InvalidDataException("A 2D packed point query received an incompatible packed value.");

        for (int dimensionOffset = 0; dimensionOffset < PackedPointLength; dimensionOffset += PackedBkdConfig.FixedBytesPerDimension)
        {
            ReadOnlySpan<byte> coordinate = packedValue.Slice(dimensionOffset, PackedBkdConfig.FixedBytesPerDimension);
            if (coordinate.SequenceCompareTo(_minimum.AsSpan(dimensionOffset, PackedBkdConfig.FixedBytesPerDimension)) < 0
                || coordinate.SequenceCompareTo(_maximum.AsSpan(dimensionOffset, PackedBkdConfig.FixedBytesPerDimension)) > 0)
                return;
        }

        AddDocument(docId);
    }

    private void AddDocument(int docId)
    {
        if (_matchedDocuments.Contains(docId))
            return;

        _matchedDocuments.Add(docId);
        _documents.Add(docId);
    }
}
