using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

/// <summary>Orders Packed BKD fields and emits their deterministic v1 sections.</summary>
internal static class PackedBkdWriter
{
    internal static void Write(
        string filePath,
        IReadOnlyDictionary<string, PackedBkdFieldBuffer> fields)
        => Write(filePath, fields, PackedBkdBuildOptions.Default);

    internal static void Write(
        string filePath,
        IReadOnlyDictionary<string, PackedBkdFieldBuffer> fields,
        PackedBkdBuildOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(fields);
        options.Validate();
        if (options.SpillDirectory is null)
            options = options with
            {
                SpillDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Path.GetTempPath()
            };

        foreach (string name in fields.Keys)
            PackedBkdFormat.ValidateFieldName(name);

        var names = fields
            .Where(static pair => pair.Value.Count > 0)
            .Select(static pair => pair.Key)
            .ToArray();
        Array.Sort(names, PackedBkdFieldNameComparer.Instance);
        for (int i = 0; i < names.Length; i++)
        {
            if (i > 0 && PackedBkdFieldNameComparer.Instance.Compare(names[i - 1], names[i]) == 0)
                throw new ArgumentException($"Packed BKD field name '{names[i]}' occurs more than once.", nameof(fields));
        }

        using var activity = Diagnostics.LeanCorpusActivitySource.Source.StartActivity(
            Diagnostics.LeanCorpusActivitySource.PackedBkdBuild);
        activity?.SetTag("packed_bkd.fields", names.Length);
        activity?.SetTag("packed_bkd.points", names.Sum(name => (long)fields[name].Count));
        activity?.SetTag("packed_bkd.memory_budget_bytes", options.MemoryBudgetBytes);
        activity?.SetTag("packed_bkd.force_spill", options.ForceSpill);

        try
        {
            CodecFileWriter.WriteAtomically(
                filePath,
                PackedBkdCodecFiles.Descriptor,
                durable: false,
                output =>
                {
                    long bodyStart = output.Position;
                    var directory = new DirectoryEntry[names.Length];
                    int leafCount = 0;
                    bool usedSpill = false;
                    long peakBuildBytes = 0;
                    for (int i = 0; i < names.Length; i++)
                    {
                        options.CancellationToken.ThrowIfCancellationRequested();
                        var field = fields[names[i]];
                        long fieldOffset = checked(output.Position - bodyStart);
                        using var built = PackedBkdBuilder.Build(field, options);
                        leafCount += built.LeafCount;
                        usedSpill |= built.UsedSpill;
                        peakBuildBytes = Math.Max(peakBuildBytes, built.PeakBuildBytes);
                        WriteField(output, built);
                        directory[i] = new DirectoryEntry(
                            names[i],
                            fieldOffset,
                            checked(output.Position - bodyStart - fieldOffset));
                    }

                    long directoryOffset = checked(output.Position - bodyStart);
                    output.WriteInt32(names.Length);
                    foreach (var entry in directory)
                    {
                        PackedBkdFormat.WriteFieldName(output, entry.Name);
                        output.WriteInt64(entry.Offset);
                        output.WriteInt64(entry.Length);
                    }

                    output.WriteInt32(unchecked((int)PackedBkdFormat.FooterMagic));
                    output.WriteInt32(names.Length);
                    output.WriteInt64(directoryOffset);
                    activity?.SetTag("packed_bkd.leaves", leafCount);
                    activity?.SetTag("packed_bkd.build_path", usedSpill ? "spill" : "memory");
                    activity?.SetTag("packed_bkd.build_peak_bytes", peakBuildBytes);
                    activity?.SetTag("packed_bkd.output_bytes", output.Position - bodyStart);
                });
            activity?.SetTag("packed_bkd.outcome", "success");
        }
        catch
        {
            activity?.SetTag("packed_bkd.outcome", "failure");
            throw;
        }
    }

    private static void WriteField(CodecBodyOutput output, PackedBkdBuiltField field)
    {
        output.WriteInt32(unchecked((int)PackedBkdFormat.FieldMagic));
        output.WriteByte(checked((byte)field.Config.Dimensions));
        output.WriteByte(checked((byte)field.Config.IndexedDimensions));
        output.WriteByte(checked((byte)field.Config.BytesPerDimension));
        output.WriteByte(0);
        output.WriteByte((byte)field.Config.MaxPointsPerLeaf);
        output.WriteByte(checked((byte)(field.Config.MaxPointsPerLeaf >> 8)));
        output.WriteByte(0);
        output.WriteByte(0);
        output.WriteInt32(field.LeafCount);
        output.WriteInt64(field.PointCount);
        output.WriteInt32(field.DocumentCount);
        output.WriteInt32(field.SplitDimensions.Length);
        output.WriteBytes(field.RootMin);
        output.WriteBytes(field.RootMax);
        output.WriteBytes(field.SplitDimensions);
        output.WriteBytes(field.SplitValues);

        foreach (long leafOffset in field.GetLeafOffsets())
            output.WriteInt64(leafOffset);
        field.WriteLeafData(output);
    }

    private readonly record struct DirectoryEntry(string Name, long Offset, long Length);
}
