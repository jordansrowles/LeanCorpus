using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Fixtures;

/// <summary>Authors tagged Int8 bytes for historical-reader tests without invoking the retired writer.</summary>
internal static class HistoricalInt8VectorFixtures
{
    internal static SegmentInfo[] ReplaceFloat32SegmentsWithV3Tag1(string directoryPath, string fieldName)
    {
        using var directory = new MMapDirectory(directoryPath);
        SegmentInfo[] segments = IndexRecovery.RecoverLatestCommit(directoryPath, cleanupOrphans: false)!
            .SegmentInfos
            .OrderBy(static segment => segment.SegmentId, StringComparer.Ordinal)
            .ToArray();

        foreach (SegmentInfo segment in segments)
        {
            if (segment.IsCompoundFile)
                throw new InvalidOperationException("Historical Int8 fixture conversion requires loose segment files.");

            VectorFieldInfo field = segment.VectorFields.Single(item => item.FieldName == fieldName);
            var vectors = new Dictionary<int, ReadOnlyMemory<float>>();
            using (var reader = new SegmentReader(directory, segment))
            {
                for (int docId = 0; docId < reader.MaxDoc; docId++)
                    if (reader.GetVector(fieldName, docId) is { } vector)
                        vectors.Add(docId, vector);
            }

            string basePath = Path.Combine(directoryPath, segment.SegmentId);
            File.Delete(VectorFilePaths.VectorFile(basePath, fieldName));
            WriteV3Tag1(
                VectorFilePaths.QuantisedVectorFile(basePath, fieldName),
                segment.DocCount,
                field.Dimension,
                vectors);
            segment.VectorFields.Remove(field);
            segment.VectorFields.Add(new VectorFieldInfo
            {
                FieldName = field.FieldName,
                Dimension = field.Dimension,
                Normalised = field.Normalised,
                HasHnsw = false,
                Quantisation = VectorQuantisation.Int8,
            });
            SegmentFlusher.RefreshSegmentSize(segment, directoryPath);
        }

        return segments;
    }

    internal static void WriteV3Tag1(
        string path,
        int docCount,
        int dimension,
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentNullException.ThrowIfNull(vectorsByDoc);
        VectorPresence.ValidateVectors(docCount, dimension, vectorsByDoc);
        byte[] presence = VectorPresence.Create(docCount, vectorsByDoc.Keys);

        float maximumMagnitude = 0f;
        foreach (ReadOnlyMemory<float> memory in vectorsByDoc.Values)
        foreach (float component in memory.Span)
        {
            if (!float.IsFinite(component))
                throw new InvalidDataException("Historical Int8 fixture input contains a non-finite component.");
            maximumMagnitude = MathF.Max(maximumMagnitude, MathF.Abs(component));
        }
        if (maximumMagnitude == 0f)
            maximumMagnitude = 1f;
        float alpha = maximumMagnitude / 127f;
        float min = -128f * alpha;

        CodecFileWriter.WriteAtomically(path, "leancorpus.vectors.quantised", formatVersion: 3, durable: false,
            bodyOutput =>
            {
                bodyOutput.WriteInt32(docCount);
                bodyOutput.WriteInt32(dimension);
                bodyOutput.WriteByte((byte)VectorQuantisation.Int8);
                bodyOutput.WriteBytes(presence, 0, presence.Length);
                bodyOutput.WriteByte((byte)QuantisedVectorStorageKind.LegacyInt8);
                bodyOutput.WriteSingle(min);
                bodyOutput.WriteSingle(alpha);
                for (int docId = 0; docId < docCount; docId++)
                    bodyOutput.WriteSingle(0f);

                Span<float> zero = dimension <= 256 ? stackalloc float[dimension] : new float[dimension];
                zero.Clear();
                for (int docId = 0; docId < docCount; docId++)
                {
                    ReadOnlySpan<float> vector = vectorsByDoc.TryGetValue(docId, out ReadOnlyMemory<float> memory)
                        ? memory.Span
                        : zero;
                    for (int component = 0; component < dimension; component++)
                    {
                        float quantised = Math.Clamp((vector[component] - min) / alpha + 0.5f, 0f, 255f);
                        bodyOutput.WriteByte((byte)quantised);
                    }
                }
            });
    }
}
