using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.DevOps.Configuration;

namespace Rowles.LeanCorpus.DevOps.Documentation;

internal sealed record BenchmarkDocumentationResult(int PageCount, IReadOnlyList<string> Warnings);

internal sealed class BenchmarkDocumentationGenerator
{
    private static readonly Regex ChartIdPattern = new("[^a-zA-Z0-9]", RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexMatchTimeoutPolicy.MatchTimeout);
    public BenchmarkDocumentationResult Generate(
        string benchmarkRunsDirectory,
        string outputDirectory,
        string chartJsPath,
        string benchmarkChartScriptPath,
        DevOpsConfiguration configuration,
        bool skipRunScan = false,
        string? canonicalQualificationAuditPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchmarkRunsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(chartJsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(benchmarkChartScriptPath);
        ArgumentNullException.ThrowIfNull(configuration);
        var runsRoot = Path.GetFullPath(benchmarkRunsDirectory);
        var outputRoot = Path.GetFullPath(outputDirectory);
        var chartPath = Path.GetFullPath(chartJsPath);
        if (!File.Exists(chartPath))
        {
            throw new FileNotFoundException($"Pinned Chart.js browser bundle not found at '{chartPath}'. Restore the site dependencies with npm ci --prefix docs/site-config/diagrams.", chartPath);
        }

        ResetOutputDirectory(outputRoot);
        var warnings = new List<string>();
        var newestPerSuite = new Dictionary<string, CoreSuiteEvidence>(StringComparer.Ordinal);
        var newestProjectEvidence = new Dictionary<string, ProjectEvidence>(StringComparer.Ordinal);
        var runDirectories = skipRunScan || !Directory.Exists(runsRoot)
            ? []
            : Directory.EnumerateDirectories(runsRoot).OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        if (runDirectories.Length == 0 && !skipRunScan)
        {
            warnings.Add($"No benchmark runs found in {runsRoot}");
        }

        foreach (var runDirectory in runDirectories)
        {
            var runReportPath = Path.Combine(runDirectory, "run-report.json");
            if (File.Exists(runReportPath))
            {
                var runManifestPath = Path.Combine(runDirectory, "run.json");
                if (!File.Exists(runManifestPath)) continue;
                using var runManifest = ReadJson(runManifestPath, warnings);
                using var runReport = ReadJson(runReportPath, warnings);
                if (runManifest is null || runReport is null || !TryGetCompletedAt(runManifest.RootElement, out var completedAt)) continue;
                if (!IsSuccessfulRun(runManifest.RootElement)) continue;

                foreach (var project in GetArray(runReport.RootElement, "projects"))
                {
                    var projectKey = GetString(project, "project");
                    if (projectKey is null || !configuration.BenchmarkProjects.ContainsKey(projectKey) ||
                        !string.Equals(GetString(project, "status"), "Passed", StringComparison.Ordinal)) continue;
                    var relativePath = GetString(project, "path") ?? projectKey;
                    var projectDirectory = ResolveInside(runDirectory, relativePath);
                    if (!Directory.Exists(projectDirectory)) continue;
                    if (projectKey == "core")
                    {
                        AddCoreRun(projectDirectory, completedAt, runManifest.RootElement, runReport.RootElement, newestPerSuite, warnings);
                    }
                    else
                    {
                        AddProjectRun(projectKey, projectDirectory, completedAt, runReport.RootElement, newestProjectEvidence);
                    }
                }
            }
            else
            {
                AddLegacyCoreRuns(runDirectory, newestPerSuite, warnings);
            }
        }

        if (newestPerSuite.Count == 0 && !skipRunScan)
        {
            warnings.Add("No valid Core benchmark suites found in retained runs.");
        }

        var machines = newestPerSuite.Values.Select(static evidence => evidence.Machine)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var pageCount = WriteCorePages(newestPerSuite, machines.Length > 1, outputRoot, warnings, configuration);
        foreach (var project in configuration.BenchmarkProjects.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (project.Key == "core") continue;
            pageCount += WriteOtherProjectPages(project.Key, $"{project.Value.DisplayName} benchmarks", newestProjectEvidence, outputRoot);
        }

        if (canonicalQualificationAuditPath is not null)
        {
            var auditSource = Path.GetFullPath(canonicalQualificationAuditPath);
            if (!File.Exists(auditSource))
                throw new FileNotFoundException("Canonical benchmark qualification audit source was not found.", auditSource);
            File.Copy(auditSource, Path.Combine(outputRoot, "qualification-audit.md"), overwrite: true);
        }

        var chartsPath = Path.GetFullPath(benchmarkChartScriptPath);
        if (!File.Exists(chartsPath))
        {
            throw new FileNotFoundException("The repository benchmark chart script is missing.", chartsPath);
        }
        WriteText(Path.Combine(outputRoot, "benchmark-charts.js"), File.ReadAllText(chartsPath) + Environment.NewLine);
        File.Copy(chartPath, Path.Combine(outputRoot, "chart.umd.js"), overwrite: true);
        var allBenchmarkRunsQualified =
            (newestPerSuite.Count > 0 || newestProjectEvidence.Count > 0) &&
            newestPerSuite.Values.All(static evidence => evidence.QualificationState == "Qualified") &&
            newestProjectEvidence.Values.All(static evidence => evidence.QualificationState == "Qualified");
        WriteBenchmarkCatalog(outputRoot, allBenchmarkRunsQualified);
        return new BenchmarkDocumentationResult(pageCount, warnings);
    }

    private static void AddCoreRun(string projectDirectory, DateTimeOffset completedAt, JsonElement runManifest, JsonElement runReport,
        IDictionary<string, CoreSuiteEvidence> newestPerSuite, ICollection<string> warnings)
    {
        var reportPath = Path.Combine(projectDirectory, "report.json");
        if (!File.Exists(reportPath)) return;
        using var report = ReadJson(reportPath, warnings);
        if (report is null || !IsCoreReport(report.RootElement) || GetInt64(report.RootElement, "totalBenchmarkCount") <= 0) return;
        var qualificationState = GetQualificationState(report.RootElement, runReport);
        var machineName = GetNestedString(report.RootElement, "provenance", "machineName") ??
            GetString(report.RootElement, "hostMachineName") ?? GetString(runManifest, "machine") ?? "unknown";
        foreach (var suite in GetArray(report.RootElement, "suites"))
        {
            var name = GetString(suite, "suiteName");
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (GetInt64(suite, "failedBenchmarkCount") > 0 || GetInt64(suite, "missingBenchmarkCount") > 0) continue;
            if (!newestPerSuite.TryGetValue(name, out var current) || completedAt > current.CompletedAtUtc)
            {
                newestPerSuite[name] = new CoreSuiteEvidence(projectDirectory, report.RootElement.Clone(), completedAt, machineName, qualificationState);
            }
        }
    }

    private static void AddProjectRun(string projectKey, string projectDirectory, DateTimeOffset completedAt, JsonElement runReport,
        IDictionary<string, ProjectEvidence> newestProjectEvidence)
    {
        var markdown = EnumerateReportMarkdown(projectDirectory)
            .Any(path => !string.IsNullOrWhiteSpace(GetTableContent(path)));
        if (!markdown) return;
        if (!newestProjectEvidence.TryGetValue(projectKey, out var current) || completedAt > current.CompletedAtUtc)
        {
            newestProjectEvidence[projectKey] = new ProjectEvidence(
                projectDirectory, completedAt, GetManagedQualificationState(runReport));
        }
    }

    private static void AddLegacyCoreRuns(string runDirectory, IDictionary<string, CoreSuiteEvidence> newestPerSuite,
        ICollection<string> warnings)
    {
        foreach (var reportPath in Directory.EnumerateFiles(runDirectory, "report.json", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal))
        {
            using var report = ReadJson(reportPath, warnings);
            if (report is null || !IsCoreReport(report.RootElement) || GetInt64(report.RootElement, "totalBenchmarkCount") <= 0 ||
                !TryGetDateTimeOffset(report.RootElement, "generatedAtUtc", out var generatedAt)) continue;
            var machineName = GetNestedString(report.RootElement, "provenance", "machineName") ?? "unknown";
            foreach (var suite in GetArray(report.RootElement, "suites"))
            {
                var name = GetString(suite, "suiteName");
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!newestPerSuite.TryGetValue(name, out var current) || generatedAt > current.CompletedAtUtc)
                {
                    newestPerSuite[name] = new CoreSuiteEvidence(
                        Path.GetDirectoryName(reportPath)!, report.RootElement.Clone(), generatedAt, machineName, "Historical / unqualified");
                }
            }
        }
    }

