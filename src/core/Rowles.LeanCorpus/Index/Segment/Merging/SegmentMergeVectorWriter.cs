using Rowles.LeanCorpus.Codecs.Vectors;
using MergeDocument = Rowles.LeanCorpus.Index.Segment.Merging.SegmentMergeDocumentMap.MergeDocument;

namespace Rowles.LeanCorpus.Index.Segment.Merging;

/// <summary>
/// Owns vector remapping and encoding during merges. Merged segments never carry historical
/// <c>.hnsw</c> graphs; when enabled, each merged Float32 field receives a Float32 vector graph
/// rebuilt from scratch over the merged <c>.vec</c> file (ADR052).
/// </summary>
internal sealed class SegmentMergeVectorWriter
{
    /// <summary>Vector graph parameters for a merge that rebuilds graphs.</summary>
    internal sealed record GraphSettings(
        Codecs.Hnsw.HnswBuildConfig Config,
        long Seed,
        Diagnostics.IMetricsCollector Metrics,
        CancellationToken CancellationToken);

    internal sealed class State
    {
        internal int TotalDocs { get; }
        internal Dictionary<string, List<int>> VectorFieldDocIds { get; } = new(StringComparer.Ordinal);

        internal State(int totalDocs) => TotalDocs = totalDocs;
    }

    internal static void AccumulateSource(SegmentMergeDocumentMap.Source source, State ctx)
    {
        var (segInfo, docIdMap, reader) = source;
        Dictionary<string, VectorFieldInfo>? vectorFieldByName = null;
        if (reader.HasVectors)
        {
            vectorFieldByName = new Dictionary<string, VectorFieldInfo>(reader.Info.VectorFields.Count, StringComparer.Ordinal);
            foreach (var vf in reader.Info.VectorFields)
                vectorFieldByName[vf.FieldName] = vf;
        }

        if (reader.HasVectors)
        {
            foreach (string vfName in reader.VectorFieldNames)
            {
                if (vectorFieldByName is null || !vectorFieldByName.ContainsKey(vfName))
                    throw new InvalidDataException($"Vector field '{vfName}' has no segment metadata during merge.");

                List<int>? vectorDocIds = null;

                for (int oldDocId = 0; oldDocId < segInfo.DocCount; oldDocId++)
                {
                    int remapDocId = docIdMap[oldDocId];
                    if (remapDocId < 0 || !reader.HasVector(vfName, oldDocId)) continue;

                    if (vectorDocIds is null)
                    {
                        if (!ctx.VectorFieldDocIds.TryGetValue(vfName, out vectorDocIds))
                        {
                            vectorDocIds = new List<int>();
                            ctx.VectorFieldDocIds.Add(vfName, vectorDocIds);
                        }
                    }

                    vectorDocIds.Add(remapDocId);
                }
            }
        }
    }

    internal List<VectorFieldInfo> Write(SegmentMergePlan plan, State ctx, GraphSettings? graphs = null)
    {
        VectorCodecFiles.EnsureCanCreate(plan.VectorQuantisation);
        return MergeVectors(ctx, plan.DocumentMap.Documents, plan.BasePath, plan.VectorContracts, graphs);
    }

