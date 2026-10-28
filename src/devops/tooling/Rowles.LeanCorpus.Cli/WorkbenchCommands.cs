using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Cli.Rendering;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Inspection;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Spectre.Console;

namespace Rowles.LeanCorpus.Cli;

/// <summary>Registers the additive inspection and research workbench command tree.</summary>
internal static partial class WorkbenchCommands
{
    internal static void AddTo(RootCommand root)
    {
        root.Add(BuildIndexCommand());
        root.Add(BuildSegmentCommand());
        root.Add(BuildFieldCommand());
        root.Add(BuildTermCommand());
        root.Add(BuildDocumentCommand());
        root.Add(BuildSearchCommand());
        root.Add(BuildAnalyseCommand());
        root.Add(BuildVectorCommand());
        root.Add(BuildFacetCommand());
        root.Add(BuildGeoCommand());
        root.Add(BuildBrowseCommand());
        root.Add(BuildCreateCommand());
        root.Add(BuildTestLabCommand());
        root.Add(BuildTransformCommand());
    }

    internal static void SetRootAction(RootCommand root)
    {
        root.SetAction(result =>
        {
            if (CanPrompt(result.InvocationConfiguration.Output))
                return WorkbenchBrowser.Launch(AnsiConsole.Console);
            result.InvocationConfiguration.Output.WriteLine("LeanCorpus CLI workbench. Use --help to list commands, or run 'browse <index-path>'.");
            return CliExitCodes.Success;
        });
    }

    private static Command BuildIndexCommand()
    {
        var command = new Command("index", "Inspect, compare, fingerprint and report on an index.");
        command.Add(BuildIndexInfoCommand("info"));
        command.Add(BuildIndexFilesCommand());
        command.Add(BuildIndexSizeCommand());
        command.Add(BuildFingerprintCommand());
        command.Add(BuildDiffCommand());
        command.Add(BuildCheckAliasCommand());
        command.Add(BuildCompatAliasCommand());
        return command;
    }

    private static Command BuildIndexInfoCommand(string name)
    {
        var path = new Argument<string>("index-path") { Description = "Path to the LeanCorpus index directory." };
        var flags = AddRenderOptions(new Command(name, "Show a committed index overview."));
        flags.Command.Add(path);
        flags.Command.SetAction(result => RunIndexInfo(GetRequiredValue(result, path), result.GetValue(flags.Json), result.GetValue(flags.Plain), result.GetValue(flags.NoColor), result.InvocationConfiguration.Output));
        return flags.Command;
    }

    private static Command BuildIndexFilesCommand()
    {
        var path = new Argument<string>("index-path") { Description = "Path to the LeanCorpus index directory." };
        var flags = AddRenderOptions(new Command("files", "List index files and codec metadata."));
        flags.Command.Add(path);
        flags.Command.SetAction(result => RunIndexFiles(GetRequiredValue(result, path), result.GetValue(flags.Json), result.GetValue(flags.Plain), result.GetValue(flags.NoColor), result.InvocationConfiguration.Output));
        return flags.Command;
    }

    private static Command BuildIndexSizeCommand()
    {
        var path = new Argument<string>("index-path") { Description = "Path to the LeanCorpus index directory." };
        var flags = AddRenderOptions(new Command("size", "Show index storage usage by segment."));
        flags.Command.Add(path);
        flags.Command.SetAction(result => RunIndexSize(GetRequiredValue(result, path), result.GetValue(flags.Json), result.GetValue(flags.Plain), result.GetValue(flags.NoColor), result.InvocationConfiguration.Output));
        return flags.Command;
    }

    private static Command BuildFingerprintCommand()
    {
        var path = new Argument<string>("index-path") { Description = "Path to the LeanCorpus index directory." };
        var json = Flag("--json", "Write the fingerprint as JSON.");
        var command = new Command("fingerprint", "Calculate logical and physical index fingerprints.");
        command.Add(path);
        command.Add(json);
        command.SetAction(result => RunFingerprint(GetRequiredValue(result, path), result.GetValue(json), result.InvocationConfiguration.Output));
        return command;
    }

    private static Command BuildDiffCommand()
    {
        var before = new Argument<string>("before") { Description = "First index directory." };
        var after = new Argument<string>("after") { Description = "Second index directory." };
        var logical = Flag("--logical", "Compare logical content and ignore physical segment layout.");
        var json = Flag("--json", "Write the diff as JSON.");
        var command = new Command("diff", "Compare index contents, metadata and storage.");
        command.Add(before);
        command.Add(after);
        command.Add(logical);
        command.Add(json);
        command.SetAction(result => RunDiff(GetRequiredValue(result, before), GetRequiredValue(result, after), result.GetValue(logical), result.GetValue(json), result.InvocationConfiguration.Output));
        return command;
    }

