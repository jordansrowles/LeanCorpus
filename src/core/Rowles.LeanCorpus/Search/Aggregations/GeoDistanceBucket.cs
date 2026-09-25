using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>One Geo distance bucket.</summary>
public readonly record struct GeoDistanceBucket(double? FromMetres, double? ToMetres, long DocumentCount);

