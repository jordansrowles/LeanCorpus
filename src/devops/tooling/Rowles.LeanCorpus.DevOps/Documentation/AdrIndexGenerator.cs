using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rowles.LeanCorpus.DevOps.Documentation;

internal sealed class AdrIndexGenerator
{
    private static readonly string[] RequiredKeys = ["adr", "title", "date", "status", "version-added", "summary", "areas"];
    private static readonly string[] RelationshipKeys = ["supersedes", "depends-on", "extends", "relates-to", "superseded-by"];
    private static readonly string[] ValidStatuses = ["Accepted", "Deprecated", "Superseded", "Withdrawn"];
    private static readonly Regex AdrIdPattern = new("^(ADR\\d{3}|ADR-S\\d{3})$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex FrontMatterLinePattern = new("^([A-Za-z][A-Za-z0-9-]*):(?:\\s?(.*))$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ChangeRowPattern = new("^\\|\\s*(\\d{4}-\\d{2}-\\d{2})\\s*\\|\\s*(Accepted|Clarified|Amended|Deprecated|Superseded|Withdrawn)\\s*\\|\\s*(.*?)\\s*\\|\\s*(.*?)\\s*\\|$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ContextHeadingPattern = new("(?m)^## Context\\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RelationshipsBlockPattern = new("(?ms)^## Relationships\\r?\\n.*?(?=^## |\\z)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public int Generate(string adrDirectory, string outputPath, string? stagingAdrDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adrDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var sourceRoot = Path.GetFullPath(adrDirectory);
        var destination = Path.GetFullPath(outputPath);
        var linkRoot = string.IsNullOrWhiteSpace(stagingAdrDirectory) ? sourceRoot : Path.GetFullPath(stagingAdrDirectory);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"ADR directory not found: {sourceRoot}");
        }

        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "ADR*.md", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        if (sourceFiles.Length == 0)
        {
            throw new InvalidDataException($"No ADR files found in {sourceRoot}");
        }

