using System.Text.Json.Serialization;
using Rowles.LeanLucene.Diagnostics;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Index.Segment;
using Rowles.LeanLucene.Search.Scoring;

namespace Rowles.LeanLucene.Serialization;

[JsonSerializable(typeof(CommitData))]
[JsonSerializable(typeof(SegmentInfo))]
[JsonSerializable(typeof(VectorFieldInfo))]
[JsonSerializable(typeof(IndexStatsDto))]
[JsonSerializable(typeof(SegmentStatsDto))]
[JsonSerializable(typeof(SearchEvent))]
[JsonSerializable(typeof(SlowQueryEntry))]
internal sealed partial class LeanLuceneJsonContext : JsonSerializerContext;