    private static Command BuildCheckAliasCommand()
    {
        var command = new Command("check", "Validate a LeanCorpus index (alias for the existing check command).");
        var path = new Argument<string>("index-path");
        command.Add(path);
        command.SetAction(result =>
        {
            CheckRequest request = IndexCheckerCli.CreateRequest(GetRequiredValue(result, path), false, false,
                false, false, false, false, false, false, false, false, null);
            return IndexCheckerCli.RunCheck(request, result.InvocationConfiguration.Output, result.InvocationConfiguration.Error);
        });
        return command;
    }

    private static Command BuildCompatAliasCommand()
    {
        var command = new Command("compat", "Check index compatibility (alias for the existing compat command).");
        var path = new Argument<string>("index-path");
        command.Add(path);
        command.SetAction(result => IndexCheckerCli.Run(["compat", GetRequiredValue(result, path)],
            result.InvocationConfiguration.Output, result.InvocationConfiguration.Error));
        return command;
    }

    private static Command BuildSegmentCommand()
    {
        var command = new Command("segment", "Explore committed segments.");
        var listPath = new Argument<string>("index-path");
        var listFlags = AddRenderOptions(new Command("list", "List committed segments."));
        listFlags.Command.Add(listPath);
        listFlags.Command.SetAction(result => RunSegments(GetRequiredValue(result, listPath), null, result.GetValue(listFlags.Json), result.GetValue(listFlags.Plain), result.GetValue(listFlags.NoColor), result.InvocationConfiguration.Output));
        command.Add(listFlags.Command);

        var inspectPath = new Argument<string>("index-path");
        var segmentId = new Argument<string>("segment-id");
        var inspectFlags = AddRenderOptions(new Command("inspect", "Inspect one segment and its files."));
        inspectFlags.Command.Add(inspectPath);
        inspectFlags.Command.Add(segmentId);
        inspectFlags.Command.SetAction(result => RunSegments(GetRequiredValue(result, inspectPath), GetRequiredValue(result, segmentId), result.GetValue(inspectFlags.Json), result.GetValue(inspectFlags.Plain), result.GetValue(inspectFlags.NoColor), result.InvocationConfiguration.Output));
        command.Add(inspectFlags.Command);
        return command;
    }

    private static Command BuildFieldCommand()
    {
        var command = new Command("field", "Explore indexed fields and capabilities.");
        var listPath = new Argument<string>("index-path");
        var listFlags = AddRenderOptions(new Command("list", "List fields."));
        listFlags.Command.Add(listPath);
        listFlags.Command.SetAction(result => RunFields(GetRequiredValue(result, listPath), null, result.GetValue(listFlags.Json), result.GetValue(listFlags.Plain), result.GetValue(listFlags.NoColor), result.InvocationConfiguration.Output));
        command.Add(listFlags.Command);

        var inspectPath = new Argument<string>("index-path");
        var fieldName = new Argument<string>("field");
        var inspectFlags = AddRenderOptions(new Command("inspect", "Show field capabilities and term statistics."));
        inspectFlags.Command.Add(inspectPath);
        inspectFlags.Command.Add(fieldName);
        inspectFlags.Command.SetAction(result => RunFields(GetRequiredValue(result, inspectPath), GetRequiredValue(result, fieldName), result.GetValue(inspectFlags.Json), result.GetValue(inspectFlags.Plain), result.GetValue(inspectFlags.NoColor), result.InvocationConfiguration.Output));
        command.Add(inspectFlags.Command);
        return command;
    }

    private static Command BuildTermCommand()
    {
        var command = new Command("term", "Browse terms and inspect postings.");
        var path = new Argument<string>("index-path");
        var field = new Argument<string>("field");
        var prefix = new Option<string?>("--prefix") { Description = "Only list terms beginning with this text." };
        var take = new Option<int>("--take") { Description = "Maximum terms to return (default 100).", DefaultValueFactory = _ => 100 };
        var flags = AddRenderOptions(new Command("list", "List terms in sorted order."));
        flags.Command.Add(path);
        flags.Command.Add(field);
        flags.Command.Add(prefix);
        flags.Command.Add(take);
        flags.Command.SetAction(result => RunTerms(GetRequiredValue(result, path), GetRequiredValue(result, field), result.GetValue(prefix), result.GetValue(take), result.GetValue(flags.Json), result.GetValue(flags.Plain), result.GetValue(flags.NoColor), result.InvocationConfiguration.Output));
        command.Add(flags.Command);

        var postingPath = new Argument<string>("index-path");
        var postingField = new Argument<string>("field");
        var term = new Argument<string>("term");
        var postingTake = new Option<int>("--take") { Description = "Maximum postings to return (default 1000).", DefaultValueFactory = _ => 1000 };
        var postingFlags = AddRenderOptions(new Command("postings", "Show live document IDs, term frequencies and positions."));
        postingFlags.Command.Add(postingPath);
        postingFlags.Command.Add(postingField);
        postingFlags.Command.Add(term);
        postingFlags.Command.Add(postingTake);
        postingFlags.Command.SetAction(result => RunPostings(GetRequiredValue(result, postingPath), GetRequiredValue(result, postingField), GetRequiredValue(result, term), result.GetValue(postingTake), result.GetValue(postingFlags.Json), result.GetValue(postingFlags.Plain), result.GetValue(postingFlags.NoColor), result.InvocationConfiguration.Output));
        command.Add(postingFlags.Command);
        return command;
    }

