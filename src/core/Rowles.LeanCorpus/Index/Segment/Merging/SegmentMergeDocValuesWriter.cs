using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment.Merging;

/// <summary>Owns scalar columns, norms and parent state.</summary>
internal static class SegmentMergeDocValuesWriter
{
    internal sealed class State
    {
        internal int TotalDocs { get; }
        internal Dictionary<string, Dictionary<int, double>> NumericFields { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, Dictionary<int, long>> Int64Fields { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, int[]> FieldLengths { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, double[]> NumericDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, HashSet<int>> NumericDocValuesPresence { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, long[]> Int64DocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, HashSet<int>> Int64DocValuesPresence { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string?[]> SortedDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, IReadOnlyList<string>?[]> SortedSetDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, IReadOnlyList<double>?[]> SortedNumericDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, IReadOnlyList<long>?[]> Int64SortedDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, IReadOnlyList<byte[]>?[]> BinaryDocValues { get; } = new(StringComparer.Ordinal);
        internal ParentBitSet? ParentBitSet { get; set; }

        internal State(int totalDocs) => TotalDocs = totalDocs;
    }

    internal static void AccumulateSource(SegmentMergeDocumentMap.Source source, State ctx)
    {
        var (segInfo, docIdMap, reader) = source;
        var segParentBitSet = reader.GetParentBitSet();

        var segFieldLengths = reader.FileExists(".fln")
            ? FieldLengthReader.TryRead(reader.OpenInput(".fln"), segInfo.DocCount)
                ?? new Dictionary<string, int[]>(StringComparer.Ordinal)
            : new Dictionary<string, int[]>(StringComparer.Ordinal);

        var segNumericIndex = ReadNumericIndex(reader);
        var segInt64Index = ReadInt64Index(reader);
        AccumulateNumericIndexColumns(segNumericIndex, docIdMap, ctx);
        AccumulateInt64IndexColumns(segInt64Index, docIdMap, ctx);
        AccumulateFieldLengthColumns(segFieldLengths, docIdMap, ctx);
        AccumulateNumericDocValuesColumns(reader, docIdMap, ctx);
        AccumulateInt64DocValuesColumns(reader, docIdMap, ctx);
        AccumulateSortedDocValuesColumns(reader, docIdMap, ctx);
        AccumulateSortedSetDocValuesColumns(reader, docIdMap, ctx);
        AccumulateSortedNumericDocValuesColumns(reader, docIdMap, ctx);
        AccumulateInt64SortedNumericDocValuesColumns(reader, docIdMap, ctx);
        AccumulateBinaryDocValuesColumns(reader, docIdMap, ctx);

        for (int oldDocId = 0; oldDocId < segInfo.DocCount; oldDocId++)
        {
            int remapDocId = docIdMap[oldDocId];
            if (remapDocId < 0) continue;

            if (segParentBitSet is not null && segParentBitSet.IsParent(oldDocId))
            {
                ctx.ParentBitSet ??= new ParentBitSet(ctx.TotalDocs);
                ctx.ParentBitSet.Set(remapDocId);
            }
        }
    }

    internal static void Write(SegmentMergePlan plan, State ctx)
    {
        WriteNorms(plan.DocumentMap.Sources, plan.Readers, plan.FieldNames, plan.BasePath, plan.TotalDocs);
        WriteNumericFiles(ctx, plan.BasePath);
        WriteFieldLengths(ctx, plan.BasePath, plan.TotalDocs);
        WriteDocValueColumns(ctx, plan.BasePath);
        WriteParentBitSet(ctx, plan.BasePath);
    }

    private static void AccumulateNumericIndexColumns(
        Dictionary<string, Dictionary<int, double>> columns,
        int[] docIdMap,
        State ctx)
    {
        foreach ((string field, Dictionary<int, double> values) in columns)
        {
            if (!ctx.NumericFields.TryGetValue(field, out Dictionary<int, double>? destination))
            {
                destination = new Dictionary<int, double>(values.Count);
                ctx.NumericFields.Add(field, destination);
            }

            foreach ((int oldDocId, double value) in values)
            {
                if ((uint)oldDocId >= (uint)docIdMap.Length)
                    continue;
                int newDocId = docIdMap[oldDocId];
                if (newDocId >= 0)
                    destination[newDocId] = value;
            }
        }
    }

    private static void AccumulateInt64IndexColumns(
        Dictionary<string, Dictionary<int, long>> columns,
        int[] docIdMap,
        State ctx)
    {
        foreach ((string field, Dictionary<int, long> values) in columns)
        {
            if (!ctx.Int64Fields.TryGetValue(field, out Dictionary<int, long>? destination))
            {
                destination = new Dictionary<int, long>(values.Count);
                ctx.Int64Fields.Add(field, destination);
            }

            foreach ((int oldDocId, long value) in values)
            {
                if ((uint)oldDocId >= (uint)docIdMap.Length)
                    continue;
                int newDocId = docIdMap[oldDocId];
                if (newDocId >= 0)
                    destination[newDocId] = value;
            }
        }
    }

    private static void AccumulateFieldLengthColumns(
        Dictionary<string, int[]> columns,
        int[] docIdMap,
        State ctx)
    {
        foreach ((string field, int[] values) in columns)
        {
            if (!ctx.FieldLengths.TryGetValue(field, out int[]? destination))
            {
                destination = new int[ctx.TotalDocs];
                ctx.FieldLengths.Add(field, destination);
            }

            int documentCount = Math.Min(values.Length, docIdMap.Length);
            for (int oldDocId = 0; oldDocId < documentCount; oldDocId++)
            {
                int newDocId = docIdMap[oldDocId];
                if (newDocId >= 0)
                    destination[newDocId] = values[oldDocId];
            }
        }
    }

    private static void AccumulateNumericDocValuesColumns(
        SegmentReader reader,
        int[] docIdMap,
        State ctx)
    {
        if (!reader.FileExists(".dvn"))
            return;

        using IndexInput input = reader.OpenInput(".dvn");
        foreach ((string field, NumericDocValuesColumn column) in NumericDocValuesReader.OpenColumns(input, reader.MaxDoc))
        {
            if (!ctx.NumericDocValues.TryGetValue(field, out double[]? destination))
            {
                destination = new double[ctx.TotalDocs];
                ctx.NumericDocValues.Add(field, destination);
            }
            if (!ctx.NumericDocValuesPresence.TryGetValue(field, out HashSet<int>? destinationPresence))
            {
                destinationPresence = new HashSet<int>();
                ctx.NumericDocValuesPresence.Add(field, destinationPresence);
            }

            if (column.Presence is { } presentDocuments)
            {
                foreach (int oldDocId in presentDocuments)
                {
                    if ((uint)oldDocId >= (uint)docIdMap.Length)
                        continue;
                    int newDocId = docIdMap[oldDocId];
                    if (newDocId < 0)
                        continue;
                    destination[newDocId] = column.GetValue(oldDocId);
                    destinationPresence.Add(newDocId);
                }
            }
            else
            {
                int documentCount = Math.Min(column.DocumentCount, docIdMap.Length);
                for (int oldDocId = 0; oldDocId < documentCount; oldDocId++)
                {
                    int newDocId = docIdMap[oldDocId];
                    if (newDocId < 0)
                        continue;
                    destination[newDocId] = column.GetValue(oldDocId);
                    destinationPresence.Add(newDocId);
                }
            }
        }
    }

    private static void AccumulateInt64DocValuesColumns(
        SegmentReader reader,
        int[] docIdMap,
        State ctx)
    {
        if (!reader.FileExists(".dvnl"))
            return;

        using IndexInput input = reader.OpenInput(".dvnl");
        foreach ((string field, Int64DocValuesColumn column) in Int64DocValuesReader.OpenColumns(input, reader.MaxDoc))
        {
            if (!ctx.Int64DocValues.TryGetValue(field, out long[]? destination))
            {
                destination = new long[ctx.TotalDocs];
                ctx.Int64DocValues.Add(field, destination);
            }
            if (!ctx.Int64DocValuesPresence.TryGetValue(field, out HashSet<int>? destinationPresence))
            {
                destinationPresence = new HashSet<int>();
                ctx.Int64DocValuesPresence.Add(field, destinationPresence);
            }

            if (column.Presence is { } presentDocuments)
            {
                foreach (int oldDocId in presentDocuments)
                {
                    if ((uint)oldDocId >= (uint)docIdMap.Length)
                        continue;
                    int newDocId = docIdMap[oldDocId];
                    if (newDocId < 0)
                        continue;
                    destination[newDocId] = column.GetValue(oldDocId);
                    destinationPresence.Add(newDocId);
                }
            }
            else
            {
                int documentCount = Math.Min(column.DocumentCount, docIdMap.Length);
                for (int oldDocId = 0; oldDocId < documentCount; oldDocId++)
                {
                    int newDocId = docIdMap[oldDocId];
                    if (newDocId < 0)
                        continue;
                    destination[newDocId] = column.GetValue(oldDocId);
                    destinationPresence.Add(newDocId);
                }
            }
        }
    }

    private static void AccumulateSortedDocValuesColumns(
        SegmentReader reader,
        int[] docIdMap,
        State ctx)
    {
        if (!reader.FileExists(".dvs"))
            return;

        using IndexInput input = reader.OpenInput(".dvs");
        foreach ((string field, SortedDocValuesColumn column) in SortedDocValuesReader.OpenColumns(input, reader.MaxDoc))
        {
            if (!ctx.SortedDocValues.TryGetValue(field, out string?[]? destination))
            {
                destination = new string?[ctx.TotalDocs];
                ctx.SortedDocValues.Add(field, destination);
            }

            if (column.Presence is { } presentDocuments)
            {
                foreach (int oldDocId in presentDocuments)
                {
                    if ((uint)oldDocId >= (uint)docIdMap.Length)
                        continue;
                    int newDocId = docIdMap[oldDocId];
                    if (newDocId >= 0)
                        destination[newDocId] = column.GetValue(oldDocId);
                }
            }
            else
            {
                int documentCount = Math.Min(column.DocumentCount, docIdMap.Length);
                for (int oldDocId = 0; oldDocId < documentCount; oldDocId++)
                {
                    int newDocId = docIdMap[oldDocId];
                    if (newDocId >= 0)
                        destination[newDocId] = column.GetValue(oldDocId);
                }
            }
        }
    }

    private static void AccumulateSortedSetDocValuesColumns(
        SegmentReader reader,
        int[] docIdMap,
        State ctx)
    {
        if (!reader.FileExists(".dss"))
            return;

        using IndexInput input = reader.OpenInput(".dss");
        foreach ((string field, SortedSetDocValuesColumn column) in SortedSetDocValuesReader.OpenColumns(input, reader.MaxDoc))
        {
            int documentCount = Math.Min(column.DocumentCount, docIdMap.Length);
            for (int oldDocId = 0; oldDocId < documentCount; oldDocId++)
            {
                int newDocId = docIdMap[oldDocId];
                if (newDocId < 0 || !column.HasValues(oldDocId))
                    continue;
                AddMergedMultiValue(ctx.SortedSetDocValues, field, newDocId, ctx.TotalDocs, column.GetValues(oldDocId));
            }
        }
    }

    private static void AccumulateSortedNumericDocValuesColumns(
        SegmentReader reader,
        int[] docIdMap,
        State ctx)
    {
        if (!reader.FileExists(".dsn"))
            return;

        using IndexInput input = reader.OpenInput(".dsn");
        foreach ((string field, SortedNumericDocValuesColumn column) in SortedNumericDocValuesReader.OpenColumns(input, reader.MaxDoc))
        {
            int documentCount = Math.Min(column.DocumentCount, docIdMap.Length);
            for (int oldDocId = 0; oldDocId < documentCount; oldDocId++)
            {
                int newDocId = docIdMap[oldDocId];
                if (newDocId < 0 || !column.HasValues(oldDocId))
                    continue;
                AddMergedMultiValue(ctx.SortedNumericDocValues, field, newDocId, ctx.TotalDocs, column.GetValues(oldDocId));
            }
        }
    }

    private static void AccumulateInt64SortedNumericDocValuesColumns(
        SegmentReader reader,
        int[] docIdMap,
        State ctx)
    {
        if (!reader.FileExists(".dsnl"))
            return;

        using IndexInput input = reader.OpenInput(".dsnl");
        foreach ((string field, Int64SortedNumericDocValuesColumn column) in Int64SortedNumericDocValuesReader.OpenColumns(input, reader.MaxDoc))
        {
            int documentCount = Math.Min(column.DocumentCount, docIdMap.Length);
            for (int oldDocId = 0; oldDocId < documentCount; oldDocId++)
            {
                int newDocId = docIdMap[oldDocId];
                if (newDocId < 0 || !column.HasValues(oldDocId))
                    continue;
                AddMergedMultiValue(ctx.Int64SortedDocValues, field, newDocId, ctx.TotalDocs, column.GetValues(oldDocId));
            }
        }
    }

    private static void AccumulateBinaryDocValuesColumns(
        SegmentReader reader,
        int[] docIdMap,
        State ctx)
    {
        if (!reader.FileExists(".dvb"))
            return;

        using IndexInput input = reader.OpenInput(".dvb");
        foreach ((string field, BinaryDocValuesColumn column) in BinaryDocValuesReader.OpenColumns(input, reader.MaxDoc))
        {
            int documentCount = Math.Min(column.DocumentCount, docIdMap.Length);
            for (int oldDocId = 0; oldDocId < documentCount; oldDocId++)
            {
                int newDocId = docIdMap[oldDocId];
                if (newDocId < 0 || !column.HasValues(oldDocId))
                    continue;
                AddMergedMultiValue(ctx.BinaryDocValues, field, newDocId, ctx.TotalDocs, column.GetValues(oldDocId));
            }
        }
    }

    private static void WriteNorms(
        IReadOnlyList<SegmentMergeDocumentMap.Source> perSegmentMaps,
        IReadOnlyDictionary<string, SegmentReader> readers,
        IReadOnlyCollection<string> fieldNames,
        string basePath,
        int totalDocs)
    {
        var fieldNorms = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var fieldBoosts = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var fieldName in fieldNames)
        {
            var norms = new float[totalDocs];
            var boosts = new float[totalDocs];
            Array.Fill(boosts, 1.0f);
            foreach (var (segInfo, docIdMap, _) in perSegmentMaps)
            {
                var reader = readers[segInfo.SegmentId];
                for (int oldDocId = 0; oldDocId < segInfo.DocCount; oldDocId++)
                {
                    int newDocId = docIdMap[oldDocId];
                    if (newDocId < 0) continue;
                    norms[newDocId] = reader.GetNorm(oldDocId, fieldName);
                    boosts[newDocId] = reader.GetFieldBoost(oldDocId, fieldName);
                }
            }
            fieldNorms[fieldName] = norms;
            fieldBoosts[fieldName] = boosts;
        }
        NormsWriter.Write(basePath + ".nrm", fieldNorms, fieldBoosts);
    }

    private static void WriteNumericFiles(State ctx, string basePath)
    {
        if (ctx.NumericFields.Count > 0)
            WriteNumericIndex(basePath + ".num", ctx.NumericFields);
        if (ctx.Int64Fields.Count > 0)
            WriteInt64Index(basePath + ".numl", ctx.Int64Fields);
    }

    private static void WriteFieldLengths(State ctx, string basePath, int totalDocs)
    {
        if (ctx.FieldLengths.Count > 0)
            FieldLengthWriter.Write(basePath + ".fln", ctx.FieldLengths, totalDocs);
    }

    private static void WriteDocValueColumns(State ctx, string basePath)
    {
        if (ctx.NumericDocValues.Count > 0)
        {
            CodecFileWriter.WriteAtomically(basePath + ".dvn", DocValuesCodecFiles.Numeric, durable: false, bodyOutput =>
            {
                bodyOutput.WriteInt32(ctx.NumericDocValues.Count);
                string[] fieldKeys = System.Buffers.ArrayPool<string>.Shared.Rent(ctx.NumericDocValues.Count);
                try
                {
                    int kn = 0;
                    foreach (var key in ctx.NumericDocValues.Keys) fieldKeys[kn++] = key;
                    for (int i = 0; i < kn; i++)
                    {
                        var field = fieldKeys[i];
                        if (!ctx.NumericDocValuesPresence.TryGetValue(field, out var presenceSet))
                            throw new InvalidDataException($"Numeric DocValues field '{field}' has no merged presence set.");
                        NumericDocValuesWriter.WriteFieldBlock(bodyOutput, field, ctx.NumericDocValues[field], ctx.TotalDocs, presenceSet);
                        ctx.NumericDocValues.Remove(field);
                        ctx.NumericDocValuesPresence.Remove(field);
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<string>.Shared.Return(fieldKeys, clearArray: true);
                }
            });
        }
        if (ctx.Int64DocValues.Count > 0)
        {
            var int64Presence = new Dictionary<string, IReadOnlySet<int>>(ctx.Int64DocValues.Count, StringComparer.Ordinal);
            foreach (var field in ctx.Int64DocValues.Keys)
            {
                if (!ctx.Int64DocValuesPresence.TryGetValue(field, out var presenceSet))
                    throw new InvalidDataException($"Int64 DocValues field '{field}' has no merged presence set.");
                int64Presence[field] = presenceSet;
            }
            Int64DocValuesWriter.Write(basePath + ".dvnl", ctx.Int64DocValues, ctx.TotalDocs, int64Presence);
            ctx.Int64DocValues.Clear();
            ctx.Int64DocValuesPresence.Clear();
        }

        if (ctx.SortedDocValues.Count > 0)
        {
            CodecFileWriter.WriteAtomically(basePath + ".dvs", DocValuesCodecFiles.Sorted, durable: false, bodyOutput =>
            {
                bodyOutput.WriteInt32(ctx.SortedDocValues.Count);
                string[] fieldKeys = System.Buffers.ArrayPool<string>.Shared.Rent(ctx.SortedDocValues.Count);
                try
                {
                    int kn = 0;
                    foreach (var key in ctx.SortedDocValues.Keys) fieldKeys[kn++] = key;
                    for (int i = 0; i < kn; i++)
                    {
                        var field = fieldKeys[i];
                        SortedDocValuesWriter.WriteFieldBlock(bodyOutput, field, ctx.SortedDocValues[field], ctx.TotalDocs);
                        ctx.SortedDocValues.Remove(field);
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<string>.Shared.Return(fieldKeys, clearArray: true);
                }
            });
        }
        if (ctx.SortedSetDocValues.Count > 0)
            SortedSetDocValuesWriter.Write(basePath + ".dss", ctx.SortedSetDocValues, ctx.TotalDocs);
        if (ctx.SortedNumericDocValues.Count > 0)
            SortedNumericDocValuesWriter.Write(basePath + ".dsn", ctx.SortedNumericDocValues, ctx.TotalDocs);
        if (ctx.Int64SortedDocValues.Count > 0)
            Int64SortedNumericDocValuesWriter.Write(basePath + ".dsnl", ctx.Int64SortedDocValues, ctx.TotalDocs);
        if (ctx.BinaryDocValues.Count > 0)
            BinaryDocValuesWriter.Write(basePath + ".dvb", ctx.BinaryDocValues, ctx.TotalDocs);
    }

    private static void AddMergedMultiValue<T>(
        Dictionary<string, IReadOnlyList<T>?[]> destination,
        string field,
        int docId,
        int totalDocs,
        IReadOnlyList<T> values)
    {
        if (!destination.TryGetValue(field, out var perDoc))
        {
            perDoc = new IReadOnlyList<T>?[totalDocs];
            destination[field] = perDoc;
        }

        perDoc[docId] = values.ToArray();
    }

    private static void WriteParentBitSet(State ctx, string basePath)
    {
        ctx.ParentBitSet?.WriteTo(basePath + ".pbs");
    }

    private static void WriteNumericIndex(string filePath, Dictionary<string, Dictionary<int, double>> numericIndex)
        => NumericIndexCodec.WriteDouble(filePath, numericIndex);

    private static void WriteInt64Index(string filePath, Dictionary<string, Dictionary<int, long>> int64Index)
        => NumericIndexCodec.WriteInt64(filePath, int64Index);

    private static Dictionary<string, Dictionary<int, double>> ReadNumericIndex(SegmentReader reader)
    {
        var result = new Dictionary<string, Dictionary<int, double>>(StringComparer.Ordinal);
        if (!reader.FileExists(".num"))
            return result;

        return NumericIndexCodec.ReadDouble(reader.OpenInput(".num"));
    }

    private static Dictionary<string, Dictionary<int, long>> ReadInt64Index(SegmentReader reader)
    {
        var result = new Dictionary<string, Dictionary<int, long>>(StringComparer.Ordinal);
        if (!reader.FileExists(".numl"))
            return result;

        return NumericIndexCodec.ReadInt64(reader.OpenInput(".numl"));
    }

}
