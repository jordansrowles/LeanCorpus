using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>Collects document-level shape relations from the shape values in a Packed BKD field.</summary>
internal struct SpatialShapeVisitor : IPackedBkdIntersectVisitor
{
    private readonly PreparedShapeQuery _query;
    private readonly SpatialRelation _relation;
    private readonly SpatialFieldKind _fieldKind;
    private readonly HashSet<int> _intersectingDocuments;
    private readonly Dictionary<int, bool> _documentState;
    private readonly Dictionary<(int DocumentId, uint ValueOrdinal), List<ShapePrimitive>> _values;
    private bool _bulkDecision;
    private bool _hasBulkDecision;

    internal SpatialShapeVisitor(
        PreparedShapeQuery query,
        SpatialRelation relation,
        SpatialFieldKind fieldKind)
    {
        _query = query;
        _relation = relation;
        _fieldKind = fieldKind;
        _intersectingDocuments = [];
        _documentState = [];
        _values = [];
    }

    public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
    {
        if (minimum.Length != 4 * PackedBkdConfig.FixedBytesPerDimension
            || maximum.Length != 4 * PackedBkdConfig.FixedBytesPerDimension)
            throw new InvalidDataException("A shape query received incompatible Packed BKD cell bounds.");

        double minY = ShapePrimitiveCodec.DecodeYKey(ReadKey(minimum, 0), _fieldKind);
        double minX = ShapePrimitiveCodec.DecodeXKey(ReadKey(minimum, 1), _fieldKind);
        double maxY = ShapePrimitiveCodec.DecodeYKey(ReadKey(maximum, 2), _fieldKind);
        double maxX = ShapePrimitiveCodec.DecodeXKey(ReadKey(maximum, 3), _fieldKind);
        var envelope = new SpatialEnvelope(minX, minY, maxX, maxY);
        _hasBulkDecision = false;
        switch (_relation)
        {
            case SpatialRelation.Intersects:
                if (_query.IsOutside(envelope))
                    return PackedBkdCellRelation.Outside;
                if (_query.ContainsEnvelope(envelope))
                {
                    _bulkDecision = true;
                    _hasBulkDecision = true;
                    return PackedBkdCellRelation.Inside;
                }
                break;
            case SpatialRelation.Within:
                if (_query.ContainsEnvelope(envelope))
                {
                    _bulkDecision = true;
                    _hasBulkDecision = true;
                    return PackedBkdCellRelation.Inside;
                }
                break;
            case SpatialRelation.Disjoint:
                if (_query.IsOutside(envelope))
                {
                    _bulkDecision = true;
                    _hasBulkDecision = true;
                    return PackedBkdCellRelation.Inside;
                }
                if (_query.ContainsEnvelope(envelope))
                {
                    _bulkDecision = false;
                    _hasBulkDecision = true;
                    return PackedBkdCellRelation.Inside;
                }
                break;
        }
        return PackedBkdCellRelation.Crosses;
    }

    public void Visit(int docId)
    {
        if (!_hasBulkDecision)
            throw new InvalidDataException("A shape query received a document-only Packed BKD visit without a safe bulk decision.");
        if (_relation == SpatialRelation.Intersects)
        {
            if (_bulkDecision)
                _intersectingDocuments.Add(docId);
            return;
        }

        if (!_documentState.TryGetValue(docId, out bool current))
            current = true;
        _documentState[docId] = current && _bulkDecision;
    }

    public void Visit(int docId, ReadOnlySpan<byte> packedValue)
    {
        ShapePrimitive primitive = ShapePrimitiveCodec.Decode(packedValue, _fieldKind);
        switch (_relation)
        {
            case SpatialRelation.Intersects:
                if (_query.Intersects(primitive))
                    _intersectingDocuments.Add(docId);
                break;
            case SpatialRelation.Within:
                if (!_documentState.TryGetValue(docId, out bool within))
                    within = true;
                _documentState[docId] = within && _query.Covers(primitive);
                break;
            case SpatialRelation.Disjoint:
                if (!_documentState.TryGetValue(docId, out bool disjoint))
                    disjoint = true;
                _documentState[docId] = disjoint && !_query.Intersects(primitive);
                break;
            case SpatialRelation.Contains:
                var key = (docId, primitive.ValueOrdinal);
                if (!_values.TryGetValue(key, out List<ShapePrimitive>? value))
                    _values.Add(key, value = []);
                value.Add(primitive);
                break;
            default:
                throw new InvalidDataException($"Unknown spatial relation '{_relation}'.");
        }
    }

    internal List<int> GetMatchingDocuments()
    {
        HashSet<int> matches = _relation switch
        {
            SpatialRelation.Intersects => _intersectingDocuments,
            SpatialRelation.Within or SpatialRelation.Disjoint => _documentState
                .Where(static pair => pair.Value)
                .Select(static pair => pair.Key)
                .ToHashSet(),
            SpatialRelation.Contains => GetContainingDocuments(),
            _ => throw new InvalidDataException($"Unknown spatial relation '{_relation}'."),
        };
        return matches.Order().ToList();
    }

    private HashSet<int> GetContainingDocuments()
    {
        var matches = new HashSet<int>();
        foreach (KeyValuePair<(int DocumentId, uint ValueOrdinal), List<ShapePrimitive>> item in _values)
        {
            if (ValueContainsQuery(item.Value))
                matches.Add(item.Key.DocumentId);
        }
        return matches;
    }

    private bool ValueContainsQuery(IReadOnlyList<ShapePrimitive> indexedValue)
        => _query.WithinRelation(indexedValue) == SpatialWithinRelation.Candidate;

    private static uint ReadKey(ReadOnlySpan<byte> packed, int dimension)
        => BinaryPrimitives.ReadUInt32BigEndian(
            packed.Slice(dimension * PackedBkdConfig.FixedBytesPerDimension, PackedBkdConfig.FixedBytesPerDimension));
}
