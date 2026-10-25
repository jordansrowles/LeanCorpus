using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.FileSystemGlobbing;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Reporting;

internal sealed record ReportRequest
{
    public required CheckoutIdentity Checkout { get; init; }

    public string Group { get; init; } = "all";

    public int Top { get; init; } = 10;

    public string? Path { get; init; }

    public bool Strict { get; init; }
}

internal sealed record ReportExecutionResult(CommandResult CommandResult, JsonObject? Document, string? RunDirectory);

internal sealed class ReportService(IProcessRunner processRunner, Action<string>? progress = null)
{
    private static readonly Regex AotHostilePattern = new(
        @"\b(dynamic|MakeGenericType|MakeGenericMethod|Assembly\.Load|Marshal\.|Activator\.CreateInstance|Type\.GetType|GetConstructor|GetMethod|Reflection\.Emit)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ConventionalPattern = new(
        @"^(feat|fix|chore|docs|test|refactor|perf|build|ci|style|revert)(\([^)]*\))?!?:",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const long LargeFileThresholdBytes = 500 * 1024;
    private const int CoChangeMaximumFiles = 20;
    private const int CoChangeMinimumCount = 2;
    private const int RecentWindowDays = 30;
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    public async Task<ReportExecutionResult> GenerateAsync(ReportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RepositoryReportGroupCatalog.TryParse(request.Group, out var group))
        {
            return Invalid(request, $"Unknown report group '{request.Group}'. Valid groups: {string.Join(", ", RepositoryReportGroupCatalog.Names)}.");
        }
        var groupName = RepositoryReportGroupCatalog.GetName(group);
        if (request.Top is < 1 or > 1000)
        {
            return Invalid(request, "Report --top must be between 1 and 1000.");
        }
        if (request.Path is { Length: 0 })
        {
            return Invalid(request, "Report --path must not be empty.");
        }

        var root = Path.GetFullPath(request.Checkout.RepositoryRoot);
        var artifactPaths = new ArtifactPaths(root);
        var runStore = new ArtifactRunStore(artifactPaths, runCategory: "report");
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["group"] = groupName,
            ["top"] = request.Top.ToString(CultureInfo.InvariantCulture),
            ["strict"] = request.Strict.ToString(CultureInfo.InvariantCulture)
        };
        if (request.Path is not null)
        {
            inputs["path"] = request.Path;
        }

