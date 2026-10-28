using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Cli.Rendering;
using Rowles.LeanCorpus.Index.Inspection;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Store;
using Spectre.Console;

namespace Rowles.LeanCorpus.Cli;

internal static class WorkbenchBrowser
{
    private static readonly string[] Views =
    [
        "Overview", "Segments", "Fields", "Terms and postings", "Document", "Search",
        "Vectors", "Facets", "Geo", "Files and codecs", "Storage", "Analyse text", "Exit"
    ];

    internal static int Launch(IAnsiConsole console)
    {
        console.Write(new Panel(new Text("Explore committed indexes, documents and search results."))
            .Header("[bold cyan]LeanCorpus CLI Workbench[/]").Border(BoxBorder.Rounded));
        string path = console.Prompt(new TextPrompt<string>(PromptTitle("Index directory:")).Validate(static value =>
            Directory.Exists(value) ? ValidationResult.Success() : ValidationResult.Error("Directory does not exist.")));
        using var inspector = new IndexInspector(path);
        return Browse(console, inspector);
    }

    internal static int Browse(IAnsiConsole console, IndexInspector inspector)
    {
        while (true)
        {
            string segmentLabel = inspector.Overview.SegmentCount == 1 ? "segment" : "segments";
            console.Write(new Panel(new Markup($"[bold]Index Explorer[/]\n[grey]{Markup.Escape(inspector.Overview.DirectoryPath)}[/]\n{inspector.Overview.LiveDocumentCount:N0} live documents | {inspector.Overview.SegmentCount:N0} {segmentLabel}"))
                .Header("[bold cyan]LeanCorpus CLI Workbench[/]").Border(BoxBorder.Rounded));
            string selected = console.Prompt(new SelectionPrompt<string>()
                .Title(PromptTitle("Choose a view"))
                .HighlightStyle(new Style(Color.Cyan1))
                .PageSize(14)
                .AddChoices(Views));
            try
            {
                switch (selected)
                {
                    case "Overview":
                        IndexInspectionRenderer.Overview(console, inspector.Overview);
                        break;
                    case "Segments":
                        IndexInspectionRenderer.Segments(console, inspector.GetSegments());
                        break;
                    case "Fields":
                        IndexInspectionRenderer.Fields(console, inspector.GetFields());
                        break;
                    case "Terms and postings":
                        BrowseTerms(console, inspector);
                        break;
                    case "Document":
                        BrowseDocument(console, inspector);
                        break;
                    case "Search":
                        BrowseSearch(console, inspector.Overview.DirectoryPath);
                        break;
                    case "Vectors":
                        IndexInspectionRenderer.Fields(console, inspector.GetFields().Where(static field => field.VectorDimension is not null).ToArray());
                        break;
                    case "Facets":
                        IndexInspectionRenderer.Fields(console, inspector.GetFields().Where(static field => field.HasOrdinalDocValues).ToArray());
                        break;
                    case "Geo":
                        IndexInspectionRenderer.Fields(console, inspector.GetFields().Where(static field => field.SpatialKind is not null).ToArray());
                        break;
                    case "Files and codecs":
                        BrowseFiles(console, inspector);
                        break;
                    case "Storage":
                        BrowseStorage(console, inspector);
                        break;
                    case "Analyse text":
                        BrowseAnalysis(console);
                        break;
                    case "Exit":
                        return 0;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException or QueryParseException)
            {
                console.Write(new Panel(new Text(exception.Message)).Header("[yellow]Unable to open this view[/]").Border(BoxBorder.Rounded));
            }
            console.WriteLine();
        }
    }

    private static string PromptTitle(string title) => $"[bold cyan]LeanCorpus CLI[/] [grey]| Index Explorer[/]\n{title}";

    private static void BrowseTerms(IAnsiConsole console, IndexInspector inspector)
    {
        string[] indexedFields = inspector.GetFields().Where(static field => field.Indexed).Select(static field => field.Name).ToArray();
        if (indexedFields.Length == 0)
        {
            console.MarkupLine("[yellow]No indexed fields are available.[/]");
            return;
        }
        string field = console.Prompt(new SelectionPrompt<string>().Title(PromptTitle("Field")).AddChoices(indexedFields).UseConverter(Markup.Escape));
        string prefix = console.Prompt(new TextPrompt<string>(PromptTitle("Term prefix (leave empty for all terms):")).AllowEmpty());
        IndexInspectionTerm[] terms = inspector.GetTerms(field, string.IsNullOrEmpty(prefix) ? null : prefix, 200).ToArray();
        IndexInspectionRenderer.Terms(console, terms);
        if (terms.Length == 0)
            return;
        bool inspect = console.Prompt(new ConfirmationPrompt(PromptTitle("Inspect postings for one of these terms?")) { DefaultValue = false });
        if (!inspect)
            return;
        string term = console.Prompt(new SelectionPrompt<string>().Title(PromptTitle("Term")).PageSize(15).AddChoices(terms.Select(static value => value.Term)).UseConverter(Markup.Escape));
        IndexInspectionRenderer.Postings(console, inspector.GetPostings(field, term, 200));
    }

    private static void BrowseDocument(IAnsiConsole console, IndexInspector inspector)
    {
        int documentId = console.Prompt(new TextPrompt<int>(PromptTitle("Global document ID:")));
        IndexInspectionRenderer.Document(console, inspector.GetDocument(documentId));
    }

    private static void BrowseSearch(IAnsiConsole console, string indexPath)
    {
        string field = console.Prompt(new TextPrompt<string>(PromptTitle("Default field:")).DefaultValue("body"));
        string query = console.Prompt(new TextPrompt<string>(PromptTitle("Query:")));
        using var directory = new MMapDirectory(indexPath);
        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(query, field, 20, new StandardAnalyser());
        console.MarkupLine($"[bold]{results.TotalHits:N0} hits[/]{(results.IsPartial ? " (partial)" : "")}");
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Rank").AddColumn("Document").AddColumn("Score").AddColumn("Stored fields");
        for (int i = 0; i < results.ScoreDocs.Length; i++)
        {
            var hit = results.ScoreDocs[i];
            var fields = searcher.GetStoredFields(hit.DocId);
            table.AddRow((i + 1).ToString(), hit.DocId.ToString(), hit.Score.ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
                Markup.Escape(string.Join("; ", fields.Select(static pair => $"{pair.Key}={string.Join('|', pair.Value)}"))));
        }
        console.Write(table);
    }

    private static void BrowseFiles(IAnsiConsole console, IndexInspector inspector)
    {
        var table = new Table().Border(TableBorder.Rounded).AddColumn("File").AddColumn("Codec").AddColumn("Version").AddColumn("Current").AddColumn("Bytes");
        foreach (var file in inspector.GetFormatInventory().Segments.SelectMany(static segment => segment.Files)
                     .Concat(inspector.GetFormatInventory().OrphanFiles).OrderBy(static file => file.FileName, StringComparer.Ordinal))
            table.AddRow(Markup.Escape(file.FileName), Markup.Escape(file.CodecName), file.FormatVersion?.ToString() ?? "n/a", file.IsCurrent ? "yes" : "no", file.Length?.ToString("N0") ?? "unknown");
        console.Write(table);
    }

    private static void BrowseStorage(IAnsiConsole console, IndexInspector inspector)
    {
        var report = inspector.GetSizeReport();
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Segment").AddColumn("Bytes");
        foreach (var segment in report.Segments)
            table.AddRow(Markup.Escape(segment.SegmentName), segment.TotalSizeBytes.ToString("N0"));
        table.AddRow("Total", report.TotalSizeBytes.ToString("N0"));
        console.Write(table);
    }

    private static void BrowseAnalysis(IAnsiConsole console)
    {
        string text = console.Prompt(new TextPrompt<string>(PromptTitle("Text to analyse:")));
        var sink = new BrowserTokenSink();
        new StandardAnalyser().Analyse(text.AsSpan(), sink);
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Token").AddColumn("Offsets").AddColumn("Type");
        foreach (var token in sink.Tokens)
            table.AddRow(Markup.Escape(token.Text), $"{token.StartOffset}..{token.EndOffset}", Markup.Escape(token.Type));
        console.Write(table);
    }

    private sealed class BrowserTokenSink : Rowles.LeanCorpus.Analysis.ISpanTokenSink
    {
        internal List<Rowles.LeanCorpus.Analysis.Token> Tokens { get; } = [];
        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type = Rowles.LeanCorpus.Analysis.Token.DefaultType,
            int positionIncrement = 1, byte[]? payload = null)
            => Tokens.Add(new Rowles.LeanCorpus.Analysis.Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
    }
}