    private static Command BuildDocumentCommand()
    {
        var path = new Argument<string>("index-path");
        var id = new Argument<int>("document-id");
        var flags = AddRenderOptions(new Command("get", "Retrieve a document by global document ID."));
        flags.Command.Add(path);
        flags.Command.Add(id);
        flags.Command.SetAction(result => RunDocument(GetRequiredValue(result, path), GetRequiredValue(result, id), result.GetValue(flags.Json), result.GetValue(flags.Plain), result.GetValue(flags.NoColor), result.InvocationConfiguration.Output));
        var command = new Command("document", "Explore stored documents.");
        command.Add(flags.Command);
        return command;
    }

    private static Command BuildSearchCommand()
    {
        var command = new Command("search", "Run, explain and compare searches.");
        var path = new Argument<string>("index-path");
        var query = new Argument<string>("query") { Description = "LeanCorpus query string." };
        var field = new Option<string>("--field") { Description = "Default query field (default body).", DefaultValueFactory = _ => "body" };
        var take = new Option<int>("--take") { Description = "Maximum results to return (default 10).", DefaultValueFactory = _ => 10 };
        var json = Flag("--json", "Write result rows as JSON.");
        var plain = Flag("--plain", "Use plain text output.");
        var run = new Command("run", "Search a committed index.");
        run.Add(path);
        run.Add(query);
        run.Add(field);
        run.Add(take);
        run.Add(json);
        run.Add(plain);
        run.SetAction(result => RunSearch(GetRequiredValue(result, path), GetRequiredValue(result, query), result.GetValue(field) ?? "body", result.GetValue(take), result.GetValue(json), result.GetValue(plain), result.InvocationConfiguration.Output));
        command.Add(run);

        var explainPath = new Argument<string>("index-path");
        var explainField = new Argument<string>("field");
        var explainTerm = new Argument<string>("term");
        var docId = new Argument<int>("document-id");
        var explain = new Command("explain", "Explain the BM25 score for a term and document.");
        explain.Add(explainPath);
        explain.Add(explainField);
        explain.Add(explainTerm);
        explain.Add(docId);
        explain.SetAction(result => RunExplain(GetRequiredValue(result, explainPath), GetRequiredValue(result, explainField), GetRequiredValue(result, explainTerm), GetRequiredValue(result, docId), result.InvocationConfiguration.Output));
        command.Add(explain);
        AddSearchResearchCommands(command);
        return command;
    }

    private static Command BuildAnalyseCommand()
    {
        var text = new Argument<string>("text") { Description = "Text to analyse." };
        var json = Flag("--json", "Write token data as JSON.");
        var plain = Flag("--plain", "Use plain text output.");
        var command = new Command("analyse", "Tokenise and analyse text with the standard analyser.");
        command.Add(text);
        command.Add(json);
        command.Add(plain);
        command.SetAction(result => RunAnalyse(GetRequiredValue(result, text), result.GetValue(json), result.GetValue(plain), result.InvocationConfiguration.Output));
        return command;
    }

    private static Command BuildVectorCommand()
    {
        var path = new Argument<string>("index-path");
        var flags = AddRenderOptions(new Command("inspect", "List vector fields and HNSW metadata."));
        flags.Command.Add(path);
        flags.Command.SetAction(result => RunVectorInfo(GetRequiredValue(result, path), result.GetValue(flags.Json), result.GetValue(flags.Plain), result.GetValue(flags.NoColor), result.InvocationConfiguration.Output));
        var command = new Command("vector", "Inspect vector fields and run nearest-neighbour queries.");
        command.Add(flags.Command);

        var neighbourPath = new Argument<string>("index-path");
        var field = new Argument<string>("field");
        var doc = new Argument<int>("document-id");
        var take = new Option<int>("--take") { Description = "Maximum neighbours (default 10).", DefaultValueFactory = _ => 10 };
        var neighbours = new Command("neighbours", "Find nearest neighbours to a stored document vector.");
        neighbours.Add(neighbourPath);
        neighbours.Add(field);
        neighbours.Add(doc);
        neighbours.Add(take);
        neighbours.SetAction(result => RunVectorNeighbours(GetRequiredValue(result, neighbourPath), GetRequiredValue(result, field), GetRequiredValue(result, doc), result.GetValue(take), result.InvocationConfiguration.Output));
        command.Add(neighbours);

        var explainPath = new Argument<string>("index-path");
        var explainField = new Argument<string>("field");
        var explainDocument = new Argument<int>("document-id");
        var explainJson = Flag("--json", "Write the vector explanation as JSON.");
        var explain = new Command("explain", "Explain vector similarity and the selected search strategy for a document.");
        explain.Add(explainPath);
        explain.Add(explainField);
        explain.Add(explainDocument);
        explain.Add(explainJson);
        explain.SetAction(result => RunVectorExplain(GetRequiredValue(result, explainPath), GetRequiredValue(result, explainField),
            GetRequiredValue(result, explainDocument), result.GetValue(explainJson), result.InvocationConfiguration.Output));
        command.Add(explain);
        return command;
    }

