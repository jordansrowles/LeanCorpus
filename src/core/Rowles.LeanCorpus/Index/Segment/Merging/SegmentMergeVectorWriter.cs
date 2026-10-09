using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Store;
using MergeDocument = Rowles.LeanCorpus.Index.Segment.Merging.SegmentMergeDocumentMap.MergeDocument;

namespace Rowles.LeanCorpus.Index.Segment.Merging;

/// <summary>Owns vector remapping, encoding and HNSW rebuild output.</summary>
internal sealed class SegmentMergeVectorWriter
{
    private readonly MMapDirectory _directory;
    private readonly HnswBuildConfig _hnswBuildConfig;
    private readonly Diagnostics.IMetricsCollector _metrics;

    internal SegmentMergeVectorWriter(MMapDirectory directory, HnswBuildConfig config, Diagnostics.IMetricsCollector metrics)
    {
        _directory = directory;
        _hnswBuildConfig = config;
        _metrics = metrics;
    }

    internal sealed class State
    {
        internal int TotalDocs { get; }
        internal Dictionary<string, List<int>> VectorFieldDocIds { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, bool> VectorFieldHadHnsw { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, List<(SegmentInfo Seg, Dictionary<int, int> OldToNew, SegmentReader Reader)>> VectorFieldRemaps { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, Dictionary<SegmentInfo, Dictionary<int, int>>> VectorFieldRemapsBySource { get; } = new(StringComparer.Ordinal);

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
                if (vectorFieldByName is null || !vectorFieldByName.TryGetValue(vfName, out var match))
                    throw new InvalidDataException($"Vector field '{vfName}' has no segment metadata during merge.");

                List<int>? vectorDocIds = null;
                Dictionary<int, int>? oldToNew = null;

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

                        if (!ctx.VectorFieldRemaps.TryGetValue(vfName, out var remapList))
                        {
                            remapList = new List<(SegmentInfo, Dictionary<int, int>, SegmentReader)>();
                            ctx.VectorFieldRemaps.Add(vfName, remapList);
                        }
                        if (!ctx.VectorFieldRemapsBySource.TryGetValue(vfName, out var remapsBySource))
                        {
                            remapsBySource = new Dictionary<SegmentInfo, Dictionary<int, int>>(ReferenceEqualityComparer.Instance);
                            ctx.VectorFieldRemapsBySource.Add(vfName, remapsBySource);
                        }
                        if (!remapsBySource.TryGetValue(segInfo, out oldToNew))
                        {
                            oldToNew = new Dictionary<int, int>();
                            remapsBySource.Add(segInfo, oldToNew);
                            remapList.Add((segInfo, oldToNew, reader));
                        }
                    }

                    vectorDocIds!.Add(remapDocId);
                    oldToNew![oldDocId] = remapDocId;
                }

                if (vectorDocIds is not null)
                    ctx.VectorFieldHadHnsw[vfName] = ctx.VectorFieldHadHnsw.GetValueOrDefault(vfName, false) || match.HasHnsw;
            }
        }
    }

    internal List<VectorFieldInfo> Write(SegmentMergePlan plan, State ctx)
        => MergeVectors(ctx, plan.DocumentMap.Documents, plan.BasePath, plan.VectorContracts, plan.VectorQuantisation);

    private List<VectorFieldInfo> MergeVectors(
        State ctx,
        IReadOnlyList<MergeDocument> documentOrder,
        string basePath,
        IReadOnlyDictionary<string, VectorFieldContract> vectorContracts,
        VectorQuantisation destinationVectorQuantisation)
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
            VectorQuantisation quantisation = destinationVectorQuantisation;
            bool shouldBuildHnsw = ctx.VectorFieldHadHnsw.GetValueOrDefault(fieldName, false)
                && vectorDocIds.Count >= 2;
            string vecPath = Codecs.Vectors.VectorFilePaths.VectorFile(basePath, fieldName);
            var mergedSource = new MergedDocumentVectorSource(documentOrder, fieldName, dimension);
            bool hasHnsw = false;

            if (quantisation == VectorQuantisation.None)
            {
                VectorWriter.WriteField(vecPath, ctx.TotalDocs, dimension, mergedSource, vectorDocIds);
                if (shouldBuildHnsw)
                {
                    using var vectorReader = VectorReader.Open(vecPath);
                    hasHnsw = BuildAndWriteMergedHnsw(
                        fieldName,
                        basePath,
                        dimension,
                        normalised,
                        quantisation,
                        new VectorReaderSource(vectorReader),
                        vectorDocIds,
                        ctx.VectorFieldRemaps);
                }
            }
            else
            {
                var vqPath = Codecs.Vectors.VectorFilePaths.QuantisedVectorFile(basePath, fieldName);
                string vecFileName = Path.GetFileName(vecPath);
                try
                {
                    VectorWriter.WriteField(vecPath, ctx.TotalDocs, dimension, mergedSource, vectorDocIds);
                    using (var vectorReader = VectorReader.Open(vecPath))
                    {
                        var vectorSource = new VectorReaderSource(vectorReader);
                        switch (quantisation)
                        {
                            case VectorQuantisation.Int8:
                                QuantisedVectorWriter.WriteInt8(
                                    vqPath, ctx.TotalDocs, dimension, vectorSource, vectorDocIds);
                                break;
                            case VectorQuantisation.BBQ:
                                QuantisedVectorWriter.WriteBBQ(
                                    vqPath, ctx.TotalDocs, dimension, vectorSource, vectorDocIds);
                                break;
                            default:
                                throw new InvalidDataException(
                                    $"Unsupported vector quantisation '{quantisation}' during merge of field '{fieldName}'.");
                        }
                    }

                    if (shouldBuildHnsw)
                    {
                        using var quantisedReader = QuantisedVectorReader.Open(vqPath);
                        hasHnsw = BuildAndWriteMergedHnsw(
                            fieldName,
                            basePath,
                            dimension,
                            normalised,
                            quantisation,
                            new QuantisedVectorSource(quantisedReader),
                            vectorDocIds,
                            ctx.VectorFieldRemaps);
                    }
                }
                finally
                {
                    if (_directory.FileExists(vecFileName))
                        _directory.DeleteFile(vecFileName);
                }
            }

            merged.Add(new VectorFieldInfo
            {
                FieldName = fieldName,
                Dimension = dimension,
                Normalised = normalised,
                Quantisation = quantisation,
                HasHnsw = hasHnsw,
            });
        }
        return merged;
    }

    private bool BuildAndWriteMergedHnsw(
        string fieldName,
        string basePath,
        int dimension,
        bool normalised,
        VectorQuantisation destinationVectorQuantisation,
        IVectorSource vectorSource,
        IReadOnlyList<int> vectorDocIds,
        IReadOnlyDictionary<string, List<(SegmentInfo Seg, Dictionary<int, int> OldToNew, SegmentReader Reader)>> vectorFieldRemaps)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        HnswGraph? graph = null;
        try
        {
            if (vectorFieldRemaps.TryGetValue(fieldName, out var remapList) && remapList.Count > 0)
            {
                var seed = remapList
                    .Where(entry => entry.Seg.VectorFields.Any(field =>
                        field.FieldName == fieldName
                        && field.HasHnsw
                        && field.Dimension == dimension
                        && field.Normalised == normalised
                        && field.Quantisation == destinationVectorQuantisation))
                    .OrderByDescending(static entry => entry.OldToNew.Count)
                    .FirstOrDefault();

                if (seed.OldToNew is not null && seed.OldToNew.Count > 0)
                {
                    string seedHnswExtension = VectorFilePaths.HnswFile(string.Empty, fieldName);
                    if (seed.Reader.FileExists(seedHnswExtension))
                    {
                        try
                        {
                            graph = HnswReader.Read(
                                seed.Reader.OpenInput(seedHnswExtension), vectorSource, normalised, seed.OldToNew);
                            graph.Thaw();
                            foreach (int docId in vectorDocIds)
                                if (!graph.ContainsNode(docId)) graph.Insert(docId);
                        }
                        catch (Exception ex) when (ex is IOException or InvalidDataException)
                        {
                            graph?.Dispose();
                            graph = null;
                            Diagnostics.LeanCorpusActivitySource.TraceSwallowed(
                                ex, $"HNSW seed read failed for '{fieldName}'; rebuilding graph from scratch");
                        }
                    }
                }
            }

            if (graph is null)
            {
                graph = HnswGraphBuilder.Build(vectorSource, vectorDocIds, _hnswBuildConfig);
            }
            else
            {
                graph.Freeze();
            }

            stopwatch.Stop();
            _metrics.RecordHnswBuild(stopwatch.Elapsed, vectorDocIds.Count);
            string hnswPath = VectorFilePaths.HnswFile(basePath, fieldName);
            HnswWriter.Write(hnswPath, graph, dimension, normalised);
            return true;
        }
        finally
        {
            graph?.Dispose();
        }
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
