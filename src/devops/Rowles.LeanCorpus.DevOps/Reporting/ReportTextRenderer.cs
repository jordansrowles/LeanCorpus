using System.Globalization;
using System.Text.Json.Nodes;

namespace Rowles.LeanCorpus.DevOps.Reporting;

internal static class ReportTextRenderer
{
    public static void Render(JsonObject document, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        var report = document["report"] as JsonObject;
        if (report is null)
        {
            output.WriteLine(document.ToJsonString());
            return;
        }

        output.WriteLine("LeanCorpus repository report");
        output.WriteLine($"Run: {Text(document["runId"])}; outcome: {Text(document["outcome"])}");
        foreach (var warning in Array(document["warnings"]))
        {
            output.WriteLine($"Warning: {Text(warning)}");
        }
        if (report["Git"] is JsonObject git) RenderGit(git, output);
        if (report["Files"] is JsonObject files) RenderFiles(files, output);
        if (report["Code"] is JsonObject code) RenderCode(code, output);
    }

    private static void RenderGit(JsonObject git, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("Git statistics");
        if (git["Overview"] is JsonObject overview)
        {
            output.WriteLine($"  commits: {Text(overview["Commits"])}; branch: {Text(overview["Branch"])}; local branches: {Text(overview["BranchCount"])}; tags: {Text(overview["TagCount"])}");
            output.WriteLine($"  history: {Text(overview["FirstCommit"])} to {Text(overview["LastCommit"])}");
        }
        output.WriteLine("  contributors:");
        foreach (var contributor in Array(git["Contributors"]))
        {
            output.WriteLine($"    {Text(contributor?["Commits"]),-6} {Text(contributor?["Name"])} <{Text(contributor?["Email"])}>");
        }
        output.WriteLine("  monthly activity:");
        var activity = Array(git["Activity"]);
        var maximum = activity.Select(row => Number(row?["Commits"])).DefaultIfEmpty(0).Max();
        foreach (var row in activity)
        {
            var count = Number(row?["Commits"]);
            var barLength = maximum == 0 ? 0 : (int)Math.Ceiling(40.0 * count / maximum);
            output.WriteLine($"    {Text(row?["Month"])}  {new string('#', barLength),-40} {count.ToString(CultureInfo.InvariantCulture)}");
        }
        if (git["Hygiene"] is JsonObject hygiene)
        {
            output.WriteLine($"  conventional commits: {Text(hygiene["Conventional"])} / {Text(hygiene["TotalSubjects"])} ({Text(hygiene["ConventionalPct"])}%); co-author trailers: {Text(hygiene["CoAuthorCommits"])}");
        }
        if (git["Churn"] is JsonObject churn)
        {
            output.WriteLine($"  source churn: +{Text(churn["TotalAdded"])} / -{Text(churn["TotalDeleted"])} lines");
            RenderPathRows("top additions", churn["TopAdded"], "Added", output);
            RenderPathRows("top deletions", churn["TopDeleted"], "Deleted", output);
        }
        RenderPathRows("recent activity", git["Recent"], "Recent", output);
    }

    private static void RenderFiles(JsonObject files, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("Tracked files");
        if (files["History"] is JsonObject history)
        {
            RenderPathRows("most touched", history["MostTouched"], "Touches", output);
            RenderPathRows("least touched", history["LeastTouched"], "Touches", output);
            RenderPathRows("newest", history["Newest"], "Last", output);
            RenderPathRows("oldest", history["Oldest"], "First", output);
            RenderPathRows("stale", history["Stale"], "Last", output);
            output.WriteLine($"  created once: {Array(history["CreatedOnce"]).Count}");
        }
        RenderPathRows("co-change pairs", files["CoChange"], "Together", output, includeRight: true);
        output.WriteLine("  file count by type:");
        foreach (var row in Array(files["ByType"]))
        {
            output.WriteLine($"    {Text(row?["Extension"]),-14} {Text(row?["Count"])}");
        }
        if (files["Large"] is JsonObject large)
        {
            output.WriteLine($"  large tracked files: {Text(large["Count"])} over {Text(large["ThresholdBytes"])} bytes");
            RenderPathRows("large files", large["Files"], "Size", output);
        }
        if (files["Illegal"] is JsonObject illegal)
        {
            output.WriteLine($"  cross-platform filename violations: {Text(illegal["ViolationCount"])}");
            foreach (var key in new[] { "ReservedChars", "DeviceNames", "TrailingDotSpace", "ControlChars" })
            {
                RenderStringRows(key, illegal[key], output);
            }
            foreach (var collision in Array(illegal["Collisions"]))
            {
                output.WriteLine($"    case collision: {string.Join(", ", Array(collision?["Paths"]).Select(Text))}");
            }
        }
    }