        var adrs = sourceFiles.Select(path => ReadAdr(sourceRoot, path)).ToArray();
        var ordered = adrs.OrderBy(static adr => adr.NamespaceOrder)
            .ThenBy(static adr => adr.Number).ToArray();
        var byId = new Dictionary<string, Adr>(StringComparer.Ordinal);
        var incomingSupersedes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var adr in ordered)
        {
            if (!byId.TryAdd(adr.Id, adr))
            {
                throw new InvalidDataException($"Found duplicate ADR identifier '{adr.Id}'.");
            }
            incomingSupersedes.Add(adr.Id, []);
        }

        foreach (var adr in ordered)
        {
            var declaredTargets = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in new[] { "supersedes", "depends-on", "extends", "relates-to" })
            {
                var withinRelation = new HashSet<string>(StringComparer.Ordinal);
                foreach (var target in adr.Relationships[key])
                {
                    if (!withinRelation.Add(target))
                    {
                        throw new InvalidDataException($"ADR '{adr.Id}' repeats '{target}' under '{key}'.");
                    }
                    if (!declaredTargets.TryAdd(target, key))
                    {
                        throw new InvalidDataException($"ADR '{adr.Id}' declares '{target}' under more than one relationship type.");
                    }
                    if (string.Equals(target, adr.Id, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"ADR '{adr.Id}' cannot refer to itself under '{key}'.");
                    }
                    if (!byId.TryGetValue(target, out var targetAdr))
                    {
                        throw new InvalidDataException($"ADR '{adr.Id}' references missing ADR '{target}'.");
                    }
                    if (key == "supersedes")
                    {
                        if (adr.Date <= targetAdr.Date)
                        {
                            throw new InvalidDataException($"ADR '{adr.Id}' must be later than the ADR it supersedes ('{target}').");
                        }
                        incomingSupersedes[target].Add(adr.Id);
                    }
                    if ((key is "depends-on" or "extends") && (targetAdr.Status is "Superseded" or "Withdrawn"))
                    {
                        throw new InvalidDataException($"ADR '{adr.Id}' has an unresolved '{key}' relationship to retired ADR '{target}'.");
                    }
                }
            }
        }

        foreach (var adr in ordered)
        {
            var incoming = incomingSupersedes[adr.Id].Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var explicitSupersededBy = adr.Relationships["superseded-by"].Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (explicitSupersededBy.Length > 0 && !explicitSupersededBy.SequenceEqual(incoming, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"ADR '{adr.Id}' superseded-by metadata disagrees with incoming supersedes relationships.");
            }
            if (adr.Status == "Superseded" && incoming.Length == 0)
            {
                throw new InvalidDataException($"Superseded ADR '{adr.Id}' has no later superseding decision.");
            }
            if (adr.Status == "Accepted" && incoming.Length > 0)
            {
                throw new InvalidDataException($"Accepted ADR '{adr.Id}' is the target of a supersession and must be reviewed.");
            }
        }

        ValidateSupersessionCycles(ordered, byId);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        WriteIndex(destination, Path.GetDirectoryName(destination)!, linkRoot, ordered, incomingSupersedes);
        WriteToc(Path.Combine(Path.GetDirectoryName(destination)!, "toc.yml"), ordered);
        if (!string.IsNullOrWhiteSpace(stagingAdrDirectory))
        {
            WriteRelationshipSections(Path.GetFullPath(stagingAdrDirectory), ordered, incomingSupersedes);
        }
        return ordered.Length;
    }

    private static Adr ReadAdr(string root, string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2 || !string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"ADR '{path}' does not start with YAML front matter.");
        }

        var closing = Array.FindIndex(lines, 1, static line => string.Equals(line.Trim(), "---", StringComparison.Ordinal));
        if (closing < 0)
        {
            throw new InvalidDataException($"ADR '{path}' has no closing YAML front-matter delimiter.");
        }

        var frontMatter = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < closing; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var match = FrontMatterLinePattern.Match(line);
            if (!match.Success)
            {
                throw new InvalidDataException($"Unsupported YAML front-matter line in '{path}': {line}");
            }
            if (!frontMatter.TryAdd(match.Groups[1].Value, match.Groups[2].Value.Trim()))
            {
                throw new InvalidDataException($"ADR '{path}' declares '{match.Groups[1].Value}' more than once.");
            }
        }
        foreach (var key in RequiredKeys)
        {
            if (!frontMatter.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"ADR '{path}' is missing '{key}' front matter.");
            }
        }

        var id = ParseYamlScalar(frontMatter["adr"], path, "adr");
        if (!AdrIdPattern.IsMatch(id))
        {
            throw new InvalidDataException($"ADR '{path}' has an invalid ADR identifier '{id}'.");
        }
        var title = ParseYamlScalar(frontMatter["title"], path, "title");
        var summary = ParseYamlScalar(frontMatter["summary"], path, "summary");
        var version = ParseYamlScalar(frontMatter["version-added"], path, "version-added");
        var status = ParseYamlScalar(frontMatter["status"], path, "status");
        if (!ValidStatuses.Contains(status, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"ADR '{path}' has an invalid status '{status}'.");
        }

        var areas = ParseYamlList(frontMatter["areas"], path, "areas");
        if (areas.Length == 0)
        {
            throw new InvalidDataException($"ADR '{path}' must declare at least one area.");
        }
        var relationships = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var key in RelationshipKeys)
        {
            relationships[key] = frontMatter.TryGetValue(key, out var value)
                ? ParseYamlList(value, path, key)
                : [];
        }

        var dateText = ParseYamlScalar(frontMatter["date"], path, "date");
        var date = ParseDate(dateText, path, "date");
        var lastUpdated = frontMatter.TryGetValue("last-updated", out var lastUpdatedValue)
            ? ParseYamlScalar(lastUpdatedValue, path, "last-updated")
            : string.Empty;
        var lastUpdatedDate = string.IsNullOrEmpty(lastUpdated) ? date : ParseDate(lastUpdated, path, "last-updated");
        if (lastUpdatedDate < date)
        {
            throw new InvalidDataException($"ADR '{path}' has last-updated earlier than its immutable date.");
        }

        var history = new List<AdrChange>();
        var inChanges = false;
        for (var index = closing + 1; index < lines.Length; index++)
        {
            if (string.Equals(lines[index], "## Changes", StringComparison.Ordinal))
            {
                inChanges = true;
                continue;
            }
            if (inChanges && lines[index].StartsWith("## ", StringComparison.Ordinal)) break;
            if (!inChanges) continue;
            var changeMatch = ChangeRowPattern.Match(lines[index]);
            if (changeMatch.Success)
            {
                var changeDateText = changeMatch.Groups[1].Value;
                history.Add(new AdrChange(ParseDate(changeDateText, path, "Changes"), changeDateText,
                    changeMatch.Groups[2].Value, changeMatch.Groups[3].Value, changeMatch.Groups[4].Value));
            }
        }
        if (!inChanges || history.Count == 0)
        {
            throw new InvalidDataException($"ADR '{path}' must contain a Changes table.");
        }
        if (history[0].Type != "Accepted" || history[0].DateText != dateText)
        {
            throw new InvalidDataException($"ADR '{path}' must begin Changes with its original Accepted decision date.");
        }
        var previousDate = date;
        foreach (var change in history)
        {
            if (change.Date < previousDate)
            {
                throw new InvalidDataException($"ADR '{path}' Changes rows are not in chronological order.");
            }
            if (string.IsNullOrWhiteSpace(change.Change) || string.IsNullOrWhiteSpace(change.Reference))
            {
                throw new InvalidDataException($"ADR '{path}' has a Changes row without a description or reference.");
            }
            previousDate = change.Date;
        }
        if (history.Count > 1 && string.IsNullOrWhiteSpace(lastUpdated))
        {
            throw new InvalidDataException($"ADR '{path}' requires last-updated for material Changes rows after acceptance.");
        }
        if (!string.IsNullOrWhiteSpace(lastUpdated) && history[^1].DateText != lastUpdated)
        {
            throw new InvalidDataException($"ADR '{path}' last-updated must match its most recent Changes row.");
        }

        var isServer = id.StartsWith("ADR-S", StringComparison.Ordinal);
        var numberText = isServer ? id[5..] : id[3..];
        var number = int.Parse(numberText, CultureInfo.InvariantCulture);
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return new Adr(id, title, summary, dateText, date, lastUpdated, status, version, areas, relationships,
            relative, number, isServer ? 1 : 0);
    }

    private static string ParseYamlScalar(string raw, string path, string key)
    {
        var value = raw.Trim();
        if (value.Length == 0) return string.Empty;
        if (value.StartsWith('"') && value.EndsWith('"'))
        {
            try
            {
                return JsonSerializer.Deserialize<string>(value) ?? string.Empty;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"Invalid quoted value for '{key}' in '{path}': {value}", exception);
            }
        }
        if (value.StartsWith('\'') && value.EndsWith('\''))
        {
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }
        return value;
    }

    private static string[] ParseYamlList(string value, string path, string key)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']'))
        {
            throw new InvalidDataException($"'{key}' in '{path}' must be an inline YAML list.");
        }
        var contents = trimmed[1..^1].Trim();
        if (contents.Length == 0) return [];
        var items = contents.Split(',').Select(item => ParseYamlScalar(item, path, key)).ToArray();
        if (items.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException($"'{key}' in '{path}' contains an empty item.");
        }
        return items;
    }

    private static DateOnly ParseDate(string value, string path, string key)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new InvalidDataException($"ADR '{path}' has an invalid {key} date '{value}'.");
        }
        return date;
    }

    private static void ValidateSupersessionCycles(IReadOnlyList<Adr> adrs, IReadOnlyDictionary<string, Adr> byId)
    {
        foreach (var start in adrs)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(start.Relationships["supersedes"]);
            while (pending.TryPop(out var current))
            {
                if (string.Equals(current, start.Id, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"ADR supersession cycle contains '{start.Id}'.");
                }
                if (!visited.Add(current)) continue;
                foreach (var target in byId[current].Relationships["supersedes"]) pending.Push(target);
            }
        }
    }

    private static void WriteIndex(string path, string indexDirectory, string linkRoot, IReadOnlyList<Adr> adrs,
        IReadOnlyDictionary<string, List<string>> incomingSupersedes)
    {
        var core = adrs.Where(static adr => adr.NamespaceOrder == 0).ToArray();
        var server = adrs.Where(static adr => adr.NamespaceOrder == 1).ToArray();
        var lines = new List<string>
        {
            "---",
            "title: Architecture Decision Records",
            "_description: Recorded LeanCorpus and Community Server architecture decisions and their status.",
            "---",
            "",
            "# Architecture Decision Records",
            "",
            "This index is generated from the core and Community Server ADR records. Their",
            "original decisions remain canonical in the linked detail pages.",
            "",
            "## Core decisions",
            "",
            NewAdrTable(core, adrs, indexDirectory, linkRoot, incomingSupersedes),
            "",
            "## Community Server decisions",
            "",
            NewAdrTable(server, adrs, indexDirectory, linkRoot, incomingSupersedes),
            "",
            "## Template",
            "",
            "New records should follow the repository's ADR authoring template. Core",
            "identifiers use the reserved ADRnnn sequence; Server identifiers use ADR-Snnn."
        };
        File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string NewAdrTable(IReadOnlyList<Adr> records, IReadOnlyList<Adr> allAdrs, string indexDirectory, string linkRoot,
        IReadOnlyDictionary<string, List<string>> incomingSupersedes)
    {
        var rows = new List<string>();
        foreach (var adr in records)
        {
            var targetPath = Path.Combine(linkRoot, adr.File.Replace('/', Path.DirectorySeparatorChar));
            var href = Path.GetRelativePath(indexDirectory, targetPath).Replace('\\', '/');
            var supersedes = FormatAdrLinks(adr.Relationships["supersedes"], indexDirectory, linkRoot, allAdrs);
            var lastUpdated = string.IsNullOrEmpty(adr.LastUpdated) ? string.Empty : Html(adr.LastUpdated);
            rows.Add($"<tr><td><a href=\"{Html(href)}\">{Html(adr.Id)}</a></td><td>{Html(adr.DateText)}</td><td>{lastUpdated}</td><td>{Html(adr.Status)}</td><td>{supersedes}</td><td>{Html(adr.Title)}</td></tr>");
        }
        return string.Join(Environment.NewLine,
        [
            "<div class=\"table-responsive\">",
            "<table class=\"table table-sm table-striped adr-index-table\">",
            "<thead><tr><th>ADR</th><th>Date</th><th>Last updated</th><th>Status</th><th>Supersedes</th><th>Decision</th></tr></thead>",
            "<tbody>",
            string.Join(Environment.NewLine, rows),
            "</tbody></table></div>"
        ]);
    }

    private static string FormatAdrLinks(IEnumerable<string> ids, string baseDirectory, string linkRoot,
        IReadOnlyList<Adr> adrs)
    {
        var byId = adrs.ToDictionary(static adr => adr.Id, StringComparer.Ordinal);
        return string.Join(", ", ids.Select(id =>
        {
            if (!byId.TryGetValue(id, out var target))
            {
                throw new InvalidDataException($"Cannot render relationship to ADR '{id}' from the selected ADR records.");
            }
            var path = Path.Combine(linkRoot, target.File.Replace('/', Path.DirectorySeparatorChar));
            var relative = Path.GetRelativePath(baseDirectory, path).Replace('\\', '/');
            return $"<a href=\"{Html(relative)}\">{Html(id)}</a>";
        }));
    }

    private static void WriteToc(string path, IReadOnlyList<Adr> adrs)
    {
        var lines = new List<string>
        {
            "- name: Architecture Decision Records",
            "  href: index.md",
            "  items:"
        };
        foreach (var group in adrs.Where(static adr => adr.NamespaceOrder == 0)
            .GroupBy(static adr => (adr.Number - 1) / 10)
            .OrderBy(static group => group.Key))
        {
            var first = group.Key * 10 + 1;
            var last = first + 9;
            lines.Add($"    - name: 'ADR{first:D3}-{last:D3}'");
            lines.Add("      items:");
            foreach (var adr in group)
            {
                lines.Add($"        - name: '{adr.Id}: {adr.Title.Replace("'", "''", StringComparison.Ordinal)} ({adr.Status})'");
                lines.Add($"          href: {adr.File}");
            }
        }
        lines.Add("    - name: 'Community Server decisions'");
        lines.Add("      href: index.md#community-server-decisions");
        lines.Add("      items:");
        foreach (var adr in adrs.Where(static adr => adr.NamespaceOrder == 1))
        {
            lines.Add($"        - name: '{adr.Id}: {adr.Title.Replace("'", "''", StringComparison.Ordinal)} ({adr.Status})'");
            lines.Add($"          href: {adr.File}");
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteRelationshipSections(string stagingRoot, IReadOnlyList<Adr> adrs,
        IReadOnlyDictionary<string, List<string>> incomingSupersedes)
    {
        foreach (var adr in adrs)
        {
            var stagedPath = Path.GetFullPath(Path.Combine(stagingRoot, adr.File.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(stagedPath))
            {
                throw new FileNotFoundException($"Staged ADR is missing: {stagedPath}", stagedPath);
            }
            var content = RelationshipsBlockPattern.Replace(File.ReadAllText(stagedPath), string.Empty);
            var lines = new List<string> { "## Relationships", string.Empty };
            AddRelationshipLine(lines, "Supersedes", adr.Relationships["supersedes"], stagedPath, stagingRoot, adrs);
            AddRelationshipLine(lines, "Depends on", adr.Relationships["depends-on"], stagedPath, stagingRoot, adrs);
            AddRelationshipLine(lines, "Extends", adr.Relationships["extends"], stagedPath, stagingRoot, adrs);

            var related = new HashSet<string>(adr.Relationships["relates-to"], StringComparer.Ordinal);
            foreach (var candidate in adrs)
            {
                if (candidate.Relationships["relates-to"].Contains(adr.Id, StringComparer.Ordinal)) related.Add(candidate.Id);
            }
            AddRelationshipLine(lines, "Relates to", related.Order(StringComparer.Ordinal), stagedPath, stagingRoot, adrs);
            AddRelationshipLine(lines, "Superseded by", incomingSupersedes[adr.Id].Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), stagedPath, stagingRoot, adrs);
            if (lines.Count == 2) lines.Add("No recorded relationships.");

            var context = ContextHeadingPattern.Match(content);
            if (!context.Success)
            {
                throw new InvalidDataException($"Staged ADR '{adr.Id}' has no Context section for generated relationships.");
            }
            var block = string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine;
            content = content.Insert(context.Index, block);
            File.WriteAllText(stagedPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static void AddRelationshipLine(List<string> lines, string label, IEnumerable<string> ids,
        string stagedPath, string stagingRoot, IReadOnlyList<Adr> adrs)
    {
        var values = ids.ToArray();
        if (values.Length == 0) return;
        var baseDirectory = Path.GetDirectoryName(stagedPath)!;
        var links = FormatAdrLinks(values, baseDirectory, stagingRoot, adrs);
        lines.Add($"- **{label}:** {links}");
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private sealed record Adr(
        string Id,
        string Title,
        string Summary,
        string DateText,
        DateOnly Date,
        string LastUpdated,
        string Status,
        string VersionAdded,
        string[] Areas,
        IReadOnlyDictionary<string, string[]> Relationships,
        string File,
        int Number,
        int NamespaceOrder);

    private sealed record AdrChange(DateOnly Date, string DateText, string Type, string Change, string Reference);
}
