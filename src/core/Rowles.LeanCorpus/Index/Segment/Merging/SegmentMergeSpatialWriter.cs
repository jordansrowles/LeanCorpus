using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using MergeDocument = Rowles.LeanCorpus.Index.Segment.Merging.SegmentMergeDocumentMap.MergeDocument;

namespace Rowles.LeanCorpus.Index.Segment.Merging;

/// <summary>Owns spatial remapping and codec output.</summary>
internal static class SegmentMergeSpatialWriter
{
    internal sealed class State : IDisposable
    {
        internal int TotalDocs { get; }
        internal Dictionary<string, PackedBkdFieldBuffer> PackedBkdFields { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, ShapeDocValuesFieldBuffer> ShapeDocValuesFields { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, Dictionary<string, ShapeDocValuesFieldMetadata>> ShapeDocValuesMetadataBySegment { get; } = new(StringComparer.Ordinal);

        internal State(int totalDocs) => TotalDocs = totalDocs;
        public void Dispose()
        {
            foreach (var buffer in PackedBkdFields.Values) buffer.Dispose();
            PackedBkdFields.Clear();
            foreach (var buffer in ShapeDocValuesFields.Values) buffer.Dispose();
            ShapeDocValuesFields.Clear();
        }
    }

    internal static void AccumulateSource(SegmentMergeDocumentMap.Source source, State ctx)
    {
        var (segInfo, docIdMap, reader) = source;
        // Dimensions, coordinate kinds and cross-file coverage were proven by the planner.
        var packedFieldNames = reader.GetPackedBkdFieldNames();
        IReadOnlyList<string> shapeDocValuesFieldNames = reader.GetShapeDocValuesFieldNames();
        var shapeDocValuesFields = new Dictionary<string, ShapeDocValuesFieldMetadata>(
            shapeDocValuesFieldNames.Count, StringComparer.Ordinal);
        if (shapeDocValuesFieldNames.Count > 0)
            reader.ValidateShapeDocValuesChecksum();
        foreach (string shapeFieldName in shapeDocValuesFieldNames)
        {
            if (!reader.TryGetShapeDocValuesFieldMetadata(shapeFieldName, out ShapeDocValuesFieldMetadata shapeMetadata))
                throw new InvalidDataException($"Shape DocValues field '{shapeFieldName}' disappeared during merge.");
            if (!ctx.ShapeDocValuesFields.TryGetValue(shapeFieldName, out ShapeDocValuesFieldBuffer? shapeBuffer))
            {
                shapeBuffer = new ShapeDocValuesFieldBuffer(shapeFieldName, shapeMetadata.Kind);
                ctx.ShapeDocValuesFields.Add(shapeFieldName, shapeBuffer);
            }
            shapeDocValuesFields.Add(shapeFieldName, shapeMetadata);
        }
        ctx.ShapeDocValuesMetadataBySegment.Add(segInfo.SegmentId, shapeDocValuesFields);
        if (packedFieldNames.Count > 0)
            reader.ValidatePackedBkdChecksum();
        foreach (var packedFieldName in packedFieldNames)
        {
            if (!reader.TryGetPackedBkdFieldMetadata(packedFieldName, out var metadata))
                throw new InvalidDataException($"Packed BKD field '{packedFieldName}' disappeared during merge.");
            if (!ctx.PackedBkdFields.TryGetValue(packedFieldName, out var packedBuffer))
            {
                packedBuffer = new PackedBkdFieldBuffer(metadata.Config);
                ctx.PackedBkdFields.Add(packedFieldName, packedBuffer);
            }
            var collector = new PackedBkdMergeVisitor(docIdMap, packedBuffer);
            reader.IntersectPackedBkd(packedFieldName, ref collector);
        }
    }

    internal static void CopyDocument(MergeDocument document, int newDocId, State ctx)
    {
        if (!ctx.ShapeDocValuesMetadataBySegment.TryGetValue(
                document.Segment.SegmentId,
                out Dictionary<string, ShapeDocValuesFieldMetadata>? shapeFields))
            return;

        foreach ((string shapeFieldName, ShapeDocValuesFieldMetadata _) in shapeFields)
        {
            if (!document.Reader.TryGetShapeDocValuesRecordMetadata(
                    shapeFieldName,
                    document.OldDocId,
                    out ShapeDocValuesRecordMetadata record))
                continue;
            document.Reader.ValidateShapeDocValuesRecord(shapeFieldName, document.OldDocId);
            byte[] rawRecord = document.Reader.ReadShapeDocValuesRecordBytes(shapeFieldName, document.OldDocId);
            ctx.ShapeDocValuesFields[shapeFieldName].AppendRawRecord(
                newDocId,
                record.ValueCount,
                record.PrimitiveCount,
                rawRecord);
        }
    }

    internal static void Write(SegmentMergePlan plan, SegmentMergeDocValuesWriter.State columns, State ctx)
    {
        WriteBkdTree(columns, plan.BasePath);
        WritePackedBkdTree(ctx, plan.BasePath);
        WriteShapeDocValues(ctx, plan.BasePath);
    }

    private static void WriteBkdTree(SegmentMergeDocValuesWriter.State ctx, string basePath)
    {
        if (ctx.NumericFields.Count > 0)
        {
            var bkdData = new Dictionary<string, List<(double Value, int DocId)>>(StringComparer.Ordinal);
            foreach (var (field, values) in ctx.NumericFields)
            {
                var points = new List<(double Value, int DocId)>(values.Count);
                foreach (var (docId, value) in values)
                    points.Add((value, docId));
                bkdData[field] = points;
            }
            BKDWriter.Write(basePath + ".bkd", bkdData);
        }

        if (ctx.Int64Fields.Count > 0)
        {
            var int64BkdData = new Dictionary<string, List<(long Value, int DocId)>>(StringComparer.Ordinal);
            foreach (var (field, values) in ctx.Int64Fields)
            {
                var points = new List<(long Value, int DocId)>(values.Count);
                foreach (var (docId, value) in values)
                    points.Add((value, docId));
                int64BkdData[field] = points;
            }
            Int64BKDWriter.Write(basePath + ".bkdl", int64BkdData);
        }
    }

    private static void WritePackedBkdTree(State ctx, string basePath)
    {
        if (ctx.PackedBkdFields.Count > 0)
            PackedBkdWriter.Write(
                basePath + ".pbkd",
                ctx.PackedBkdFields,
                PackedBkdBuildOptions.Default with { SpillDirectory = Path.GetDirectoryName(basePath) });
    }

    private static void WriteShapeDocValues(State ctx, string basePath)
    {
        if (ctx.ShapeDocValuesFields.Count > 0)
            ShapeDocValuesWriter.Write(basePath + ".dvg", ctx.TotalDocs, ctx.ShapeDocValuesFields);
    }

    private readonly struct PackedBkdMergeVisitor(int[] docIdMap, PackedBkdFieldBuffer destination) : IPackedBkdIntersectVisitor
    {
        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => PackedBkdCellRelation.Crosses;

        public void Visit(int docId)
            => throw new InvalidDataException("Packed BKD merge expected value payloads for every point.");

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
        {
            if ((uint)docId >= (uint)docIdMap.Length)
                throw new InvalidDataException("Packed BKD merge encountered an out-of-range document ID.");
            int remapped = docIdMap[docId];
            if (remapped >= 0)
                destination.Append(packedValue, remapped);
        }
    }

}
