using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Searcher;

public sealed partial class IndexSearcher
{
    private void ExecuteGeoShapeQuery(GeoShapeQuery query, SegmentReader reader, ref TopNCollector collector)
        => CollectShapeMatches(
            query.Field,
            SpatialFieldKind.GeoShape,
            query.Relation,
            PreparedShapeQuery.Prepare(query),
            query.Boost,
            reader,
            ref collector);

    private void ExecuteXYShapeQuery(XYShapeQuery query, SegmentReader reader, ref TopNCollector collector)
        => CollectShapeMatches(
            query.Field,
            SpatialFieldKind.XYShape,
            query.Relation,
            PreparedShapeQuery.Prepare(query),
            query.Boost,
            reader,
            ref collector);

    private static void AddShapeSubQueryResults(
        List<ScoreDoc> results,
        SegmentReader reader,
        string field,
        SpatialFieldKind fieldKind,
        SpatialRelation relation,
        PreparedShapeQuery query,
        float boost)
    {
        foreach (int docId in CollectShapeDocuments(reader, field, fieldKind, relation, query))
            if (reader.IsLive(docId))
                results.Add(new ScoreDoc(docId, ApplyFieldBoost(reader, docId, field, boost)));
    }

    private static void CollectShapeMatches(
        string field,
        SpatialFieldKind fieldKind,
        SpatialRelation relation,
        PreparedShapeQuery query,
        float boost,
        SegmentReader reader,
        ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        foreach (int docId in CollectShapeDocuments(reader, field, fieldKind, relation, query))
            if (reader.IsLive(docId))
                collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, field, boost));
    }

    private static List<int> CollectShapeDocuments(
        SegmentReader reader,
        string field,
        SpatialFieldKind fieldKind,
        SpatialRelation relation,
        PreparedShapeQuery query)
    {
        SpatialFieldInfo? spatialInfo = null;
        foreach (SpatialFieldInfo candidate in reader.Info.SpatialFields)
        {
            if (string.Equals(candidate.FieldName, field, StringComparison.Ordinal))
            {
                spatialInfo = candidate;
                break;
            }
        }

        if (spatialInfo is not null && spatialInfo.Kind != fieldKind)
            throw new InvalidDataException(
                $"Spatial field '{field}' is stored as '{spatialInfo.Kind}' and cannot be queried as '{fieldKind}'.");

        if (!reader.TryGetPackedBkdFieldMetadata(field, out PackedBkdFieldMetadata metadata))
            return [];

        if (metadata.Config.Dimensions != 7
            || metadata.Config.IndexedDimensions != 4
            || metadata.Config.BytesPerDimension != PackedBkdConfig.FixedBytesPerDimension)
            throw new InvalidDataException(
                $"Packed BKD field '{field}' does not use the 7D/4-indexed shape layout.");
        if (spatialInfo is null)
            throw new InvalidDataException(
                $"Packed BKD field '{field}' has no persisted spatial kind and cannot be interpreted as a shape.");

        var visitor = new SpatialShapeVisitor(query, relation, fieldKind);
        _ = reader.IntersectPackedBkd(field, ref visitor);
        return visitor.GetMatchingDocuments();
    }
}
