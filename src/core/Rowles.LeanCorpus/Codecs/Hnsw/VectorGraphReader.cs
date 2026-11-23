using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>
/// Opens a <c>.vgr</c> body written by <see cref="VectorGraphWriter"/> (ADR052). Every structural
/// rule is validated before a graph is returned; any violation throws
/// <see cref="InvalidDataException"/> and no partially usable graph escapes.
/// </summary>
internal static class VectorGraphReader
{
    /// <summary>The persisted graph identity that segment metadata and vectors must agree with.</summary>
    /// <param name="Dimension">The field's vector dimension.</param>
    /// <param name="Normalised">Whether the field stores normalised vectors.</param>
    /// <param name="DocCount">The segment document count.</param>
    /// <param name="HasVector">Persisted vector presence by document ID.</param>
    internal readonly record struct Expectation(int Dimension, bool Normalised, int DocCount, Func<int, bool> HasVector);

    /// <summary>Opens a graph and takes ownership of <paramref name="input"/>.</summary>
    internal static VectorGraph Read(IndexInput input, Expectation expected)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(expected.HasVector);
        IndexInput? body = null;
        try
        {
            using (input)
            using (var frame = CodecFileReader.OpenSupported(input, VectorCodecFiles.Graph))
            {
                // Structural checks cannot see a flipped in-range neighbour or a changed inverse
                // norm, so a checksum mismatch must reject the whole graph before it is used.
                frame.ValidateChecksum();
                body = frame.OpenBodyInput();
            }

            var result = ReadBody(body, expected, mapLevelZero: BitConverter.IsLittleEndian);
            if (!result.HasMappedBody)
                body.Dispose();
            body = null;
            return result.Graph;
        }
        catch
        {
            body?.Dispose();
            throw;
        }
    }

    internal readonly record struct ReadResult(VectorGraph Graph, bool HasMappedBody);

    internal static ReadResult ReadBody(IndexInput body, Expectation expected, bool mapLevelZero)
    {
        long position = 0;
        int dimension = body.ReadInt32(ref position);
        byte scorer = body.ReadByte(ref position);
        byte flags = body.ReadByte(ref position);
        int m = body.ReadInt32(ref position);
        int m0 = body.ReadInt32(ref position);
        int efConstruction = body.ReadInt32(ref position);
        long seed = body.ReadInt64(ref position);
        int nodeCount = body.ReadInt32(ref position);
        int maxLevel = body.ReadInt32(ref position);
        int entryOrdinal = body.ReadInt32(ref position);

        if (scorer != VectorGraph.ScorerFloat32Cosine)
            throw Invalid($"unsupported scorer {scorer}");
        if ((flags & ~(VectorGraph.FlagNormalised | VectorGraph.FlagInverseNorms)) != 0)
            throw Invalid($"unknown flags 0x{flags:X2}");
        bool normalised = (flags & VectorGraph.FlagNormalised) != 0;
        bool hasInverseNorms = (flags & VectorGraph.FlagInverseNorms) != 0;
        if (hasInverseNorms == normalised)
            throw Invalid("inverse norms must be present exactly when vectors are not normalised");
        if (normalised != expected.Normalised)
            throw Invalid($"declares Normalised={normalised} but the segment field declares Normalised={expected.Normalised}");
        if (dimension != expected.Dimension)
            throw Invalid($"dimension {dimension} does not match the field dimension {expected.Dimension}");
        if (m is < 2 or > 100 || m0 is < 2 or > 200 || efConstruction is < 1 or > 2000)
            throw Invalid($"parameters M={m}, M0={m0}, efConstruction={efConstruction} are out of range");
        if (nodeCount < 0 || nodeCount > expected.DocCount)
            throw Invalid($"node count {nodeCount} is outside 0..{expected.DocCount}");
        if (nodeCount == 0 ? maxLevel != -1 || entryOrdinal != -1 : maxLevel is < 0 or >= VectorGraph.MaxLevels)
            throw Invalid($"maxLevel {maxLevel} or entry ordinal {entryOrdinal} is invalid for {nodeCount} nodes");

        var docIds = new int[nodeCount];
        body.ReadInt32Array(docIds, nodeCount, ref position);
        int previous = -1;
        foreach (int docId in docIds)
        {
            if (docId <= previous || docId >= expected.DocCount)
                throw Invalid("document IDs are not strictly ascending within the segment");
            if (!expected.HasVector(docId))
                throw Invalid($"node document {docId} has no stored vector");
            previous = docId;
        }

        int presentCount = 0;
        for (int docId = 0; docId < expected.DocCount; docId++)
        {
            if (expected.HasVector(docId))
                presentCount++;
        }
        if (presentCount != nodeCount)
            throw Invalid($"node count {nodeCount} differs from the {presentCount} stored vectors");

        int stride0 = 1 + m0;
        long level0Offset = position;
        if (!mapLevelZero && (long)nodeCount * stride0 > Array.MaxLength)
            throw Invalid($"level zero of {nodeCount} nodes is too large to materialise on this host");
        int[]? level0 = mapLevelZero ? null : new int[nodeCount * stride0];
        // Stamped per record, so each record's neighbours are checked for duplicates in O(degree).
        var marks = new int[nodeCount];
        int stamp = 0;
        ValidateLevelZero(body, nodeCount, m0, level0, marks, ref stamp, ref position);

        var upperOrdinals = new int[Math.Max(0, maxLevel)][];
        var upperAdjacency = new int[Math.Max(0, maxLevel)][];
        int stride = 1 + m;
        ReadOnlySpan<int> below = default;
        for (int level = 1; level <= maxLevel; level++)
        {
            int levelCount = body.ReadInt32(ref position);
            int belowCount = level == 1 ? nodeCount : below.Length;
            if (levelCount < 1 || levelCount > belowCount)
                throw Invalid($"level {level} has {levelCount} nodes");
            var ordinals = new int[levelCount];
            body.ReadInt32Array(ordinals, levelCount, ref position);
            int last = -1;
            foreach (int ordinal in ordinals)
            {
                if (ordinal <= last || ordinal >= nodeCount)
                    throw Invalid($"level {level} ordinals are not strictly ascending node ordinals");
                if (level > 1 && below.BinarySearch(ordinal) < 0)
                    throw Invalid($"level {level} node {ordinal} is absent from level {level - 1}");
                last = ordinal;
            }

            var adjacency = new int[checked(levelCount * stride)];
            body.ReadInt32Array(adjacency, adjacency.Length, ref position);
            for (int i = 0; i < levelCount; i++)
            {
                ReadOnlySpan<int> record = adjacency.AsSpan(i * stride, stride);
                ValidateRecord(record, m, ordinals[i], nodeCount, level, ordinals, marks, ref stamp);
            }

            upperOrdinals[level - 1] = ordinals;
            upperAdjacency[level - 1] = adjacency;
            below = ordinals;
        }

        if (nodeCount > 0)
        {
            bool entryValid = maxLevel == 0
                ? (uint)entryOrdinal < (uint)nodeCount
                : Array.BinarySearch(upperOrdinals[maxLevel - 1], entryOrdinal) >= 0;
            if (!entryValid)
                throw Invalid($"entry ordinal {entryOrdinal} is not a member of level {maxLevel}");
        }

        float[]? inverseNorms = null;
        if (hasInverseNorms)
        {
            inverseNorms = new float[nodeCount];
            body.ReadSingleArray(inverseNorms, nodeCount, ref position);
            foreach (float inverseNorm in inverseNorms)
            {
                if (!VectorGraph.IsValidInverseNorm(inverseNorm))
                    throw Invalid("an inverse norm is outside the conditioned range and is not a zero or scaled-cosine marker");
            }
        }

        if (position != body.Length)
            throw Invalid($"{body.Length - position} trailing body bytes");

        var graph = level0 is not null
            ? VectorGraph.FromArrays(dimension, normalised, m, m0, efConstruction, seed, maxLevel,
                entryOrdinal, docIds, level0, upperOrdinals, upperAdjacency, inverseNorms)
            : VectorGraph.FromMapped(dimension, normalised, m, m0, efConstruction, seed, maxLevel,
                entryOrdinal, docIds, body, level0Offset, upperOrdinals, upperAdjacency, inverseNorms);

        // The builder never publishes a graph with an unreachable node; one would silently hide documents.
        int reachable = graph.CountReachableFromEntry();
        if (reachable != nodeCount)
        {
            // An owned graph is released here; a mapped one wraps the body, which the caller disposes on failure.
            if (level0 is not null)
                graph.Dispose();
            throw Invalid($"{nodeCount - reachable} level-zero nodes are unreachable from the entry point");
        }

        return new ReadResult(graph, HasMappedBody: level0 is null);
    }

    private static void ValidateLevelZero(
        IndexInput body, int nodeCount, int m0, int[]? destination, int[] marks, ref int stamp, ref long position)
    {
        int stride = 1 + m0;
        const int ChunkRecords = 1024;
        int[] buffer = ArrayPool<int>.Shared.Rent(ChunkRecords * stride);
        try
        {
            for (int start = 0; start < nodeCount; start += ChunkRecords)
            {
                int records = Math.Min(ChunkRecords, nodeCount - start);
                Span<int> chunk = buffer.AsSpan(0, records * stride);
                body.ReadInt32Array(chunk, chunk.Length, ref position);
                for (int i = 0; i < records; i++)
                    ValidateRecord(chunk.Slice(i * stride, stride), m0, start + i, nodeCount, level: 0, levelMembers: default,
                        marks, ref stamp);
                if (destination is not null)
                    chunk.CopyTo(destination.AsSpan(start * stride));
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buffer);
        }
    }

    private static void ValidateRecord(
        ReadOnlySpan<int> record, int degree, int self, int nodeCount, int level, ReadOnlySpan<int> levelMembers,
        int[] marks, ref int stamp)
    {
        int count = record[0];
        if (count < 0 || count > degree)
            throw Invalid($"level {level} node {self} has neighbour count {count} outside 0..{degree}");
        if (++stamp == int.MaxValue)
        {
            Array.Clear(marks);
            stamp = 1;
        }
        for (int i = 1; i <= count; i++)
        {
            int neighbour = record[i];
            if ((uint)neighbour >= (uint)nodeCount || neighbour == self)
                throw Invalid($"level {level} node {self} has an invalid neighbour ordinal {neighbour}");
            if (marks[neighbour] == stamp)
                throw Invalid($"level {level} node {self} lists neighbour {neighbour} twice");
            marks[neighbour] = stamp;
            if (level > 0 && levelMembers.BinarySearch(neighbour) < 0)
                throw Invalid($"level {level} node {self} links to {neighbour}, which is not on that level");
        }
        for (int i = count + 1; i <= degree; i++)
        {
            if (record[i] != -1)
                throw Invalid($"level {level} node {self} has a non-empty unused neighbour slot");
        }
    }

    private static InvalidDataException Invalid(string detail)
        => new($"Vector graph (.vgr) is invalid: {detail}.");
}