        RunManifest? manifest = null;
        GitCommandContext? git = null;
        ReportProcessRunner? processes = null;
        var startedUtc = DateTimeOffset.UtcNow;
        var elapsed = Stopwatch.StartNew();
        try
        {
            using var repository = new LibGit2Sharp.Repository(root);
            var repositorySnapshot = CreateRepositorySnapshot(repository);
            _ = await runStore.ReadAllAsync(cancellationToken).ConfigureAwait(false);
            manifest = await runStore.StartAsync("report", inputs, cancellationToken: cancellationToken).ConfigureAwait(false);
            var runDirectory = artifactPaths.GetRunDirectory("report", manifest.RunId);
            Directory.CreateDirectory(runDirectory);
            processes = new ReportProcessRunner(processRunner, runDirectory);
            git = new GitCommandContext(processes, request.Checkout, runDirectory, progress);

            var sourceCommit = repositorySnapshot.Head;
            var sourceBranch = repositorySnapshot.Branch;
            var sourceDirty = repositorySnapshot.Dirty;
            manifest = await runStore.UpdateRunningAsync(
                manifest,
                sourceCommit,
                sourceBranch,
                sourceDirty,
                toolVersions: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["LibGit2Sharp"] = typeof(LibGit2Sharp.Repository).Assembly.GetName().Version?.ToString() ?? "unknown"
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var started = DateTimeOffset.UtcNow;
            var sections = new JsonObject();
            GitHistory? history = null;
            IReadOnlyList<TrackedFile>? tree = null;
            if (group is RepositoryReportGroup.All or RepositoryReportGroup.Git or RepositoryReportGroup.Files)
            {
                history = await ReadGitHistoryAsync(git, cancellationToken).ConfigureAwait(false);
            }
            if (group is RepositoryReportGroup.All or RepositoryReportGroup.Files)
            {
                tree = await ReadTrackedFilesAsync(git, cancellationToken).ConfigureAwait(false);
            }
            if (group is RepositoryReportGroup.All or RepositoryReportGroup.Git)
            {
                sections["Git"] = await BuildGitReportAsync(repositorySnapshot, git, history!, request.Top, cancellationToken).ConfigureAwait(false);
            }
            if (group is RepositoryReportGroup.All or RepositoryReportGroup.Files)
            {
                sections["Files"] = BuildFileReport(history!, tree!, request.Path, request.Top);
            }
            if (group is RepositoryReportGroup.All or RepositoryReportGroup.Code)
            {
                sections["Code"] = await BuildCodeReportAsync(git, request.Path, request.Top, cancellationToken).ConfigureAwait(false);
            }

            var violations = CountStrictViolations(sections);
            var outcome = request.Strict && violations > 0 ? OutcomeKind.CommandFailure : OutcomeKind.Success;
            var ended = DateTimeOffset.UtcNow;
            elapsed.Stop();
            var document = CreateDocument(manifest, request, groupName, sourceCommit, sourceBranch, sourceDirty,
                outcome, started, ended, elapsed.Elapsed, sections, violations, errors: [], processes,
                warnings: CollectWarnings(sections));
            var reportPath = Path.Combine(runDirectory, "report.json");
            await WriteJsonAtomicallyAsync(reportPath, document, cancellationToken).ConfigureAwait(false);
            var logReferences = processes.LogReferences;
            var finished = await runStore.FinishAsync(
                manifest,
                outcome == OutcomeKind.Success ? RunState.Succeeded : RunState.Failed,
                artifacts: logReferences.Append(Path.GetRelativePath(runDirectory, reportPath)),
                failure: outcome == OutcomeKind.Success ? null : $"{violations} strict report violation(s) were found.",
                cancellationToken: cancellationToken,
                childExitCodes: processes.ChildExitCodes).ConfigureAwait(false);
            _ = finished;
            return new ReportExecutionResult(
                new CommandResult(outcome, outcome == OutcomeKind.Success
                    ? $"Repository report generated. Run evidence: {runDirectory}"
                    : $"{violations} strict report violation(s) were found.", Details: runDirectory),
                document,
                runDirectory);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            elapsed.Stop();
            var runDirectory = manifest is null ? null : artifactPaths.GetRunDirectory("report", manifest.RunId);
            var document = manifest is null ? ErrorDocument(OutcomeKind.Cancelled, "Report generation was cancelled.", request) :
                CreateDocument(manifest, request, groupName, manifest.SourceCommit, manifest.SourceBranch, manifest.SourceDirty,
                    OutcomeKind.Cancelled, startedUtc, DateTimeOffset.UtcNow, elapsed.Elapsed, new JsonObject(), 0,
                    ["Report generation was cancelled."], processes);
            if (manifest is not null)
            {
                if (runDirectory is not null)
                {
                    try
                    {
                        await WriteJsonAtomicallyAsync(Path.Combine(runDirectory, "report.json"), document, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
                    {
                    }
                }
                await runStore.FinishAsync(manifest, RunState.Cancelled,
                    artifacts: processes?.LogReferences.Append("report.json"),
                    failure: "Report generation was cancelled.",
                    cancellationToken: CancellationToken.None,
                    childExitCodes: processes?.ChildExitCodes).ConfigureAwait(false);
            }
            return new ReportExecutionResult(new CommandResult(OutcomeKind.Cancelled, "Report generation was cancelled."), document, runDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or Win32Exception or LibGit2SharpException or JsonException)
        {
            elapsed.Stop();
            var message = $"Could not generate the repository report: {exception.Message}";
            var outcome = cancellationToken.IsCancellationRequested || processes?.LatestOutcome == OutcomeKind.Cancelled
                ? OutcomeKind.Cancelled
                : processes?.LatestOutcome == OutcomeKind.Timeout ? OutcomeKind.Timeout : OutcomeKind.InfrastructureFailure;
            var terminalState = outcome switch
            {
                OutcomeKind.Cancelled => RunState.Cancelled,
                OutcomeKind.Timeout => RunState.TimedOut,
                _ => RunState.Failed
            };
            var runDirectory = manifest is null ? null : artifactPaths.GetRunDirectory("report", manifest.RunId);
            var document = manifest is null ? ErrorDocument(outcome, message, request) :
                CreateDocument(manifest, request, groupName, manifest.SourceCommit, manifest.SourceBranch, manifest.SourceDirty,
                    outcome, startedUtc, DateTimeOffset.UtcNow, elapsed.Elapsed, new JsonObject(), 0, [message], processes);
            if (manifest is not null)
            {
                if (runDirectory is not null)
                {
                    try
                    {
                        var reportPath = Path.Combine(runDirectory, "report.json");
                        await WriteJsonAtomicallyAsync(reportPath, document, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
                    {
                        message += $" Report evidence could not be written: {writeException.Message}";
                    }
                }
                await runStore.FinishAsync(manifest, terminalState,
                    artifacts: processes?.LogReferences.Append("report.json"),
                    failure: message,
                    cancellationToken: CancellationToken.None,
                    childExitCodes: processes?.ChildExitCodes).ConfigureAwait(false);
            }
            return new ReportExecutionResult(new CommandResult(outcome, message, Details: runDirectory), document, runDirectory);
        }
    }

    private static ReportExecutionResult Invalid(ReportRequest request, string message)
        => new(new CommandResult(OutcomeKind.ValidationFailure, message), ErrorDocument(OutcomeKind.ValidationFailure, message, request), null);

    private static JsonObject ErrorDocument(OutcomeKind outcome, string message, ReportRequest? request = null)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return new JsonObject
        {
            ["contractVersion"] = 1,
            ["command"] = "report",
            ["runId"] = null,
            ["sourceIdentity"] = null,
            ["requestSummary"] = request is null ? null : new JsonObject
            {
                ["group"] = request.Group.Trim().ToLowerInvariant(),
                ["top"] = request.Top,
                ["path"] = request.Path,
                ["strict"] = request.Strict
            },
            ["state"] = GetStateName(outcome),
            ["outcome"] = outcome.ToString(),
            ["startedUtc"] = now,
            ["endedUtc"] = now,
            ["elapsedMs"] = 0,
            ["childAttempts"] = new JsonArray(),
            ["logRefs"] = new JsonArray(),
            ["artifactRefs"] = new JsonArray(),
            ["warnings"] = new JsonArray(),
            ["errors"] = new JsonArray(message)
        };
    }

    private static JsonObject CreateDocument(
        RunManifest manifest,
        ReportRequest request,
        string group,
        string? sourceCommit,
        string? sourceBranch,
        bool sourceDirty,
        OutcomeKind outcome,
        DateTimeOffset started,
        DateTimeOffset ended,
        TimeSpan elapsed,
        JsonObject sections,
        int violations,
        IReadOnlyList<string> errors,
        ReportProcessRunner? processes,
        IReadOnlyList<string>? warnings = null)
        => new()
        {
            ["contractVersion"] = 1,
            ["command"] = "report",
            ["runId"] = manifest.RunId.ToString("D"),
            ["sourceIdentity"] = new JsonObject
            {
                ["commit"] = sourceCommit,
                ["branch"] = sourceBranch,
                ["dirty"] = sourceDirty
            },
            ["requestSummary"] = new JsonObject
            {
                ["group"] = group,
                ["top"] = request.Top,
                ["path"] = request.Path,
                ["strict"] = request.Strict
            },
            ["state"] = GetStateName(outcome),
            ["outcome"] = outcome.ToString(),
            ["startedUtc"] = started.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["endedUtc"] = ended.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["elapsedMs"] = (long)elapsed.TotalMilliseconds,
            ["strictViolationCount"] = violations,
            ["childAttempts"] = processes?.ToJsonArray() ?? new JsonArray(),
            ["logRefs"] = ToJsonArray(processes?.LogReferences ?? []),
            ["artifactRefs"] = ToJsonArray((processes?.LogReferences ?? []).Append("report.json")),
            ["report"] = sections,
            ["warnings"] = ToJsonArray(warnings ?? []),
            ["errors"] = new JsonArray(errors.Select(error => (JsonNode?)JsonValue.Create(error)).ToArray())
        };

    private static JsonArray ToJsonArray(IEnumerable<string> values)
        => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static IReadOnlyList<string> CollectWarnings(JsonObject sections)
    {
        var warnings = new List<string>();
        if (sections["Git"] is JsonObject git && git["Overview"] is JsonObject overview)
        {
            if (overview["HistoryComplete"]?.GetValue<bool>() == false && overview["HistoryNote"]?.GetValue<string>() is { Length: > 0 } historyNote)
            {
                warnings.Add(historyNote);
            }
            if (overview["RenameAnalysisNote"]?.GetValue<string>() is { Length: > 0 } renameNote)
            {
                warnings.Add(renameNote);
            }
        }
        if (sections.ContainsKey("Code"))
        {
            warnings.Add("C# syntax counts are structural indicators and do not prove runtime or reflection behaviour.");
        }
        return warnings;
    }

    private static string GetStateName(OutcomeKind outcome)
        => outcome switch
        {
            OutcomeKind.Success => "succeeded",
            OutcomeKind.Cancelled => "cancelled",
            OutcomeKind.Timeout => "timedOut",
            _ => "failed"
        };

    private static async Task WriteJsonAtomicallyAsync(string path, JsonObject document, CancellationToken cancellationToken)
    {
        var writer = new AtomicFileWriter();
        await writer.WriteReplaceAsync(path, JsonSerializer.SerializeToUtf8Bytes(document, ReportJsonOptions), cancellationToken).ConfigureAwait(false);
    }

    private async Task<GitHistory> ReadGitHistoryAsync(GitCommandContext git, CancellationToken cancellationToken)
    {
        var output = await git.ReadTextAsync(["log", "--numstat", "-M", "-z", "--format=%x01%cI"], cancellationToken).ConfigureAwait(false);
        var files = new Dictionary<string, FileHistory>(StringComparer.Ordinal);
        var coChange = new Dictionary<FilePair, int>();
        var commitDates = new List<DateTimeOffset>();
        var currentPaths = new List<string>();
        var currentDate = DateTimeOffset.MinValue;
        var recent = false;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RecentWindowDays);

        var records = output.Split('\0');
        for (var index = 0; index < records.Length; index++)
        {
            var value = records[index];
            if (value.Length == 0)
            {
                continue;
            }
            var marker = value.IndexOf('\u0001');
            if (marker >= 0)
            {
                AddCoChange(currentPaths, coChange);
                currentPaths.Clear();
                currentDate = DateTimeOffset.Parse(value[(marker + 1)..].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
                recent = currentDate >= cutoff;
                commitDates.Add(currentDate);
                continue;
            }

            value = value.TrimStart('\r', '\n');
            var firstSeparator = value.IndexOf('\t');
            var secondSeparator = firstSeparator < 0 ? -1 : value.IndexOf('\t', firstSeparator + 1);
            if (firstSeparator < 0 || secondSeparator < 0)
            {
                continue;
            }
            var added = value[..firstSeparator];
            var deleted = value[(firstSeparator + 1)..secondSeparator];
            var path = value[(secondSeparator + 1)..];
            if (path.Length == 0 && index + 2 < records.Length)
            {
                index++;
                index++;
                path = records[index];
            }
            if (path.Length == 0)
            {
                continue;
            }
            currentPaths.Add(path);
            if (!files.TryGetValue(path, out var entry))
            {
                entry = new FileHistory { First = currentDate, Last = currentDate };
                files.Add(path, entry);
            }
            entry.Count++;
            entry.First = currentDate;
            if (int.TryParse(added, NumberStyles.None, CultureInfo.InvariantCulture, out var additions))
            {
                entry.Added += additions;
            }
            if (int.TryParse(deleted, NumberStyles.None, CultureInfo.InvariantCulture, out var deletions))
            {
                entry.Deleted += deletions;
            }
            if (recent)
            {
                entry.Recent++;
            }
        }
        AddCoChange(currentPaths, coChange);

        var renameMap = await ReadRenameMapAsync(git, cancellationToken).ConfigureAwait(false);
        var tracked = (await git.ReadTextAsync(["ls-files", "-z"], cancellationToken).ConfigureAwait(false))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        var merged = new Dictionary<string, FileHistory>(StringComparer.Ordinal);
        foreach (var (path, value) in files)
        {
            var canonical = ResolveRename(path, renameMap);
            if (!tracked.Contains(canonical))
            {
                continue;
            }
            if (!merged.TryGetValue(canonical, out var target))
            {
                target = value.Clone();
                merged.Add(canonical, target);
            }
            else
            {
                target.Count += value.Count;
                target.Added += value.Added;
                target.Deleted += value.Deleted;
                target.Recent += value.Recent;
                target.First = target.First < value.First ? target.First : value.First;
                target.Last = target.Last > value.Last ? target.Last : value.Last;
            }
        }

        var filteredPairs = new Dictionary<FilePair, int>();
        foreach (var (pair, count) in coChange)
        {
            var left = ResolveRename(pair.Left, renameMap);
            var right = ResolveRename(pair.Right, renameMap);
            if (left == right || !tracked.Contains(left) || !tracked.Contains(right))
            {
                continue;
            }
            var normalized = FilePair.Create(left, right);
            filteredPairs[normalized] = filteredPairs.GetValueOrDefault(normalized) + count;
        }

        return new GitHistory(merged, commitDates, filteredPairs);
    }

    private async Task<IReadOnlyList<TrackedFile>> ReadTrackedFilesAsync(GitCommandContext git, CancellationToken cancellationToken)
    {
        var output = await git.ReadTextAsync(["ls-tree", "-r", "-z", "--format=%(objectsize)%x09%(path)", "HEAD"], cancellationToken).ConfigureAwait(false);
        var files = new List<TrackedFile>();
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = record.IndexOf('\t');
            if (separator <= 0 || !long.TryParse(record.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var size))
            {
                continue;
            }
            files.Add(new TrackedFile(record[(separator + 1)..], size));
        }
        return files;
    }

    private static GitRepositorySnapshot CreateRepositorySnapshot(LibGit2Sharp.Repository repository)
    {
        var status = repository.RetrieveStatus(new StatusOptions
            {
                IncludeUntracked = true,
                IncludeIgnored = false,
                RecurseUntrackedDirs = true
            })
            .Where(entry => entry.State != FileStatus.Unaltered)
            .Select(entry => new RepositoryStatusEntry(entry.FilePath, entry.State.ToString()))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();
        var branches = repository.Branches.Where(branch => !branch.IsRemote)
            .Select(branch => branch.FriendlyName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var tags = repository.Tags.Select(tag => tag.FriendlyName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        return new GitRepositorySnapshot(repository.Head.Tip?.Sha, repository.Head.FriendlyName, status.Length > 0, status, branches, tags);
    }

    private static async Task<Dictionary<string, string>> ReadRenameMapAsync(GitCommandContext git, CancellationToken cancellationToken)
    {
        var output = await git.ReadTextAsync(["log", "--diff-filter=R", "--name-status", "-M", "--format=", "-z"], cancellationToken).ConfigureAwait(false);
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        var records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 2 < records.Length; index++)
        {
            if (records[index].StartsWith("R", StringComparison.Ordinal))
            {
                renames[records[index + 1]] = records[index + 2];
                index += 2;
            }
        }
        return renames;
    }

    private static void AddCoChange(IReadOnlyList<string> paths, Dictionary<FilePair, int> pairs)
    {
        if (paths.Count < 2 || paths.Count > CoChangeMaximumFiles)
        {
            return;
        }
        for (var leftIndex = 0; leftIndex < paths.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < paths.Count; rightIndex++)
            {
                var pair = FilePair.Create(paths[leftIndex], paths[rightIndex]);
                pairs[pair] = pairs.GetValueOrDefault(pair) + 1;
            }
        }
    }

    private static string ResolveRename(string path, IReadOnlyDictionary<string, string> renameMap)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = path;
        while (renameMap.TryGetValue(current, out var next) && seen.Add(current))
        {
            current = next;
        }
        return current;
    }

    private async Task<JsonObject> BuildGitReportAsync(GitRepositorySnapshot repository, GitCommandContext git, GitHistory history, int top, CancellationToken cancellationToken)
    {
        var dates = history.CommitDates;
        var dateRows = dates.GroupBy(date => date.ToString("yyyy-MM", CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new { Month = group.Key, Commits = group.Count() }).ToArray();

        var contributorLines = await git.ReadTextAsync(["log", "--format=%ae%x09%an"], cancellationToken).ConfigureAwait(false);
        var contributorsByEmail = new Dictionary<string, Contributor>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in contributorLines.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('\t');
            var email = (separator < 0 ? line : line[..separator]).Trim();
            var name = (separator < 0 ? email : line[(separator + 1)..]).Trim();
            if (email.Length == 0)
            {
                continue;
            }
            if (!contributorsByEmail.TryGetValue(email, out var contributor))
            {
                contributorsByEmail[email] = new Contributor(email, name, 1);
            }
            else
            {
                contributorsByEmail[email] = contributor with { Name = name, Commits = contributor.Commits + 1 };
            }
        }
        var contributors = contributorsByEmail.Values.OrderByDescending(item => item.Commits)
            .ThenBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Email, StringComparer.Ordinal)
            .Take(top).ToArray();

        var subjects = (await git.ReadTextAsync(["log", "--format=%s"], cancellationToken).ConfigureAwait(false))
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var conventional = subjects.Count(subject => ConventionalPattern.IsMatch(subject));
        var coAuthorCount = (await git.ReadTextAsync(["log", "--grep=Co-authored-by", "--format=%h"], cancellationToken).ConfigureAwait(false))
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
        var sourceChurn = history.Files.Where(pair => pair.Key.StartsWith("src/", StringComparison.Ordinal)).ToArray();
        var topAdded = sourceChurn.Select(pair => new { Path = pair.Key, Added = pair.Value.Added, Deleted = pair.Value.Deleted })
            .OrderByDescending(item => item.Added).ThenBy(item => item.Path, StringComparer.Ordinal).Take(top).ToArray();
        var topDeleted = sourceChurn.Select(pair => new { Path = pair.Key, Added = pair.Value.Added, Deleted = pair.Value.Deleted })
            .OrderByDescending(item => item.Deleted).ThenBy(item => item.Path, StringComparer.Ordinal).Take(top).ToArray();
        var recent = history.Files.Where(pair => pair.Value.Recent > 0)
            .Select(pair => new { Path = pair.Key, Recent = pair.Value.Recent })
            .OrderByDescending(item => item.Recent).ThenBy(item => item.Path, StringComparer.Ordinal).Take(top).ToArray();
        var countText = await git.ReadTextAsync(["rev-list", "--count", "HEAD"], cancellationToken).ConfigureAwait(false);
        var commitCount = int.TryParse(countText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : dates.Count;
        var isShallow = string.Equals((await git.ReadTextAsync(["rev-parse", "--is-shallow-repository"], cancellationToken).ConfigureAwait(false)).Trim(), "true", StringComparison.OrdinalIgnoreCase);
        var overview = new
        {
            Commits = commitCount,
            FirstCommit = dates.Count > 0 ? dates[^1].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "n/a",
            LastCommit = dates.Count > 0 ? dates[0].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "n/a",
            Head = repository.Head,
            Branch = repository.Branch,
            BranchCount = repository.Branches.Length,
            TagCount = repository.Tags.Length,
            HistoryComplete = !isShallow,
            HistoryNote = isShallow ? "History is incomplete because this checkout is shallow." : null,
            RenameAnalysisNote = "File history follows Git similarity-based rename detection; rename attribution is approximate."
        };
        var hygiene = new
        {
            TotalSubjects = subjects.Length,
            Conventional = conventional,
            ConventionalPct = subjects.Length == 0 ? 0.0 : Math.Round(100.0 * conventional / subjects.Length, 1),
            CoAuthorCommits = coAuthorCount
        };
        var churn = new
        {
            TotalAdded = sourceChurn.Sum(pair => pair.Value.Added),
            TotalDeleted = sourceChurn.Sum(pair => pair.Value.Deleted),
            TopAdded = topAdded,
            TopDeleted = topDeleted
        };
        return ToJsonObject(new
        {
            Overview = overview,
            Branches = repository.Branches,
            Tags = repository.Tags,
            Status = new { Clean = !repository.Dirty, Entries = repository.Status },
            Contributors = contributors,
            Activity = dateRows,
            Hygiene = hygiene,
            Churn = churn,
            Recent = recent
        });
    }

    private static JsonObject BuildFileReport(GitHistory history, IReadOnlyList<TrackedFile> tree, string? pathPattern, int top)
    {
        var matcher = CreateMatcher(pathPattern);
        var scopedTree = tree.Where(item => matcher is null || matcher.Match(item.Path).HasMatches).ToArray();
        var historyRows = history.Files.Select(pair => new FileHistoryRow(pair.Key, pair.Value.Count, pair.Value.First, pair.Value.Last)).ToArray();
        var historyDocument = new
        {
            MostTouched = historyRows.OrderByDescending(row => row.Touches).ThenBy(row => row.Path, StringComparer.Ordinal).Take(top).ToArray(),
            LeastTouched = historyRows.Where(row => row.Touches > 1).OrderBy(row => row.Touches).ThenBy(row => row.Path, StringComparer.Ordinal).Take(top).ToArray(),
            Newest = historyRows.OrderByDescending(row => row.Last).ThenBy(row => row.Path, StringComparer.Ordinal).Take(top).ToArray(),
            Oldest = historyRows.OrderBy(row => row.First).ThenBy(row => row.Path, StringComparer.Ordinal).Take(top).ToArray(),
            Stale = historyRows.Where(row => row.Touches > 1).OrderBy(row => row.Last).ThenBy(row => row.Path, StringComparer.Ordinal).Take(top).ToArray(),
            CreatedOnce = historyRows.Where(row => row.Touches == 1).OrderBy(row => row.First).ThenBy(row => row.Path, StringComparer.Ordinal).ToArray()
        };
        var coChange = history.CoChange.Where(pair => pair.Value >= CoChangeMinimumCount)
            .Select(pair => new { Left = pair.Key.Left, Right = pair.Key.Right, Together = pair.Value })
            .OrderByDescending(pair => pair.Together).ThenBy(pair => pair.Left, StringComparer.Ordinal).ThenBy(pair => pair.Right, StringComparer.Ordinal)
            .Take(top).ToArray();
        var byType = scopedTree.GroupBy(item => Path.GetExtension(item.Path).TrimStart('.').ToLowerInvariant() is { Length: > 0 } extension ? extension : "(none)", StringComparer.Ordinal)
            .Select(group => new { Extension = group.Key, Count = group.Count() })
            .OrderByDescending(row => row.Count).ThenBy(row => row.Extension, StringComparer.Ordinal).ToArray();
        var large = scopedTree.Where(item => item.Size > LargeFileThresholdBytes).OrderByDescending(item => item.Size)
            .ThenBy(item => item.Path, StringComparer.Ordinal).ToArray();
        var illegal = GetIllegalNames(scopedTree);
        return ToJsonObject(new
        {
            History = historyDocument,
            CoChange = coChange,
            ByType = byType,
            Large = new { ThresholdBytes = LargeFileThresholdBytes, Count = large.Length, Files = large.Take(top).ToArray() },
            Illegal = illegal
        });
    }

    private async Task<JsonObject> BuildCodeReportAsync(GitCommandContext git, string? pathPattern, int top, CancellationToken cancellationToken)
    {
        var source = await git.ReadTextAsync(["ls-files", "-z", "--", "*.cs"], cancellationToken).ConfigureAwait(false);
        var allCs = source.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => CreateMatcher(pathPattern) is not { } matcher || matcher.Match(path).HasMatches)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var production = allCs.Where(IsProductionFile).ToArray();
        var analysis = new List<CodeFileAnalysis>();
        foreach (var relativePath in allCs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(git.RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }
            analysis.Add(await AnalyseCodeFileAsync(relativePath, fullPath, cancellationToken).ConfigureAwait(false));
        }
        var productionSet = production.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productionAnalysis = analysis.Where(item => productionSet.Contains(item.Path)).ToArray();
        var dependencyGraph = await BuildDependencyGraphAsync(git, cancellationToken).ConfigureAwait(false);
        var untested = GetUntestedSource(allCs, top);
        var syntax = GetSyntaxFacts(analysis);
        var code = new
        {
            GodClasses = GetGodClasses(productionAnalysis, top),
            Dependencies = dependencyGraph,
            Untested = untested,
            Syntax = syntax,
            AotHygiene = new
            {
                AotFiles = productionAnalysis.Where(item => item.AotMatches.Length > 0).Select(item => new { item.Path, item.AotMatches }).ToArray(),
                BomFiles = analysis.Where(item => item.HasBom).Select(item => item.Path).ToArray(),
                CrlfFiles = analysis.Where(item => item.HasCrlf).Select(item => item.Path).ToArray(),
                TrailingWhitespaceFiles = analysis.Where(item => item.HasTrailingWhitespace).Select(item => item.Path).ToArray(),
                TabIndentFiles = analysis.Where(item => item.HasTabs).Select(item => item.Path).ToArray()
            }
        };
        return ToJsonObject(code);
    }

    private async Task<object?> BuildDependencyGraphAsync(GitCommandContext git, CancellationToken cancellationToken)
    {
        var solutionPath = RepositoryPaths.GetSolutionPath(git.RepositoryRoot);
        IReadOnlyList<string> projects;
        int? solutionProjectCount = null;
        int trackedProjectCount;
        bool? solutionGraphMatchesCli = null;
        string? solutionGraphNote = null;
        if (File.Exists(solutionPath))
        {
            var inspection = await new DotNetProjectInspector(git.ProcessRunner)
                .InspectSolutionAsync(git.Checkout, git.RunDirectory, cancellationToken).ConfigureAwait(false);
            if (inspection.Outcome != OutcomeKind.Success)
            {
                throw new InvalidOperationException(inspection.Failure ?? "Could not inspect the repository solution graph.");
            }
            var solutionProjects = inspection.Projects
                .Select(project => Path.GetRelativePath(git.RepositoryRoot, project.ProjectPath).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var trackedSource = await git.ReadTextAsync(["ls-files", "-z", "--", "*.csproj"], cancellationToken).ConfigureAwait(false);
            var trackedProjects = trackedSource.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            projects = solutionProjects.Concat(trackedProjects)
                .Distinct(pathComparer)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            solutionProjectCount = solutionProjects.Length;
            trackedProjectCount = trackedProjects.Length;
            solutionGraphMatchesCli = inspection.MatchesDotNetCli;
            solutionGraphNote = inspection.Failure;
        }
        else
        {
            var source = await git.ReadTextAsync(["ls-files", "-z", "--", "*.csproj"], cancellationToken).ConfigureAwait(false);
            projects = source.Split('\0', StringSplitOptions.RemoveEmptyEntries).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            trackedProjectCount = projects.Count;
        }
        if (projects.Count == 0)
        {
            return null;
        }

        var resolver = new DotNetSdkResolver(git.ProcessRunner);
        var sdk = await resolver.ResolveAsync(git.Checkout, git.RunDirectory, cancellationToken).ConfigureAwait(false);
        if (sdk.Outcome != OutcomeKind.Success)
        {
            throw new InvalidOperationException(sdk.Failure ?? "Could not resolve the repository SDK for project graph evaluation.");
        }

        var metadata = new List<ProjectDependency>();
        var projectIndex = 0;
        foreach (var project in projects)
        {
            var projectPath = Path.Combine(git.RepositoryRoot, project.Replace('/', Path.DirectorySeparatorChar));
            var log = await git.RunTextAsync(["msbuild", projectPath, "-getItem:ProjectReference,PackageReference", "-property:Configuration=Release", "--disable-build-servers"], "dotnet", cancellationToken).ConfigureAwait(false);
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(log);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"MSBuild returned invalid structured item data for '{project}': {exception.Message}", exception);
            }
            using (document)
            {
                var root = document.RootElement;
                var items = root.TryGetProperty("Items", out var itemElement) ? itemElement : root;
                var references = ReadItemIdentities(items, "ProjectReference");
                var packages = ReadItemIdentities(items, "PackageReference");
                metadata.Add(new ProjectDependency(Path.GetFileNameWithoutExtension(project), references, packages.Length));
                projectIndex++;
                if (progress is not null && (projectIndex % 5 == 0 || projectIndex == projects.Count))
                {
                    progress($"Report project metadata: {projectIndex}/{projects.Count} projects evaluated.");
                }
            }
        }

        var names = metadata.Select(project => project.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inbound = metadata.ToDictionary(project => project.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var project in metadata)
        {
            foreach (var reference in project.References)
            {
                var target = Path.GetFileNameWithoutExtension(reference);
                if (names.Contains(target))
                {
                    inbound[target]++;
                }
            }
        }
        var cycles = FindCycles(metadata);
        var ordered = metadata.OrderBy(project => project.Name, StringComparer.Ordinal)
            .Select(project => new { project.Name, Refs = project.References.Select(Path.GetFileNameWithoutExtension).OrderBy(name => name, StringComparer.Ordinal).ToArray(), PackageCount = project.PackageCount, Inbound = inbound[project.Name] })
            .ToArray();
        return new
        {
            ProjectCount = metadata.Count,
            SolutionProjectCount = solutionProjectCount,
            TrackedProjectCount = trackedProjectCount,
            TotalPackageRefs = metadata.Sum(project => project.PackageCount),
            SolutionPersistenceMatchesDotNetCli = solutionGraphMatchesCli,
            SolutionGraphNote = solutionGraphNote,
            Projects = ordered,
            Cycles = cycles
        };
    }

    private static string[] ReadItemIdentities(JsonElement items, string name)
    {
        if (!items.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return array.EnumerateArray()
            .Where(item => item.TryGetProperty("Identity", out _))
            .Select(item => item.GetProperty("Identity").GetString() ?? string.Empty)
            .Where(value => value.Length > 0).ToArray();
    }

    private static string[] FindCycles(IReadOnlyList<ProjectDependency> projects)
    {
        var names = projects.Select(project => project.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outgoing = projects.ToDictionary(
            project => project.Name,
            project => project.References.Select(reference => Path.GetFileNameWithoutExtension(reference))
                .Where(name => name is not null && names.Contains(name)).Select(name => name!).ToArray(),
            StringComparer.OrdinalIgnoreCase);
        var indegree = projects.ToDictionary(project => project.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var references in outgoing.Values)
        {
            foreach (var reference in references)
            {
                indegree[reference]++;
            }
        }
        var queue = new Queue<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key).OrderBy(name => name, StringComparer.Ordinal));
        while (queue.TryDequeue(out var name))
        {
            foreach (var reference in outgoing[name])
            {
                if (--indegree[reference] == 0)
                {
                    queue.Enqueue(reference);
                }
            }
        }
        return indegree.Where(pair => pair.Value > 0).Select(pair => pair.Key).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static object GetGodClasses(IReadOnlyList<CodeFileAnalysis> analysis, int top)
    {
        var basePaths = analysis.GroupBy(item => Path.GetFileNameWithoutExtension(item.Path), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Path, StringComparer.Ordinal);
        var groups = new Dictionary<string, List<CodeFileAnalysis>>(StringComparer.Ordinal);
        foreach (var item in analysis)
        {
            var baseName = Path.GetFileNameWithoutExtension(item.Path);
            var group = baseName;
            var dot = baseName.IndexOf('.');
            if (dot > 0 && basePaths.TryGetValue(baseName[..dot], out var basePath) &&
                string.Equals(Path.GetDirectoryName(basePath), Path.GetDirectoryName(item.Path), StringComparison.Ordinal))
            {
                group = baseName[..dot];
            }
            if (!groups.TryGetValue(group, out var files))
            {
                files = [];
                groups.Add(group, files);
            }
            files.Add(item);
        }
        var rows = groups.Select(pair =>
        {
            var lines = pair.Value.Sum(item => item.Lines);
            var severity = lines > 800 ? "severe" : lines > 400 ? "warn" : "ok";
            return new { Type = pair.Key, Lines = lines, Files = pair.Value.Select(item => item.Path).OrderBy(path => path, StringComparer.Ordinal).ToArray(), Severity = severity };
        }).OrderByDescending(item => item.Lines).ThenBy(item => item.Type, StringComparer.Ordinal).ToArray();
        return new
        {
            WarnThreshold = 400,
            SevereThreshold = 800,
            Flagged = rows.Where(item => item.Severity != "ok").ToArray(),
            SevereCount = rows.Count(item => item.Severity == "severe"),
            Top = rows.Take(top).ToArray()
        };
    }

    private static object GetUntestedSource(IReadOnlyList<string> files, int top)
    {
        var testStems = files.Where(IsTestOrBenchmarkFile).Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var production = files.Where(path => (path.StartsWith("src/core/", StringComparison.Ordinal) || path.StartsWith("src/server/", StringComparison.Ordinal)) && !IsTestOrBenchmarkFile(path)).ToArray();
        var untested = production.Where(path =>
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var dot = name.IndexOf('.');
            var baseName = dot > 0 ? name[..dot] : name;
            return !testStems.Contains(name + "Tests") && !testStems.Contains(name + "Test") &&
                !testStems.Contains(baseName + "Tests") && !testStems.Contains(baseName + "Test");
        }).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        return new { ProductionCount = production.Length, TestFileCount = testStems.Count, UntestedCount = untested.Length, Untested = untested.Take(top).ToArray() };
    }

    private static object GetSyntaxFacts(IReadOnlyList<CodeFileAnalysis> analysis)
    {
        var types = analysis.SelectMany(item => item.Types).ToArray();
        var partialCount = types.Where(type => type.Partial)
            .Select(type => $"{type.Namespace}:{type.Kind}:{type.Name}")
            .Distinct(StringComparer.Ordinal)
            .Count();
        return new
        {
            FileCount = analysis.Count,
            TypeCount = types.Length,
            PartialTypeCount = partialCount,
            MethodCount = analysis.Sum(item => item.MethodCount),
            DynamicTypeCount = analysis.Sum(item => item.DynamicTypeCount),
            ReflectionPatternCount = analysis.Sum(item => item.AotMatches.Length),
            Types = types.OrderBy(type => type.Namespace, StringComparer.Ordinal).ThenBy(type => type.Name, StringComparer.Ordinal).ToArray()
        };
    }

    private static async Task<CodeFileAnalysis> AnalyseCodeFileAsync(string relativePath, string fullPath, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(bytes);
        var lines = text.Split('\n');
        var loc = lines.Count(line =>
        {
            var trimmed = line.Trim();
            return trimmed.Length > 0 && trimmed is not "{" and not "}" && !trimmed.StartsWith("//", StringComparison.Ordinal) &&
                !trimmed.StartsWith("/*", StringComparison.Ordinal) && !trimmed.StartsWith('*') && !trimmed.StartsWith("*/", StringComparison.Ordinal);
        });
        var tree = CSharpSyntaxTree.ParseText(text, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var namespaceName = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? string.Empty;
        var types = root.DescendantNodes().Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            .Select(node => node switch
            {
                BaseTypeDeclarationSyntax type => new SyntaxType(type.Identifier.ValueText, type.Kind().ToString(),
                    type.Parent is BaseNamespaceDeclarationSyntax namespaceSyntax ? namespaceSyntax.Name.ToString() : namespaceName,
                    type.Modifiers.Any(modifier => modifier.ValueText == "partial")),
                DelegateDeclarationSyntax @delegate => new SyntaxType(@delegate.Identifier.ValueText, "Delegate", namespaceName,
                    @delegate.Modifiers.Any(modifier => modifier.ValueText == "partial")),
                _ => throw new InvalidOperationException("Unexpected type declaration node.")
            }).ToArray();
        var methods = root.DescendantNodes().Count(node => node is MethodDeclarationSyntax or ConstructorDeclarationSyntax or LocalFunctionStatementSyntax);
        var dynamicCount = root.DescendantNodes().OfType<PredefinedTypeSyntax>().Count(node => node.Keyword.ValueText == "dynamic");
        var aotMatches = AotHostilePattern.Matches(text).Select(match => match.Value).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return new CodeFileAnalysis(
            relativePath,
            loc,
            hasBom,
            text.Contains("\r\n", StringComparison.Ordinal),
            Regex.IsMatch(text, @"(?m)[ \t]+$", RegexOptions.CultureInvariant),
            Regex.IsMatch(text, @"(?m)^\t", RegexOptions.CultureInvariant),
            aotMatches,
            types,
            methods,
            dynamicCount);
    }

    private static object GetIllegalNames(IReadOnlyList<TrackedFile> tree)
    {
        var reservedChars = new List<string>();
        var deviceNames = new List<string>();
        var trailing = new List<string>();
        var control = new List<string>();
        var lowerIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL" };
        for (var index = 0; index <= 9; index++)
        {
            devices.Add($"COM{index}");
            devices.Add($"LPT{index}");
        }
        foreach (var item in tree)
        {
            var name = Path.GetFileName(item.Path);
            if (Regex.IsMatch(name, @"[<>:""\\|?*]", RegexOptions.CultureInvariant)) reservedChars.Add(item.Path);
            if (Regex.IsMatch(name, @"[\x00-\x1f]", RegexOptions.CultureInvariant)) control.Add(item.Path);
            if (Regex.IsMatch(name, @"[. ]$", RegexOptions.CultureInvariant)) trailing.Add(item.Path);
            var dot = name.IndexOf('.');
            var stem = dot > 0 ? name[..dot] : name;
            if (devices.Contains(stem)) deviceNames.Add(item.Path);
            if (!lowerIndex.TryGetValue(item.Path.ToLowerInvariant(), out var names))
            {
                names = [];
                lowerIndex.Add(item.Path.ToLowerInvariant(), names);
            }
            names.Add(item.Path);
        }
        var collisions = lowerIndex.Where(pair => pair.Value.Count > 1)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new { Key = pair.Key, Paths = pair.Value.OrderBy(path => path, StringComparer.Ordinal).ToArray() }).ToArray();
        return new
        {
            ReservedChars = reservedChars,
            DeviceNames = deviceNames,
            TrailingDotSpace = trailing,
            ControlChars = control,
            Collisions = collisions,
            ViolationCount = reservedChars.Count + deviceNames.Count + trailing.Count + control.Count + collisions.Length
        };
    }

    private static bool IsProductionFile(string path)
    {
        if (!path.StartsWith("src/core/", StringComparison.Ordinal) && !path.StartsWith("src/server/", StringComparison.Ordinal))
        {
            return false;
        }
        string[] exclusions = ["Tests", "Benchmarks", "Example", "Examples", "Profiling", "AOTSmoke", "SourceGen"];
        return !path.Split('/').Any(segment => exclusions.Any(exclusion => segment.Contains(exclusion, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsTestOrBenchmarkFile(string path)
        => path.Split('/').Any(segment => segment.Contains("Tests", StringComparison.OrdinalIgnoreCase) || segment.Contains("Benchmarks", StringComparison.OrdinalIgnoreCase));

    private static Matcher? CreateMatcher(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude(pattern.Replace('\\', '/'));
        return matcher;
    }

    private static int CountStrictViolations(JsonObject sections)
    {
        var violations = 0;
        if (sections["Files"]?["Illegal"]?["ViolationCount"]?.GetValue<int>() is { } illegal)
        {
            violations += illegal > 0 ? 1 : 0;
        }
        if (sections["Code"]?["GodClasses"]?["SevereCount"]?.GetValue<int>() is { } severe && severe > 0)
        {
            violations++;
        }
        if (sections["Code"]?["AotHygiene"]?["AotFiles"] is JsonArray aotFiles && aotFiles.Count > 0)
        {
            violations++;
        }
        return violations;
    }

    private static JsonObject ToJsonObject<T>(T value)
        => JsonSerializer.SerializeToNode(value, ReportJsonOptions) as JsonObject ?? throw new InvalidOperationException("The report serializer did not produce an object.");

    private readonly record struct FilePair(string Left, string Right)
    {
        public static FilePair Create(string left, string right) => StringComparer.Ordinal.Compare(left, right) <= 0 ? new FilePair(left, right) : new FilePair(right, left);
    }

    private sealed record Contributor(string Email, string Name, int Commits);

    private sealed record RepositoryStatusEntry(string Path, string State);

    private sealed record GitRepositorySnapshot(
        string? Head,
        string Branch,
        bool Dirty,
        RepositoryStatusEntry[] Status,
        string[] Branches,
        string[] Tags);

    private sealed record TrackedFile(string Path, long Size);

    private sealed record FileHistoryRow(string Path, int Touches, DateTimeOffset First, DateTimeOffset Last);

    private sealed record SyntaxType(string Name, string Kind, string Namespace, bool Partial);

    private sealed record ProjectDependency(string Name, string[] References, int PackageCount);

    private sealed record GitHistory(Dictionary<string, FileHistory> Files, List<DateTimeOffset> CommitDates, Dictionary<FilePair, int> CoChange);

    private sealed class FileHistory
    {
        public int Count { get; set; }
        public DateTimeOffset First { get; set; }
        public DateTimeOffset Last { get; set; }
        public int Added { get; set; }
        public int Deleted { get; set; }
        public int Recent { get; set; }

        public FileHistory Clone() => new() { Count = Count, First = First, Last = Last, Added = Added, Deleted = Deleted, Recent = Recent };
    }

    private sealed record CodeFileAnalysis(
        string Path,
        int Lines,
        bool HasBom,
        bool HasCrlf,
        bool HasTrailingWhitespace,
        bool HasTabs,
        string[] AotMatches,
        SyntaxType[] Types,
        int MethodCount,
        int DynamicTypeCount);

    private sealed class GitCommandContext(IProcessRunner runner, CheckoutIdentity checkout, string runDirectory, Action<string>? progress)
    {
        private int _nextLog;
        public CheckoutIdentity Checkout { get; } = checkout;
        public string RepositoryRoot { get; } = checkout.RepositoryRoot;
        public string RunDirectory { get; } = runDirectory;
        public IProcessRunner ProcessRunner { get; } = runner;
        public List<string> LogPaths { get; } = [];
        public IEnumerable<string> RelativeLogPaths => LogPaths.Select(path => Path.GetRelativePath(RunDirectory, path));

        public Task<string> ReadTextAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
            => RunTextAsync(arguments, "git", cancellationToken);

        public async Task<string> RunTextAsync(IReadOnlyList<string> arguments, string executable, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _nextLog);
            var stdoutPath = Path.Combine(RunDirectory, $"child-{index:D3}.stdout.log");
            var stderrPath = Path.Combine(RunDirectory, $"child-{index:D3}.stderr.log");
            LogPaths.Add(stdoutPath);
            LogPaths.Add(stderrPath);
            var fullArguments = executable == "git" ? new[] { "-C", RepositoryRoot }.Concat(arguments).ToArray() : arguments.ToArray();
            var result = await ProcessRunner.RunAsync(new ProcessRequest
            {
                ExecutablePath = executable,
                Arguments = fullArguments,
                WorkingDirectory = RepositoryRoot,
                StandardOutputLogPath = stdoutPath,
                StandardErrorLogPath = stderrPath,
                ProgressInterval = TimeSpan.FromSeconds(15),
                Progress = progress is null ? null : update => progress(
                    $"Report child '{Path.GetFileName(executable)}' is still running after {update.Elapsed.ToString("c", CultureInfo.InvariantCulture)} " +
                    $"(stdout lines: {update.StandardOutputLineCount}; stderr lines: {update.StandardErrorLineCount}). Full output is retained in the run logs.")
            }, cancellationToken).ConfigureAwait(false);
            if (result.Outcome != OutcomeKind.Success || result.RawExitCode != 0)
            {
                var detail = result.Failure ?? string.Join(Environment.NewLine, result.StandardErrorTail);
                throw new InvalidOperationException($"{executable} {string.Join(' ', arguments)} failed with exit code {result.RawExitCode?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}. {detail}");
            }
            return await File.ReadAllTextAsync(stdoutPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record ReportChildAttempt(
        int Sequence,
        string Executable,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        DateTimeOffset StartedUtc,
        DateTimeOffset EndedUtc,
        TimeSpan Duration,
        OutcomeKind Outcome,
        int? RawExitCode,
        double? TimeoutMilliseconds,
        string? StandardOutputLogRef,
        string? StandardErrorLogRef,
        IReadOnlyList<string> StandardOutputTail,
        IReadOnlyList<string> StandardErrorTail,
        string? Failure);

    private sealed class ReportProcessRunner(IProcessRunner inner, string runDirectory) : IProcessRunner
    {
        private readonly object _gate = new();
        private readonly List<ReportChildAttempt> _attempts = [];
        private int _nextSequence;

        public IReadOnlyList<string> LogReferences
        {
            get
            {
                lock (_gate)
                {
                    return _attempts
                        .SelectMany(attempt => new[] { attempt.StandardOutputLogRef, attempt.StandardErrorLogRef })
                        .Where(reference => reference is not null)
                        .Select(reference => reference!)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                }
            }
        }

        public IReadOnlyList<int> ChildExitCodes
        {
            get
            {
                lock (_gate)
                {
                    return _attempts.Where(attempt => attempt.RawExitCode.HasValue)
                        .Select(attempt => attempt.RawExitCode!.Value)
                        .ToArray();
                }
            }
        }

        public OutcomeKind? LatestOutcome
        {
            get
            {
                lock (_gate)
                {
                    return _attempts.Count == 0 ? null : _attempts.MaxBy(attempt => attempt.Sequence)!.Outcome;
                }
            }
        }

        public JsonArray ToJsonArray()
        {
            lock (_gate)
            {
                return new JsonArray(_attempts.OrderBy(attempt => attempt.Sequence).Select(attempt => (JsonNode?)new JsonObject
                {
                    ["executable"] = attempt.Executable,
                    ["arguments"] = ReportService.ToJsonArray(attempt.Arguments),
                    ["workingDirectory"] = attempt.WorkingDirectory,
                    ["environmentOverrideNames"] = new JsonArray(),
                    ["startedUtc"] = attempt.StartedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["endedUtc"] = attempt.EndedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["elapsedMs"] = (long)attempt.Duration.TotalMilliseconds,
                    ["timeoutMs"] = attempt.TimeoutMilliseconds,
                    ["outcome"] = attempt.Outcome.ToString(),
                    ["rawExitCode"] = attempt.RawExitCode,
                    ["standardOutputLogRef"] = attempt.StandardOutputLogRef,
                    ["standardErrorLogRef"] = attempt.StandardErrorLogRef,
                    ["standardOutputTail"] = ReportService.ToJsonArray(attempt.StandardOutputTail),
                    ["standardErrorTail"] = ReportService.ToJsonArray(attempt.StandardErrorTail),
                    ["failure"] = attempt.Failure
                }).ToArray());
            }
        }

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var started = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            ProcessResult? result = null;
            Exception? exception = null;
            try
            {
                result = await inner.RunAsync(request, cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception caught)
            {
                exception = caught;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                var ended = DateTimeOffset.UtcNow;
                var attempt = new ReportChildAttempt(
                    Interlocked.Increment(ref _nextSequence),
                    request.ExecutablePath,
                    RedactArguments(request.Arguments),
                    request.WorkingDirectory,
                    started,
                    ended,
                    result?.Duration ?? stopwatch.Elapsed,
                    result?.Outcome ?? OutcomeKind.InfrastructureFailure,
                    result?.RawExitCode,
                    request.Timeout?.TotalMilliseconds,
                    GetLogReference(request.StandardOutputLogPath),
                    GetLogReference(request.StandardErrorLogPath),
                    result?.StandardOutputTail ?? [],
                    result?.StandardErrorTail ?? [],
                    result?.Failure ?? exception?.Message);
                lock (_gate)
                {
                    _attempts.Add(attempt);
                }
            }
        }

        private string? GetLogReference(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(path);
            var relative = Path.GetRelativePath(runDirectory, fullPath);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return null;
            }

            return relative.Replace('\\', '/');
        }

        private static IReadOnlyList<string> RedactArguments(IReadOnlyList<string> arguments)
        {
            var redacted = new string[arguments.Count];
            var redactNext = false;
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                if (redactNext)
                {
                    redacted[index] = "<redacted>";
                    redactNext = false;
                    continue;
                }

                var separator = argument.IndexOf('=');
                var key = separator < 0 ? argument : argument[..separator];
                if (!IsSensitiveArgument(key))
                {
                    redacted[index] = argument;
                    continue;
                }

                if (separator >= 0)
                {
                    redacted[index] = $"{key}=<redacted>";
                }
                else
                {
                    redacted[index] = argument;
                    redactNext = true;
                }
            }

            return redacted;
        }

        private static bool IsSensitiveArgument(string argument)
        {
            var key = argument.TrimStart('-', '/').Replace('_', '-').ToLowerInvariant();
            return key.Contains("password", StringComparison.Ordinal) ||
                key.Contains("token", StringComparison.Ordinal) ||
                key.Contains("secret", StringComparison.Ordinal) ||
                key.Contains("credential", StringComparison.Ordinal) ||
                key.Contains("api-key", StringComparison.Ordinal) ||
                key.Contains("apikey", StringComparison.Ordinal) ||
                key.Contains("access-key", StringComparison.Ordinal) ||
                key.Contains("authorization", StringComparison.Ordinal);
        }
    }
}