    private static Command BuildFacetCommand()
    {
        var path = new Argument<string>("index-path");
        var field = new Argument<string?>("field") { Arity = ArgumentArity.ZeroOrOne };
        var flags = AddRenderOptions(new Command("inspect", "List ordinal DocValues fields and their indexed buckets."));
        flags.Command.Add(path);
        flags.Command.Add(field);
        flags.Command.SetAction(result => RunFacetInfo(GetRequiredValue(result, path), result.GetValue(field), result.GetValue(flags.Json), result.GetValue(flags.Plain), result.GetValue(flags.NoColor), result.InvocationConfiguration.Output));
        var command = new Command("facet", "Explore candidate facet fields and indexed bucket counts.");
        command.Add(flags.Command);
        return command;
    }

    private static Command BuildGeoCommand()
    {
        var path = new Argument<string>("index-path");
        var field = new Argument<string?>("field") { Arity = ArgumentArity.ZeroOrOne };
        var flags = AddRenderOptions(new Command("inspect", "List supported spatial fields and kinds."));
        flags.Command.Add(path);
        flags.Command.Add(field);
        flags.Command.SetAction(result => RunGeoInfo(GetRequiredValue(result, path), result.GetValue(field), result.GetValue(flags.Json), result.GetValue(flags.Plain), result.GetValue(flags.NoColor), result.InvocationConfiguration.Output));
        var command = new Command("geo", "Explore Geo and XY field metadata.");
        command.Add(flags.Command);

        var queryPath = new Argument<string>("index-path");
        var queryField = new Argument<string>("field");
        var latitude = new Argument<double>("latitude");
        var longitude = new Argument<double>("longitude");
        var radius = new Option<double>("--radius-metres") { Description = "Search radius in metres.", DefaultValueFactory = _ => 1_000d };
        var take = new Option<int>("--take") { Description = "Maximum results (default 10).", DefaultValueFactory = _ => 10 };
        var queryFlags = AddRenderOptions(new Command("query", "Find geo points within a radius of a centre point."));
        queryFlags.Command.Add(queryPath);
        queryFlags.Command.Add(queryField);
        queryFlags.Command.Add(latitude);
        queryFlags.Command.Add(longitude);
        queryFlags.Command.Add(radius);
        queryFlags.Command.Add(take);
        queryFlags.Command.SetAction(result => RunGeoQuery(GetRequiredValue(result, queryPath), GetRequiredValue(result, queryField),
            GetRequiredValue(result, latitude), GetRequiredValue(result, longitude), result.GetValue(radius), result.GetValue(take),
            result.GetValue(queryFlags.Json), result.GetValue(queryFlags.Plain), result.GetValue(queryFlags.NoColor), result.InvocationConfiguration.Output));
        command.Add(queryFlags.Command);
        return command;
    }

    private static Command BuildBrowseCommand()
    {
        var path = new Argument<string>("index-path") { Description = "Path to the LeanCorpus index directory." };
        var command = new Command("browse", "Open an interactive index browser, or print an overview when redirected.");
        command.Add(path);
        command.SetAction(result => RunBrowse(GetRequiredValue(result, path), result.InvocationConfiguration.Output));
        return command;
    }

    private static Command BuildCreateCommand() => BuildCreateCommands();
    private static Command BuildTestLabCommand() => BuildTestLabCommands();
    private static Command BuildTransformCommand() => BuildTransformCommands();

    private static int RunIndexInfo(string path, bool json, bool plain, bool noColor, TextWriter output)
    {
        using var inspector = new IndexInspector(path);
        IndexInspectionOverview overview = inspector.Overview;
        if (json)
            WriteJson(output, overview);
        else if (CanUseRich(output, plain, noColor))
            IndexInspectionRenderer.Overview(AnsiConsole.Console, overview);
        else
            IndexInspectionRenderer.WriteOverview(output, overview);
        return overview.HasUnsupportedFutureFormat ? CliExitCodes.ValidationErrors : CliExitCodes.Success;
    }

