using Spectre.Console;
using Rowles.LeanCorpus.Index.Inspection;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Cli.Rendering;

/// <summary>Renders structured index inspection results for terminal and redirected output.</summary>
internal static class IndexInspectionRenderer
{
    internal static void Overview(IAnsiConsole console, IndexInspectionOverview value)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(value);
        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("Path", Markup.Escape(value.DirectoryPath));
        grid.AddRow("Commit generation", Value(value.CommitGeneration));
        grid.AddRow("Documents", $"{value.LiveDocumentCount:N0} live / {value.DocumentCount:N0} total / {value.DeletedDocumentCount:N0} deleted");
        grid.AddRow("Segments", value.SegmentCount.ToString("N0"));
        grid.AddRow("Files and size", $"{value.FileCount:N0} / {FormatBytes(value.TotalSizeBytes)}");
        grid.AddRow("Vectors / HNSW", $"{Yes(value.HasVectors)} / {Yes(value.HasHnsw)}");
        grid.AddRow("Ordinal DocValues", Yes(value.HasOrdinalDocValues));
        grid.AddRow("Geo fields", Yes(value.HasGeo));
        grid.AddRow("Compound segments", value.CompoundSegmentCount.ToString("N0"));
        grid.AddRow("Orphan files", value.OrphanFileCount.ToString("N0"));
        grid.AddRow("Format", value.HasUnsupportedFutureFormat ? "future format" : value.HasUnknownFormat ? "unknown format" : value.HasOutdatedCodec ? "readable, outdated codec" : "current");
        console.Write(new Panel(grid).Header("LeanCorpus index overview", Justify.Left).Border(BoxBorder.Rounded));
    }

    internal static void Segments(IAnsiConsole console, IReadOnlyList<IndexInspectionSegment> segments)
    {
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Segment").AddColumn("Docs").AddColumn("Live").AddColumn("Generation").AddColumn("Delete gen").AddColumn("Size").AddColumn("Compound").AddColumn("Files").AddColumn("Warnings");
        foreach (IndexInspectionSegment segment in segments)
            table.AddRow(Markup.Escape(segment.SegmentId), Value(segment.DocumentCount), Value(segment.LiveDocumentCount), Value(segment.CommitGeneration), Value(segment.DeleteGeneration), FormatBytes(segment.TotalSizeBytes), segment.IsCompound is null ? "?" : Yes(segment.IsCompound.Value), segment.Files.Count.ToString("N0"), (segment.MissingFiles.Count + segment.Warnings.Count).ToString("N0"));
        console.Write(table);
    }

    internal static void Fields(IAnsiConsole console, IReadOnlyList<IndexInspectionField> fields)
    {
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Field").AddColumn("Indexed").AddColumn("Stored").AddColumn("DocValues").AddColumn("Docs").AddColumn("Terms / TF").AddColumn("Vector").AddColumn("Spatial");
        foreach (IndexInspectionField field in fields)
        {
            string vector = field.VectorDimension is null ? "" : $"{field.VectorDimension}d, {field.VectorQuantisation}, HNSW {Yes(field.HasHnsw ?? false)} ({field.HnswNodeCount ?? 0:N0})";
            table.AddRow(Markup.Escape(field.Name), Yes(field.Indexed), Yes(field.Stored), Yes(field.HasDocValues), field.DocumentCount.ToString("N0"), field.UniqueTermCount is null ? "" : $"{field.UniqueTermCount:N0} / {field.TotalTermFrequency:N0}", vector, field.SpatialKind?.ToString() ?? "");
        }
        console.Write(table);
    }

    internal static void Terms(IAnsiConsole console, IReadOnlyList<IndexInspectionTerm> terms)
    {
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Term").AddColumn("Documents").AddColumn("Frequency");
        foreach (IndexInspectionTerm term in terms)
            table.AddRow(Markup.Escape(term.Term), term.DocumentFrequency.ToString("N0"), term.TotalTermFrequency.ToString("N0"));
        console.Write(table);
    }

    internal static void Postings(IAnsiConsole console, IReadOnlyList<IndexInspectionPosting> postings)
    {
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Document").AddColumn("Segment").AddColumn("Local").AddColumn("Frequency").AddColumn("Positions");
        foreach (IndexInspectionPosting posting in postings)
            table.AddRow(posting.DocumentId.ToString(), Markup.Escape(posting.SegmentId), posting.SegmentDocumentId.ToString(), posting.Frequency.ToString(), string.Join(", ", posting.Positions));
        console.Write(table);
    }

    internal static void Document(IAnsiConsole console, IndexInspectionDocument document)
    {
        var tree = new Tree($"Document {document.DocumentId} · {Markup.Escape(document.SegmentId)}:{document.SegmentDocumentId} · {(document.IsLive ? "live" : "deleted")}");
        var stored = tree.AddNode("Stored fields");
        foreach ((string name, IReadOnlyList<string> values) in document.StoredFields)
            stored.AddNode($"{Markup.Escape(name)}: {Markup.Escape(string.Join(" | ", values))}");
        var binary = tree.AddNode("Binary stored fields");
        foreach ((string name, IReadOnlyList<int> lengths) in document.BinaryFieldLengths)
            binary.AddNode($"{Markup.Escape(name)}: {string.Join(", ", lengths)} bytes");
        var vectors = tree.AddNode("Vectors");
        foreach ((string name, IReadOnlyList<float> values) in document.Vectors)
            vectors.AddNode($"{Markup.Escape(name)}: {values.Count} dimensions");
        console.Write(tree);
    }

    internal static void Explanation(IAnsiConsole console, Explanation explanation)
    {
        ArgumentNullException.ThrowIfNull(explanation);
        var tree = new Tree($"{explanation.Score:G7}  {Markup.Escape(explanation.Description)}");
        AddChildren(tree, explanation.Details);
        console.Write(tree);
    }

    internal static void WriteOverview(TextWriter writer, IndexInspectionOverview value)
    {
        writer.WriteLine($"Index: {value.DirectoryPath}");
        writer.WriteLine($"Generation: {Value(value.CommitGeneration)}");
        writer.WriteLine($"Documents: {value.LiveDocumentCount} live, {value.DeletedDocumentCount} deleted ({value.DocumentCount} total)");
        writer.WriteLine($"Segments: {value.SegmentCount}");
        writer.WriteLine($"Files: {value.FileCount}, {FormatBytes(value.TotalSizeBytes)}");
        writer.WriteLine($"Vectors: {Yes(value.HasVectors)}, HNSW: {Yes(value.HasHnsw)}, facets: {Yes(value.HasOrdinalDocValues)}, geo: {Yes(value.HasGeo)}");
        writer.WriteLine($"Compound segments: {value.CompoundSegmentCount}; orphan files: {value.OrphanFileCount}");
        writer.WriteLine($"Format: {(value.HasUnsupportedFutureFormat ? "future format" : value.HasUnknownFormat ? "unknown format" : value.HasOutdatedCodec ? "readable, outdated codec" : "current")}");
    }

    internal static void WriteSegments(TextWriter writer, IReadOnlyList<IndexInspectionSegment> segments)
    {
        foreach (IndexInspectionSegment segment in segments)
            writer.WriteLine($"{segment.SegmentId}: {Value(segment.LiveDocumentCount)} live / {Value(segment.DocumentCount)} docs; generation {Value(segment.CommitGeneration)}; {FormatBytes(segment.TotalSizeBytes)}; {(segment.IsCompound is null ? "compound unknown" : segment.IsCompound.Value ? "compound" : "loose")}; {segment.Files.Count} files; {segment.MissingFiles.Count + segment.Warnings.Count} warnings");
    }

    internal static void WriteFields(TextWriter writer, IReadOnlyList<IndexInspectionField> fields)
    {
        foreach (IndexInspectionField field in fields)
            writer.WriteLine($"{field.Name}: indexed={field.Indexed}, stored={field.Stored}, docValues={field.HasDocValues}, docs={field.DocumentCount}, terms={field.UniqueTermCount}, tf={field.TotalTermFrequency}, vector={field.VectorDimension}, spatial={field.SpatialKind}");
    }

    internal static void WriteTerms(TextWriter writer, IReadOnlyList<IndexInspectionTerm> terms)
    {
        foreach (IndexInspectionTerm term in terms)
            writer.WriteLine($"{term.Term}\tdf={term.DocumentFrequency}\ttf={term.TotalTermFrequency}");
    }

    internal static void WritePostings(TextWriter writer, IReadOnlyList<IndexInspectionPosting> postings)
    {
        foreach (IndexInspectionPosting posting in postings)
            writer.WriteLine($"doc={posting.DocumentId}\tsegment={posting.SegmentId}:{posting.SegmentDocumentId}\tfreq={posting.Frequency}\tpositions={string.Join(",", posting.Positions)}");
    }

    internal static void WriteDocument(TextWriter writer, IndexInspectionDocument document)
    {
        writer.WriteLine($"Document {document.DocumentId} ({(document.IsLive ? "live" : "deleted")}) in {document.SegmentId}:{document.SegmentDocumentId}");
        foreach ((string name, IReadOnlyList<string> values) in document.StoredFields)
            writer.WriteLine($"  {name}: {string.Join(" | ", values)}");
        foreach ((string name, IReadOnlyList<int> lengths) in document.BinaryFieldLengths)
            writer.WriteLine($"  {name}: binary lengths [{string.Join(", ", lengths)}]");
        foreach ((string name, IReadOnlyList<float> values) in document.Vectors)
            writer.WriteLine($"  {name}: {values.Count} dimensions");
    }

    private static void AddChildren(Tree parent, IReadOnlyList<Explanation> children)
    {
        foreach (Explanation child in children)
        {
            var node = parent.AddNode($"{child.Score:G7}  {Markup.Escape(child.Description)}");
            AddChildren(node, child.Details);
        }
    }

    private static void AddChildren(TreeNode parent, IReadOnlyList<Explanation> children)
    {
        foreach (Explanation child in children)
        {
            var node = parent.AddNode($"{child.Score:G7}  {Markup.Escape(child.Description)}");
            AddChildren(node, child.Details);
        }
    }

    private static string Yes(bool value) => value ? "yes" : "no";
    private static string Value<T>(T? value) where T : struct => value?.ToString() ?? "unknown";
    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
    };
}