    private List<VectorFieldInfo> MergeVectors(
        State ctx,
        IReadOnlyList<MergeDocument> documentOrder,
        string basePath,
        IReadOnlyDictionary<string, VectorFieldContract> vectorContracts,
        GraphSettings? graphs)
    {
        var merged = new List<VectorFieldInfo>();
        foreach (var (fieldName, vectorDocIds) in ctx.VectorFieldDocIds)
        {
            if (vectorDocIds.Count == 0) continue;
            if (!vectorContracts.TryGetValue(fieldName, out VectorFieldContract contract))
                throw new InvalidOperationException(
                    $"Cannot determine the vector contract for field '{fieldName}' during merge. Source segments must declare this field.");

            int dimension = contract.Dimension;
            bool normalised = contract.Normalised;
            string vecPath = Codecs.Vectors.VectorFilePaths.VectorFile(basePath, fieldName);
            var mergedSource = new MergedDocumentVectorSource(documentOrder, fieldName, dimension);
            VectorWriter.WriteField(vecPath, ctx.TotalDocs, dimension, mergedSource, vectorDocIds);
            bool hasGraph = false;
            if (graphs is not null && vectorDocIds.Count >= 2)
            {
                if (!Codecs.Hnsw.VectorGraphBuilder.CanBuild(vectorDocIds.Count, graphs.Config))
                    graphs.Metrics.RecordVectorGraphBuildSkipped(Diagnostics.VectorGraphBuildSkipReason.TooLarge);
                else
                    hasGraph = BuildGraph(vecPath, Codecs.Vectors.VectorFilePaths.GraphFile(basePath, fieldName), vectorDocIds, normalised, graphs);
            }
            merged.Add(new VectorFieldInfo
            {
                FieldName = fieldName,
                Dimension = dimension,
                Normalised = normalised,
                Quantisation = VectorQuantisation.None,
                HasHnsw = false,
                HasGraph = hasGraph,
            });
        }
        return merged;
    }

    /// <summary>
    /// Rebuilds the field's graph from the merged vector file just written, so memory stays bounded
    /// by the mapped vectors plus the graph. The graph is written before segment publication.
    /// Returns false, writing nothing, when the built graph is refused.
    /// </summary>
    private static bool BuildGraph(string vecPath, string graphPath, List<int> vectorDocIds, bool normalised, GraphSettings graphs)
    {
        int[] docIds = vectorDocIds.ToArray();
        Array.Sort(docIds);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var vectors = VectorReader.Open(vecPath);
        // Cancellable between insertions, so writer disposal does not wait out a whole build.
        using var graph = Codecs.Hnsw.VectorGraphBuilder.TryBuild(
            new VectorReaderSource(vectors), docIds, graphs.Config, graphs.Seed, normalised, graphs.CancellationToken);
        // A graph that repair could not make fully reachable is not published; the field is searched exactly.
        if (graph is null)
        {
            graphs.Metrics.RecordVectorGraphBuildSkipped(Diagnostics.VectorGraphBuildSkipReason.Unreachable);
            return false;
        }
        Codecs.Hnsw.VectorGraphWriter.Write(graphPath, graph);
        stopwatch.Stop();
        graphs.Metrics.RecordHnswBuild(stopwatch.Elapsed, docIds.Length);
        return true;
    }

    private sealed class MergedDocumentVectorSource : IVectorSource
    {
        private readonly IReadOnlyList<MergeDocument> _documents;
        private readonly string _fieldName;
        private float[]? _zeroVector;

        internal MergedDocumentVectorSource(IReadOnlyList<MergeDocument> documents, string fieldName, int dimension)
        {
            _documents = documents;
            _fieldName = fieldName;
            Dimension = dimension;
        }

        public int Dimension { get; }
        public int Count => _documents.Count;

        public ReadOnlySpan<float> GetVector(int docId)
        {
            MergeDocument document = GetDocument(docId);
            return document.Reader.GetVector(_fieldName, document.OldDocId) ?? (_zeroVector ??= new float[Dimension]);
        }

        public void CopyVectorTo(int docId, Span<float> destination)
        {
            if (destination.Length != Dimension)
                throw new ArgumentException($"Destination length {destination.Length} != vector dimension {Dimension}.", nameof(destination));
            MergeDocument document = GetDocument(docId);
            if (!document.Reader.TryCopyVectorTo(_fieldName, document.OldDocId, destination))
                destination.Clear();
        }

        private MergeDocument GetDocument(int docId)
        {
            if ((uint)docId >= (uint)_documents.Count)
                throw new ArgumentOutOfRangeException(nameof(docId));
            return _documents[docId];
        }
    }

}
