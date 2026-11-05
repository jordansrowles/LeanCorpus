using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Fixtures;

/// <summary>Attaches legacy graph files to test indexes without invoking the retired product writer path.</summary>
internal static class HistoricalHnswGraphFixtures
{
    internal static SegmentInfo[] AttachToFloat32Index(
        string directoryPath,
        HnswBuildConfig? config = null,
        long seed = 42,
        bool compound = false)
    {
        using var directory = new MMapDirectory(directoryPath);
        SegmentInfo[] segments = IndexRecovery.RecoverLatestCommit(directoryPath, cleanupOrphans: false)!
            .SegmentInfos
            .OrderBy(static segment => segment.SegmentId, StringComparer.Ordinal)
            .ToArray();

        config ??= new HnswBuildConfig { M = 4, M0 = 8, EfConstruction = 16 };
        foreach (SegmentInfo segment in segments)
        {
            if (segment.IsCompoundFile)
                throw new InvalidOperationException("Historical graph fixtures must be attached before compound packing.");

            using (var reader = new SegmentReader(directory, segment))
            {
                for (int fieldIndex = 0; fieldIndex < segment.VectorFields.Count; fieldIndex++)
                {
                    VectorFieldInfo field = segment.VectorFields[fieldIndex];
                    var vectors = new Dictionary<int, ReadOnlyMemory<float>>();
                    for (int docId = 0; docId < reader.MaxDoc; docId++)
                        if (reader.GetVector(field.FieldName, docId) is { } vector)
                            vectors.Add(docId, vector);

                    if (vectors.Count == 0)
                        continue;

                    var source = new InMemoryVectorSource(vectors, field.Dimension);
                    using HnswGraph graph = HnswGraphBuilder.Build(
                        source,
                        vectors.Keys.Order().ToArray(),
                        config,
                        seed);
                    HnswWriter.Write(
                        VectorFilePaths.HnswFile(Path.Combine(directoryPath, segment.SegmentId), field.FieldName),
                        graph,
                        field.Dimension,
                        field.Normalised);

                    segment.VectorFields[fieldIndex] = new VectorFieldInfo
                    {
                        FieldName = field.FieldName,
                        Dimension = field.Dimension,
                        Normalised = field.Normalised,
                        HasHnsw = true,
                        Quantisation = field.Quantisation,
                    };
                }
            }

            if (compound)
                segment.IsCompoundFile = SegmentFileSet.Pack(directoryPath, segment.SegmentId);
            SegmentFlusher.RefreshSegmentSize(segment, directoryPath);
        }

        return segments;
    }
}