    private static int RunIndexFiles(string path, bool json, bool plain, bool noColor, TextWriter output)
    {
        using var inspector = new IndexInspector(path);
        var inventory = inspector.GetFormatInventory();
        var files = inventory.Segments.SelectMany(static segment => segment.Files)
            .Concat(inventory.OrphanFiles)
            .OrderBy(static file => file.FileName, StringComparer.Ordinal)
            .ToArray();
        if (json)
            WriteJson(output, files);
        else if (CanUseRich(output, plain, noColor))
        {
            var table = new Table().Border(TableBorder.Rounded).AddColumn("File").AddColumn("Codec").AddColumn("Version").AddColumn("Current").AddColumn("Supported").AddColumn("Bytes").AddColumn("Location");
            foreach (var file in files)
                table.AddRow(Markup.Escape(file.FileName), Markup.Escape(file.CodecName), file.FormatVersion?.ToString() ?? "n/a", file.IsCurrent ? "yes" : "no", file.IsSupported ? "yes" : "no", file.Length?.ToString("N0") ?? "unknown", file.PhysicalLocation.ToString());
            AnsiConsole.Console.Write(table);
        }
        else
            foreach (var file in files)
                output.WriteLine($"{file.FileName}\t{file.CodecName}\tversion={file.FormatVersion?.ToString() ?? "n/a"}\tcurrent={file.IsCurrent}\tsupported={file.IsSupported}\tbytes={file.Length?.ToString() ?? "unknown"}\t{file.PhysicalLocation}");
        return inventory.HasUnsupportedFutureFormat ? CliExitCodes.ValidationErrors : CliExitCodes.Success;
    }

    private static int RunIndexSize(string path, bool json, bool plain, bool noColor, TextWriter output)
    {
        using var inspector = new IndexInspector(path);
        var report = inspector.GetSizeReport();
        if (json)
            WriteJson(output, report);
        else if (CanUseRich(output, plain, noColor))
        {
            var table = new Table().Border(TableBorder.Rounded).AddColumn("Segment").AddColumn("Documents").AddColumn("Total bytes");
            foreach (var segment in report.Segments)
                table.AddRow(Markup.Escape(segment.SegmentName), "", segment.TotalSizeBytes.ToString("N0"));
            table.AddRow("Total", "", report.TotalSizeBytes.ToString("N0"));
            AnsiConsole.Console.Write(table);
        }
        else
        {
            output.WriteLine($"Total: {report.TotalSizeFormatted} ({report.TotalSizeBytes} bytes)");
            foreach (var segment in report.Segments)
                output.WriteLine($"{segment.SegmentName}: {segment.TotalSizeBytes} bytes");
        }
        return CliExitCodes.Success;
    }

    private static int RunSegments(string path, string? segmentId, bool json, bool plain, bool noColor, TextWriter output)
    {
        using var inspector = new IndexInspector(path);
        IReadOnlyList<IndexInspectionSegment> segments = inspector.GetSegments();
        if (segmentId is not null)
            segments = segments.Where(segment => string.Equals(segment.SegmentId, segmentId, StringComparison.Ordinal)).ToArray();
        if (segmentId is not null && segments.Count == 0)
            return WriteNotFound(output, $"Segment '{segmentId}' was not found.");
        if (json)
            WriteJson(output, segments);
        else if (CanUseRich(output, plain, noColor))
            IndexInspectionRenderer.Segments(AnsiConsole.Console, segments);
        else
            IndexInspectionRenderer.WriteSegments(output, segments);
        return CliExitCodes.Success;
    }

    private static int RunFields(string path, string? fieldName, bool json, bool plain, bool noColor, TextWriter output)
    {
        using var inspector = new IndexInspector(path);
        IReadOnlyList<IndexInspectionField> fields = inspector.GetFields();
        if (fieldName is not null)
            fields = fields.Where(field => string.Equals(field.Name, fieldName, StringComparison.Ordinal)).ToArray();
        if (fieldName is not null && fields.Count == 0)
            return WriteNotFound(output, $"Field '{fieldName}' was not found.");
        if (json)
            WriteJson(output, fields);
        else if (CanUseRich(output, plain, noColor))
            IndexInspectionRenderer.Fields(AnsiConsole.Console, fields);
        else
            IndexInspectionRenderer.WriteFields(output, fields);
        return CliExitCodes.Success;
    }

    private static int RunTerms(string path, string field, string? prefix, int take, bool json, bool plain, bool noColor, TextWriter output)
    {
        using var inspector = new IndexInspector(path);
        IReadOnlyList<IndexInspectionTerm> terms = inspector.GetTerms(field, prefix, take);
        if (json)
            WriteJson(output, terms);
        else if (CanUseRich(output, plain, noColor))
            IndexInspectionRenderer.Terms(AnsiConsole.Console, terms);
        else
            IndexInspectionRenderer.WriteTerms(output, terms);
        return CliExitCodes.Success;
    }