    private static int WriteCorePages(IReadOnlyDictionary<string, CoreSuiteEvidence> newestPerSuite, bool multipleMachines,
        string outputRoot, ICollection<string> warnings, DevOpsConfiguration configuration)
    {
        var count = 0;
        var suites = newestPerSuite.OrderBy(pair => GetSuiteDisplayName(configuration, pair.Key), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal);
        foreach (var (suiteName, evidence) in suites)
        {
            var fileName = multipleMachines ? $"{evidence.Machine}-{suiteName}.md" : $"{suiteName}.md";
            var displayName = GetSuiteDisplayName(configuration, suiteName);
            var suiteDirectory = ResolveInside(evidence.ProjectDirectory, suiteName);
            var markdown = EnumerateReportMarkdown(suiteDirectory).FirstOrDefault();
            if (markdown is null)
            {
                warnings.Add($"No markdown for '{suiteName}', skipping.");
                continue;
            }
            var table = GetTableContent(markdown);
            if (string.IsNullOrWhiteSpace(table))
            {
                warnings.Add($"No table in {Path.GetFileName(markdown)}, skipping.");
                continue;
            }

            var report = evidence.Report;
            var generatedAt = GetDateTimeOffset(report, "generatedAtUtc").UtcDateTime;
            var commitHash = GetString(report, "commitHash") ?? string.Empty;
            var commitShort = commitHash.Length > 7 ? commitHash[..7] : commitHash;
            var docCount = GetNestedInt64(report, "provenance", "effectiveDocCount");
            var dotnetVersion = GetString(report, "dotnetVersion") ?? string.Empty;
            var runDate = generatedAt.ToString("d MMMM yyyy HH:mm 'UTC'", CultureInfo.CurrentCulture);
            var jsonBaseName = Path.GetFileNameWithoutExtension(fileName);
            var jsonOutName = jsonBaseName + ".json";
            var encodedJsonOutName = System.Net.WebUtility.HtmlEncode(jsonOutName);
            var jsonFiles = Directory.Exists(suiteDirectory)
                ? Directory.EnumerateFiles(suiteDirectory, "*-report-full.json", SearchOption.AllDirectories).OrderBy(static path => path, StringComparer.Ordinal).ToArray()
                : [];
            var hasCharts = jsonFiles.Length > 0;
            var jsonOutPath = Path.Combine(outputRoot, jsonOutName);
            if (hasCharts)
            {
                File.Copy(jsonFiles[0], jsonOutPath, overwrite: true);
            }
            else if (File.Exists(jsonOutPath))
            {
                File.Delete(jsonOutPath);
            }

            var chartId = ChartIdPattern.Replace(suiteName, "-");
            var builder = new StringBuilder();
            builder.AppendLine("---");
            builder.AppendLine($"title: Benchmarks - {displayName}");
            builder.AppendLine("---");
            builder.AppendLine();
            builder.AppendLine($"# {displayName}");
            builder.AppendLine();
            builder.AppendLine($"**.NET** {dotnetVersion} &nbsp;&middot;&nbsp; **Commit** ``{commitShort}`` &nbsp;&middot;&nbsp; {runDate} &nbsp;&middot;&nbsp; {docCount.ToString("N0", CultureInfo.CurrentCulture)} docs");
            builder.AppendLine();
            builder.AppendLine($"**Qualification:** {evidence.QualificationState}");
            if (evidence.QualificationState != "Qualified")
            {
                builder.AppendLine();
                builder.AppendLine("This retained execution output is historical or unqualified and is not a 4.0 performance comparison.");
            }
            builder.AppendLine();
            builder.AppendLine(table);
            builder.AppendLine();
            if (hasCharts)
            {
                builder.AppendLine("<div class=\"benchmark-chart\">");
                builder.AppendLine($"<p style=\"margin-bottom:4px\"><label>Time scale: <select id=\"chart-scale-{chartId}\"><option value=\"log2\" selected>Log2</option><option value=\"log10\">Log10</option><option value=\"linear\">Linear</option></select></label> <label>Width: <input type=\"range\" id=\"chart-width-{chartId}\" min=\"400\" max=\"1400\" value=\"960\" step=\"20\" style=\"vertical-align:middle\"></label> <label>Height: <input type=\"range\" id=\"chart-height-{chartId}\" min=\"200\" max=\"900\" value=\"500\" step=\"20\" style=\"vertical-align:middle\"></label></p>");
                builder.AppendLine($"<div id=\"chart-wrap-{chartId}\" style=\"max-width:960px\"><canvas id=\"chart-bench-{chartId}\" data-json=\"{encodedJsonOutName}\" style=\"height:500px\"></canvas></div>");
                builder.AppendLine($"<p><a href=\"{jsonOutName}\">Full results as JSON</a></p>");
                builder.AppendLine("</div>");
                builder.AppendLine("<script src=\"benchmark-charts.js\"></script>");
            }
            builder.AppendLine();
            WriteText(Path.Combine(outputRoot, fileName), builder.ToString());
            count++;
        }
        return count;
    }

