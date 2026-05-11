using System.Text.Json.Serialization;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Serialization;

[JsonSerializable(typeof(CommitData))]
[JsonSerializable(typeof(SegmentInfo))]
[JsonSerializable(typeof(VectorFieldInfo))]
[JsonSerializable(typeof(IndexStatsDto))]
[JsonSerializable(typeof(SegmentStatsDto))]
[JsonSerializable(typeof(SearchEvent))]
[JsonSerializable(typeof(SlowQueryEntry))]
[JsonSerializable(typeof(IndexMigrationMarker))]
[JsonSerializable(typeof(IndexCodecMigrationAction))]
[JsonSerializable(typeof(IndexBackupManifest))]
[JsonSerializable(typeof(IndexBackupFileEntry))]
internal sealed partial class LeanCorpusJsonContext : JsonSerializerContext;
