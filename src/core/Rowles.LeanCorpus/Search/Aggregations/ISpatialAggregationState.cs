using System.Buffers;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Search.Aggregations;

internal interface ISpatialAggregationState
{
    string Name { get; }
    string Field { get; }
    /// <summary>Collects the current document of <paramref name="document"/>, whose decoded values are shared by every state.</summary>
    void Collect(SpatialDocumentValues document);
    ISearchAggregationResult Finish();
}