    private static int RunPostings(string path, string field, string term, int take, bool json, bool plain, bool noColor, TextWriter output)
    {
        using var inspector = new IndexInspector(path);
        IReadOnlyList<IndexInspectionPosting> postings = inspector.GetPostings(field, term, take);
        if (json)
            WriteJson(output, postings);
        else if (CanUseRich(output, plain, noColor))
            IndexInspectionRenderer.Postings(AnsiConsole.Console, postings);
        else
            IndexInspectionRenderer.WritePostings(output, postings);
        return CliExitCodes.Success;
    }

    private static int RunDocument(string path, int id, bool json, bool plain, bool noColor, TextWriter output)
    {
        using var inspector = new IndexInspector(path);
        IndexInspectionDocument document = inspector.GetDocument(id);
        if (json)
            WriteJson(output, document);
        else if (CanUseRich(output, plain, noColor))
            IndexInspectionRenderer.Document(AnsiConsole.Console, document);
        else
            IndexInspectionRenderer.WriteDocument(output, document);
        return CliExitCodes.Success;
    }

    private static int RunSearch(string path, string query, string field, int take, bool json, bool plain, TextWriter output)
    {
        var watch = Stopwatch.StartNew();
        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory);
        TopDocs results = searcher.Search(query, field, take);
        watch.Stop();
        var rows = results.ScoreDocs.Select(hit => new SearchRow(hit.DocId, hit.Score, searcher.GetStoredFields(hit.DocId))).ToArray();
        var report = new SearchReport(query, field, results.TotalHits, rows, watch.Elapsed.TotalMilliseconds, results.IsPartial);
        if (json)
            WriteJson(output, report);
        else if (CanUseRich(output, plain, noColor: false))
        {
            var table = new Table().Border(TableBorder.Rounded).AddColumn("Rank").AddColumn("Doc ID").AddColumn("Score").AddColumn("Stored fields");
            for (int i = 0; i < rows.Length; i++)
                table.AddRow((i + 1).ToString(), rows[i].DocumentId.ToString(), rows[i].Score.ToString("F5", System.Globalization.CultureInfo.InvariantCulture), Markup.Escape(string.Join("; ", rows[i].StoredFields.Select(static pair => $"{pair.Key}={string.Join('|', pair.Value)}"))));
            AnsiConsole.Console.MarkupLine($"[bold]{results.TotalHits:N0} hits[/] in {watch.Elapsed.TotalMilliseconds:F2} ms{(results.IsPartial ? " (partial)" : "")}");
            AnsiConsole.Console.Write(table);
        }
        else
        {
            output.WriteLine($"{results.TotalHits} hits in {watch.Elapsed.TotalMilliseconds:F2} ms{(results.IsPartial ? " (partial)" : "")}");
            foreach (SearchRow row in rows)
                output.WriteLine($"doc={row.DocumentId}\tscore={row.Score.ToString("G7", System.Globalization.CultureInfo.InvariantCulture)}\t{string.Join("; ", row.StoredFields.Select(static pair => $"{pair.Key}={string.Join('|', pair.Value)}"))}");
        }
        return CliExitCodes.Success;
    }

    private static int RunExplain(string path, string field, string term, int documentId, TextWriter output)
    {
        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory);
        Explanation? explanation = searcher.Explain(new TermQuery(field, term), documentId);
        if (explanation is null)
            return WriteNotFound(output, $"Document {documentId} does not match '{field}:{term}'.");
        if (CanUseRich(output, plain: false, noColor: false))
            IndexInspectionRenderer.Explanation(AnsiConsole.Console, explanation);
        else
            WriteExplanation(output, explanation, 0);
        return CliExitCodes.Success;
    }

    private static int RunAnalyse(string text, bool json, bool plain, TextWriter output)
    {
        var sink = new MaterialisedTokenSink();
        new StandardAnalyser().Analyse(text.AsSpan(), sink);
        var report = sink.Tokens.Select((token, index) => new AnalysisToken(index, token.Text, token.StartOffset, token.EndOffset, token.Type, token.PositionIncrement, token.PositionLength)).ToArray();
        if (json)
            WriteJson(output, report);
        else if (CanUseRich(output, plain, noColor: false))
        {
            var table = new Table().Border(TableBorder.Rounded).AddColumn("#").AddColumn("Token").AddColumn("Offsets").AddColumn("Position +/length").AddColumn("Type");
            foreach (AnalysisToken token in report)
                table.AddRow(token.Ordinal.ToString(), Markup.Escape(token.Text), $"{token.StartOffset}..{token.EndOffset}", $"{token.PositionIncrement}/{token.PositionLength}", Markup.Escape(token.Type));
            AnsiConsole.Console.Write(table);
        }
        else
            foreach (AnalysisToken token in report)
                output.WriteLine($"{token.Ordinal}\t{token.Text}\t{token.StartOffset}..{token.EndOffset}\tpos+{token.PositionIncrement}/{token.PositionLength}\t{token.Type}");
        return CliExitCodes.Success;
    }

    private static int RunVectorInfo(string path, bool json, bool plain, bool noColor, TextWriter output)
    {
        using var inspector = new IndexInspector(path);
        IReadOnlyList<IndexInspectionField> fields = inspector.GetFields().Where(static field => field.VectorDimension is not null).ToArray();
        if (json)
            WriteJson(output, fields);
        else if (CanUseRich(output, plain, noColor))
            IndexInspectionRenderer.Fields(AnsiConsole.Console, fields);
        else
            IndexInspectionRenderer.WriteFields(output, fields);
        return CliExitCodes.Success;
    }

    private static int RunVectorNeighbours(string path, string field, int documentId, int take, TextWriter output)
    {
        using var directory = new MMapDirectory(path);
        using var inspector = new IndexInspector(path);
        using var searcher = new IndexSearcher(directory);
        IndexInspectionDocument document = inspector.GetDocument(documentId);
        if (!document.Vectors.TryGetValue(field, out IReadOnlyList<float>? vector))
            return WriteNotFound(output, $"Document {documentId} has no vector in field '{field}'.");
        TopDocs results = searcher.Search(new VectorQuery(field, vector.ToArray(), take), take);
        foreach (ScoreDoc hit in results.ScoreDocs)
            output.WriteLine($"doc={hit.DocId}\tsimilarity={hit.Score.ToString("G7", System.Globalization.CultureInfo.InvariantCulture)}");
        return CliExitCodes.Success;
    }

    private static int RunVectorExplain(string path, string field, int documentId, bool json, TextWriter output)
    {
        using var directory = new MMapDirectory(path);
        using var inspector = new IndexInspector(path);
        using var searcher = new IndexSearcher(directory);
        IndexInspectionDocument document = inspector.GetDocument(documentId);
        if (!document.Vectors.TryGetValue(field, out IReadOnlyList<float>? vector))
            return WriteNotFound(output, $"Document {documentId} has no vector in field '{field}'.");
        Explanation? explanation = searcher.Explain(new VectorQuery(field, vector.ToArray()), documentId);
        if (explanation is null)
            return WriteNotFound(output, $"Vector explanation is unavailable for document {documentId} in field '{field}'.");
        if (json)
            WriteJson(output, explanation);
        else if (CanUseRich(output, plain: false, noColor: false))
            IndexInspectionRenderer.Explanation(AnsiConsole.Console, explanation);
        else
            WriteExplanation(output, explanation, 0);
        return CliExitCodes.Success;
    }

    private static int RunGeoQuery(string path, string field, double latitude, double longitude, double radiusMetres,
        int take, bool json, bool plain, bool noColor, TextWriter output)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90 degrees.");
        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180 degrees.");
        if (!double.IsFinite(radiusMetres) || radiusMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(radiusMetres), "Radius must be a positive finite number of metres.");
        if (take is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(take), "Result count must be between 1 and 10,000.");
        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory);
        TopDocs hits = searcher.Search(new GeoDistanceQuery(field, latitude, longitude, radiusMetres), take);
        var rows = hits.ScoreDocs.Select(hit => new GeoSearchHit(hit.DocId, hit.Score, searcher.GetStoredFields(hit.DocId))).ToArray();
        var report = new GeoSearchReport(field, latitude, longitude, radiusMetres, hits.TotalHits, rows);
        if (json)
            WriteJson(output, report);
        else if (CanUseRich(output, plain, noColor))
        {
            var table = new Table().Border(TableBorder.Rounded).AddColumn("Rank").AddColumn("Document").AddColumn("Stored fields");
            for (int index = 0; index < rows.Length; index++)
                table.AddRow((index + 1).ToString(), rows[index].DocumentId.ToString(),
                    Markup.Escape(string.Join("; ", rows[index].StoredFields.Select(static pair => $"{pair.Key}={string.Join('|', pair.Value)}"))));
            AnsiConsole.Console.MarkupLine($"[bold]{hits.TotalHits:N0} points[/] within {radiusMetres:N0} m");
            AnsiConsole.Console.Write(table);
        }
        else
        {
            output.WriteLine($"{hits.TotalHits} points within {radiusMetres.ToString("G", System.Globalization.CultureInfo.InvariantCulture)} m");
            foreach (GeoSearchHit row in rows)
                output.WriteLine($"doc={row.DocumentId}\t{string.Join("; ", row.StoredFields.Select(static pair => $"{pair.Key}={string.Join('|', pair.Value)}"))}");
        }
        return CliExitCodes.Success;
    }

    private static int RunFacetInfo(string path, string? fieldName, bool json, bool plain, bool noColor, TextWriter output)
    {
        using var inspector = new IndexInspector(path);
        IReadOnlyList<IndexInspectionField> fields = inspector.GetFields()
            .Where(field => field.HasOrdinalDocValues && (fieldName is null || string.Equals(field.Name, fieldName, StringComparison.Ordinal)))
            .ToArray();
        var report = fields.Select(field => new FacetCandidate(field, inspector.GetTerms(field.Name, take: 1000))).ToArray();
        if (json)
            WriteJson(output, report);
        else if (CanUseRich(output, plain, noColor))
        {
            var table = new Table().Border(TableBorder.Rounded).AddColumn("Facet field").AddColumn("Bucket").AddColumn("Documents");
            foreach (FacetCandidate candidate in report)
                foreach (IndexInspectionTerm term in candidate.Buckets)
                    table.AddRow(Markup.Escape(candidate.Field.Name), Markup.Escape(term.Term), term.DocumentFrequency.ToString("N0"));
            AnsiConsole.Console.Write(table);
        }
        else
        {
            output.WriteLine("Ordinal DocValues identify facet candidates; bucket counts below come from indexed postings.");
            foreach (FacetCandidate candidate in report)
            {
                output.WriteLine($"{candidate.Field.Name} ({candidate.Field.DocumentCount} documents)");
                foreach (IndexInspectionTerm term in candidate.Buckets)
                    output.WriteLine($"  {term.Term}: {term.DocumentFrequency}");
            }
        }
        return CliExitCodes.Success;
    }

    private static int RunGeoInfo(string path, string? fieldName, bool json, bool plain, bool noColor, TextWriter output)
    {
        using var inspector = new IndexInspector(path);
        IReadOnlyList<IndexInspectionField> fields = inspector.GetFields()
            .Where(field => field.SpatialKind is not null && (fieldName is null || string.Equals(field.Name, fieldName, StringComparison.Ordinal)))
            .ToArray();
        if (json)
            WriteJson(output, fields);
        else if (CanUseRich(output, plain, noColor))
            IndexInspectionRenderer.Fields(AnsiConsole.Console, fields);
        else
            IndexInspectionRenderer.WriteFields(output, fields);
        return CliExitCodes.Success;
    }

    private static int RunBrowse(string path, TextWriter output)
    {
        if (!CanPrompt(output))
            return RunIndexInfo(path, json: false, plain: true, noColor: true, output);
        using var inspector = new IndexInspector(path);
        return WorkbenchBrowser.Browse(AnsiConsole.Console, inspector);
    }

    private static bool CanPrompt(TextWriter output)
        => ReferenceEquals(output, Console.Out) && !Console.IsInputRedirected && !Console.IsOutputRedirected
            && AnsiConsole.Profile.Capabilities.Interactive;

    private static bool CanUseRich(TextWriter output, bool plain, bool noColor)
        => !plain && !noColor && ReferenceEquals(output, Console.Out)
            && !Console.IsOutputRedirected && AnsiConsole.Profile.Capabilities.Interactive;

    private static void WriteJson<T>(TextWriter output, T value)
        => output.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

    private static int WriteNotFound(TextWriter output, string message)
    {
        output.WriteLine(message);
        return CliExitCodes.ValidationErrors;
    }

    private static (Command Command, Option<bool> Json, Option<bool> Plain, Option<bool> NoColor) AddRenderOptions(Command command)
    {
        var json = Flag("--json", "Write JSON.");
        var plain = Flag("--plain", "Use plain text output.");
        var noColor = Flag("--no-color", "Disable colour and rich terminal output.");
        command.Add(json);
        command.Add(plain);
        command.Add(noColor);
        return (command, json, plain, noColor);
    }

    private static Option<bool> Flag(string name, string description) => new(name) { Description = description };
    private static T GetRequiredValue<T>(ParseResult result, Argument<T> argument) => result.GetValue(argument)!;

    private static void WriteExplanation(TextWriter output, Explanation value, int depth)
    {
        output.WriteLine($"{new string(' ', depth * 2)}{value.Score:G7}  {value.Description}");
        foreach (Explanation child in value.Details)
            WriteExplanation(output, child, depth + 1);
    }

    private sealed class MaterialisedTokenSink : ISpanTokenSink
    {
        internal List<Token> Tokens { get; } = [];
        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type = Token.DefaultType,
            int positionIncrement = 1, byte[]? payload = null)
            => Tokens.Add(new Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
    }

    private sealed record SearchRow(int DocumentId, float Score, IReadOnlyDictionary<string, IReadOnlyList<string>> StoredFields);
    private sealed record SearchReport(string Query, string Field, int TotalHits, SearchRow[] Results, double ElapsedMilliseconds, bool IsPartial);
    private sealed record AnalysisToken(int Ordinal, string Text, int StartOffset, int EndOffset, string Type, int PositionIncrement, int PositionLength);
    private sealed record FacetCandidate(IndexInspectionField Field, IReadOnlyList<IndexInspectionTerm> Buckets);
    private sealed record GeoSearchHit(int DocumentId, float Score, IReadOnlyDictionary<string, IReadOnlyList<string>> StoredFields);
    private sealed record GeoSearchReport(string Field, double Latitude, double Longitude, double RadiusMetres,
        int TotalHits, GeoSearchHit[] Results);
}