    private static void RenderCode(JsonObject code, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("Code health");
        if (code["GodClasses"] is JsonObject gods)
        {
            output.WriteLine($"  god-class thresholds: warn > {Text(gods["WarnThreshold"])} LOC; severe > {Text(gods["SevereThreshold"])} LOC");
            RenderPathRows("largest type groups", gods["Top"], "Lines", output);
        }
        if (code["Dependencies"] is JsonObject dependencies)
        {
            output.WriteLine($"  project graph: {Text(dependencies["ProjectCount"])} projects; {Text(dependencies["TotalPackageRefs"])} package references");
            RenderStringRows("dependency cycles", dependencies["Cycles"], output);
        }
        if (code["Untested"] is JsonObject untested)
        {
            output.WriteLine($"  untested-source heuristic: {Text(untested["UntestedCount"])} / {Text(untested["ProductionCount"])} production files; {Text(untested["TestFileCount"])} test file names");
            RenderStringRows("untested candidates", untested["Untested"], output);
        }
        if (code["Syntax"] is JsonObject syntax)
        {
            output.WriteLine($"  syntax: {Text(syntax["FileCount"])} files; {Text(syntax["TypeCount"])} type declarations; {Text(syntax["PartialTypeCount"])} partial types; {Text(syntax["MethodCount"])} methods; {Text(syntax["DynamicTypeCount"])} dynamic types");
        }
        if (code["AotHygiene"] is JsonObject hygiene)
        {
            foreach (var key in new[] { "AotFiles", "BomFiles", "CrlfFiles", "TrailingWhitespaceFiles", "TabIndentFiles" })
            {
                output.WriteLine($"  {key}: {Array(hygiene[key]).Count}");
                foreach (var item in Array(hygiene[key]))
                {
                    var path = item is JsonObject obj ? Text(obj["Path"]) : Text(item);
                    var matches = item is JsonObject matchObject && matchObject["AotMatches"] is JsonArray aotMatches
                        ? $" ({string.Join(", ", aotMatches.Select(Text))})"
                        : string.Empty;
                    output.WriteLine($"    {path}{matches}");
                }
            }
        }
    }

    private static void RenderPathRows(string heading, JsonNode? node, string valueName, TextWriter output, bool includeRight = false)
    {
        var rows = Array(node);
        output.WriteLine($"  {heading}:");
        foreach (var row in rows)
        {
            var value = Text(row?[valueName]);
            var path = Text(row?["Path"]);
            if (path == "(none)")
            {
                path = Text(row?["Left"]);
            }
            if (includeRight)
            {
                path += $" <=> {Text(row?["Right"])}";
            }
            output.WriteLine($"    {value,-8} {path}");
        }
        if (rows.Count == 0)
        {
            output.WriteLine("    none");
        }
    }

    private static void RenderStringRows(string heading, JsonNode? node, TextWriter output)
    {
        var rows = Array(node);
        if (rows.Count == 0)
        {
            return;
        }
        output.WriteLine($"  {heading}:");
        foreach (var row in rows)
        {
            output.WriteLine($"    {Text(row)}");
        }
    }

    private static IReadOnlyList<JsonNode?> Array(JsonNode? value)
        => value is JsonArray array ? array.ToArray() : [];

    private static int Number(JsonNode? value)
        => value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var result) ? result : 0;

    private static string Text(JsonNode? value)
    {
        if (value is null)
        {
            return "(none)";
        }
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
        {
            return text;
        }
        return value.ToJsonString();
    }
}