    private static string GetSuiteDisplayName(DevOpsConfiguration configuration, string suiteId)
    {
        if (configuration.BenchmarkSuites.TryGetValue(suiteId, out var suite))
            return suite.DisplayName;
        if (configuration.HistoricalBenchmarkSuiteLabels.TryGetValue(suiteId, out var historicalLabel))
            return historicalLabel;
        return suiteId;
    }

    private static int WriteOtherProjectPages(string projectKey, string title,
        IReadOnlyDictionary<string, ProjectEvidence> newestProjectEvidence, string outputRoot)
    {
        if (!newestProjectEvidence.TryGetValue(projectKey, out var evidence)) return 0;
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: {title}");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine($"# {title}");
        builder.AppendLine();
        builder.AppendLine($"**Qualification:** {evidence.QualificationState}");
        if (evidence.QualificationState != "Qualified")
        {
            builder.AppendLine();
            builder.AppendLine("This retained execution output is unqualified and is not a 4.0 performance comparison.");
        }
        builder.AppendLine();
        foreach (var markdown in EnumerateReportMarkdown(evidence.ProjectDirectory))
        {
            var table = GetTableContent(markdown);
            if (string.IsNullOrWhiteSpace(table)) continue;
            var name = Regex.Replace(Path.GetFileNameWithoutExtension(markdown), "-report-github$", string.Empty, RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeout);
            builder.AppendLine($"## {name}");
            builder.AppendLine();
            builder.AppendLine(table);
            builder.AppendLine();
        }
        WriteText(Path.Combine(outputRoot, projectKey + ".md"), builder.ToString());
        return 1;
    }

