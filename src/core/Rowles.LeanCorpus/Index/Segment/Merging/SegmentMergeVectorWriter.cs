using Rowles.LeanCorpus.Codecs.Vectors;
using MergeDocument = Rowles.LeanCorpus.Index.Segment.Merging.SegmentMergeDocumentMap.MergeDocument;

namespace Rowles.LeanCorpus.Index.Segment.Merging;

/// <summary>Owns vector remapping and encoding during merges; merged segments omit retired HNSW graphs.</summary>
internal sealed class SegmentMergeVectorWriter
{
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

    internal List<VectorFieldInfo> Write(SegmentMergePlan plan, State ctx)
    {
        VectorCodecFiles.EnsureCanCreate(plan.VectorQuantisation);
        return MergeVectors(ctx, plan.DocumentMap.Documents, plan.BasePath, plan.VectorContracts);
    }

    private List<VectorFieldInfo> MergeVectors(
        State ctx,
        IReadOnlyList<MergeDocument> documentOrder,
        string basePath,
        IReadOnlyDictionary<string, VectorFieldContract> vectorContracts)
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
            merged.Add(new VectorFieldInfo
            {
                FieldName = fieldName,
                Dimension = dimension,
                Normalised = normalised,
                Quantisation = VectorQuantisation.None,
                HasHnsw = false,
            });
        }
        return merged;
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
