using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Writes dense float vectors with an explicit document-to-vector ordinal mapping.
/// </summary>
internal static class VectorWriter
{
    internal static void Write(string filePath, ReadOnlyMemory<float>[] vectors)
    {
        int dimension = 0;
        for (int i = 0; i < vectors.Length; i++)
        {
            if (vectors[i].Length > 0) { dimension = vectors[i].Length; break; }
        }

        var byDoc = new Dictionary<int, ReadOnlyMemory<float>>(vectors.Length);
        for (int i = 0; i < vectors.Length; i++)
        {
            if (vectors[i].Length == dimension)
                byDoc.Add(i, vectors[i]);
        }
        WriteField(filePath, vectors.Length, dimension, byDoc);
    }

    /// <summary>
    /// Writes a per-field dense vector file. Only documents with a vector receive a dense vector
    /// ordinal; missing documents are represented by their absence from the persisted document map.
    /// </summary>
    internal static void WriteField(
        string filePath,
        int docCount,
        int dimension,
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc,
        VectorQuantisation quantisation = VectorQuantisation.None)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentNullException.ThrowIfNull(vectorsByDoc);

        var bodyBuf = new ArrayBufferWriter<byte>(4096);
        bodyBuf.WriteInt32(docCount);
        bodyBuf.WriteInt32(dimension);
        bodyBuf.WriteByte((byte)quantisation);

        if (quantisation == VectorQuantisation.Int8)
            throw new ArgumentException(
                "Int8 vectors must be written with QuantisedVectorWriter.",
                nameof(quantisation));
        if (quantisation != VectorQuantisation.None)
            throw new ArgumentOutOfRangeException(nameof(quantisation));

        int[] docIds = vectorsByDoc.Keys.Order().ToArray();
        bodyBuf.WriteInt32(docIds.Length);
        foreach (int docId in docIds)
        {
            if ((uint)docId >= (uint)docCount)
                throw new InvalidDataException(
                    $"Vector document identifier {docId} is outside the segment range 0..{docCount - 1}.");
            bodyBuf.WriteInt32(docId);
        }

        foreach (int docId in docIds)
        {
            ReadOnlySpan<float> span = vectorsByDoc[docId].Span;
            if (span.Length != dimension)
                throw new InvalidDataException(
                    $"Vector for document {docId} has dimension {span.Length}; expected {dimension}.");
            for (int j = 0; j < dimension; j++)
                bodyBuf.WriteSingle(span[j]);
        }

        using var output = new IndexOutput(filePath);
        CodecFileHeader.Write(output, CodecFormats.Vectors, bodyBuf.WrittenSpan);
    }
}