    private static void WriteBenchmarkCatalog(string outputRoot, bool allBenchmarkRunsQualified)
    {
        var pages = Directory.EnumerateFiles(outputRoot, "*.md", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path) is not ("overview.md" or "toc.yml"))
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        var reportPages = pages
            .Where(static path => !string.Equals(Path.GetFileName(path), "qualification-audit.md", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var qualificationAudit = pages.FirstOrDefault(static path =>
            string.Equals(Path.GetFileName(path), "qualification-audit.md", StringComparison.OrdinalIgnoreCase));
        var markdown = new List<string> { "# Benchmarks", string.Empty };
        if (reportPages.Length == 0)
        {
            markdown.Add("No qualified benchmark results are published for this build. See [Performance](../performance.md) for comparison limits and the [Benchmarking guide](../tips/03-benchmarking.md) for methodology and reproducibility.");
            if (qualificationAudit is not null)
            {
                markdown.Add(string.Empty);
                markdown.Add($"The [4.0 benchmark qualification audit]({Uri.EscapeDataString(Path.GetFileName(qualificationAudit))}) records discovered cases and current exclusions.");
            }
        }
        else
        {
            markdown.Add(allBenchmarkRunsQualified
                ? "Qualified benchmark reports from retained runs."
                : "Retained execution reports include historical or unqualified evidence; they do not establish 4.0 performance comparisons.");
            markdown.Add(string.Empty);
            foreach (var page in reportPages)
            {
                var title = ReadPageTitle(page) ?? Path.GetFileNameWithoutExtension(page);
                markdown.Add($"- [{title}]({Uri.EscapeDataString(Path.GetFileName(page))})");
            }
        }

        var toc = new List<string> { "- name: Benchmarks", "  href: overview.md" };
        if (pages.Length > 0)
        {
            toc.Add("  items:");
            foreach (var page in pages)
            {
                var title = ReadPageTitle(page) ?? Path.GetFileNameWithoutExtension(page);
                toc.Add($"    - name: {ToYaml(title)}");
                toc.Add($"      href: {ToYaml(Path.GetFileName(page))}");
            }
        }
        WriteLines(Path.Combine(outputRoot, "overview.md"), markdown);
        WriteLines(Path.Combine(outputRoot, "toc.yml"), toc);
    }

    private static JsonDocument? ReadJson(string path, ICollection<string> warnings)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllBytes(path));
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            warnings.Add($"Failed to parse {path}, skipping: {exception.Message}");
            return null;
        }
    }

    private static bool IsSuccessfulRun(JsonElement manifest)
    {
        var status = GetString(manifest, "status");
        if (string.Equals(status, "Passed", StringComparison.Ordinal)) return true;
        var state = GetString(manifest, "state");
        return string.Equals(state, "succeeded", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetCompletedAt(JsonElement manifest, out DateTimeOffset completedAt)
        => TryGetDateTimeOffset(manifest, "completedAtUtc", out completedAt) ||
           TryGetDateTimeOffset(manifest, "completedUtc", out completedAt);

    private static bool TryGetDateTimeOffset(JsonElement value, string property, out DateTimeOffset parsed)
    {
        parsed = default;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var element) &&
            element.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out parsed);
    }

    private static DateTimeOffset GetDateTimeOffset(JsonElement value, string property)
        => TryGetDateTimeOffset(value, property, out var parsed)
            ? parsed
            : throw new InvalidDataException($"Benchmark report has no valid {property} timestamp.");

    private static bool IsCoreReport(JsonElement report)
        => report.ValueKind == JsonValueKind.Object && report.TryGetProperty("totalBenchmarkCount", out _) &&
           report.TryGetProperty("suites", out _) && report.TryGetProperty("generatedAtUtc", out _);

    private static string GetQualificationState(JsonElement coreReport, JsonElement runReport)
    {
        if (GetInt64(coreReport, "schemaVersion") != 5)
            return "Historical / unqualified";
        if (TryGetDateTimeOffset(coreReport, "generatedAtUtc", out var generatedAt) &&
            generatedAt is { Year: 2026, Month: 8 })
            return "Historical / unqualified";
        return GetManagedQualificationState(runReport);
    }

    private static string GetManagedQualificationState(JsonElement runReport)
        => GetNestedString(runReport, "qualification", "identityStatus") == "Validated" &&
           GetNestedString(runReport, "qualification", "state") == "Qualified" &&
           GetNestedBoolean(runReport, "qualification", "eligibleForRatio") &&
           GetBoolean(runReport, "comparable")
            ? "Qualified"
            : "Unqualified";

    private static long GetInt64(JsonElement value, string property)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var element) && element.TryGetInt64(out var number) ? number : 0;

    private static long GetNestedInt64(JsonElement value, string parent, string property)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(parent, out var node) ? GetInt64(node, property) : 0;

    private static string? GetString(JsonElement value, string property)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static string? GetNestedString(JsonElement value, string parent, string property)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(parent, out var node) ? GetString(node, property) : null;

    private static bool GetBoolean(JsonElement value, string property)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var element) &&
           (element.ValueKind is JsonValueKind.True or JsonValueKind.False) && element.GetBoolean();

    private static bool GetNestedBoolean(JsonElement value, string parent, string property)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(parent, out var node) && GetBoolean(node, property);

    private static IEnumerable<JsonElement> GetArray(JsonElement value, string property)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().ToArray()
            : [];

    private static IEnumerable<string> EnumerateReportMarkdown(string directory)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*-report-github.md", SearchOption.AllDirectories).OrderBy(static path => path, StringComparer.Ordinal)
            : [];

    private static string? GetTableContent(string path)
    {
        if (!File.Exists(path)) return null;
        var lines = File.ReadAllLines(path);
        var start = Array.FindIndex(lines, static line => line.StartsWith('|'));
        if (start < 0) return null;
        var table = string.Join('\n', lines[start..]).TrimEnd();
        return table.Length == 0 ? null : table;
    }

    private static string? ReadPageTitle(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var match = Regex.Match(line, "^#\\s+(.+?)\\s*$", RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeout);
            if (match.Success) return match.Groups[1].Value.Trim();
        }
        return null;
    }

    private static string ToYaml(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string ResolveInside(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Split(['/', '\\']).Any(static segment => segment == ".."))
        {
            throw new InvalidDataException($"Retained benchmark report path escapes its run directory: {relativePath}");
        }
        var fullRoot = Path.GetFullPath(root);
        var resolved = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!resolved.StartsWith(Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidDataException($"Retained benchmark report path escapes its run directory: {relativePath}");
        }
        return resolved;
    }

    private static void ResetOutputDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Benchmark docs generator refuses to reset a linked directory: {path}");
            }
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"Benchmark docs generator refuses to remove linked staged path: {entry}");
                }
                if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
        }
        else
        {
            Directory.CreateDirectory(path);
        }
    }

    private static void WriteText(string path, string content)
        => File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static void WriteLines(string path, IEnumerable<string> lines)
        => File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private sealed record CoreSuiteEvidence(string ProjectDirectory, JsonElement Report, DateTimeOffset CompletedAtUtc, string Machine, string QualificationState);

    private sealed record ProjectEvidence(string ProjectDirectory, DateTimeOffset CompletedAtUtc, string QualificationState);
}
