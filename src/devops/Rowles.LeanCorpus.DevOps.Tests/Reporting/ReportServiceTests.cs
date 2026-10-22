using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Reporting;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Reporting;

public sealed class ReportServiceTests
{
    [Fact]
    public async Task GitReportReadsHistoryAndContributorDataFromSelectedCheckout()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        await repository.WriteAsync("src/Feature/Alpha.cs", "namespace Feature; public sealed class Alpha { }\n");
        await repository.WriteAsync("docs/guide.md", "First revision.\n");
        await repository.CommitAsync("feat: add initial files", DateTimeOffset.UtcNow.AddDays(-1));
        await repository.WriteAsync("docs/guide.md", "Second revision.\n");
        await repository.CommitAsync("fix: update guide", DateTimeOffset.UtcNow);

        var service = new ReportService(new ProcessRunner());
        var result = await service.GenerateAsync(new ReportRequest
        {
            Checkout = repository.Identity,
            Group = "git",
            Top = 10
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        var runDirectory = Assert.IsType<string>(result.RunDirectory);
        Assert.StartsWith(Path.Combine(repository.Path, "artifacts", "report", "runs"), runDirectory, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(runDirectory, "run.json")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "report.json")));
        var logReferences = Assert.IsType<JsonArray>(result.Document!["logRefs"]);
        Assert.NotEmpty(logReferences);
        Assert.Contains(Assert.IsType<JsonArray>(result.Document["artifactRefs"]), item => item!.GetValue<string>() == "report.json");
        var childAttempts = Assert.IsType<JsonArray>(result.Document["childAttempts"]);
        Assert.NotEmpty(childAttempts);
        Assert.All(childAttempts, attempt => Assert.Equal(0, attempt!["rawExitCode"]!.GetValue<int>()));
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "run.json"))))
        {
            Assert.Equal("succeeded", manifest.RootElement.GetProperty("state").GetString());
            Assert.NotEmpty(manifest.RootElement.GetProperty("childExitCodes").EnumerateArray());
        }
        var git = Assert.IsType<JsonObject>(result.Document!["report"]!["Git"]);
        Assert.Contains(Assert.IsType<JsonArray>(result.Document["warnings"]), item =>
            item!.GetValue<string>().Contains("rename attribution is approximate", StringComparison.Ordinal));
        Assert.Equal(2, git["Overview"]!["Commits"]!.GetValue<int>());
        Assert.Equal(1, git["Overview"]!["BranchCount"]!.GetValue<int>());
        Assert.Equal("main", git["Overview"]!["Branch"]!.GetValue<string>());
        Assert.Single(Assert.IsType<JsonArray>(git["Contributors"]));
        Assert.Contains(Assert.IsType<JsonArray>(git["Churn"]!["TopAdded"]),
            item => item!["Path"]!.GetValue<string>() == "src/Feature/Alpha.cs");
    }

    [Fact]
    public async Task GitReportWarnsWhenSelectedCheckoutHasShallowHistory()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        await repository.WriteAsync("README.md", "first revision\n");
        await repository.CommitAsync("docs: add first revision", DateTimeOffset.UtcNow.AddDays(-1));
        await repository.WriteAsync("README.md", "second revision\n");
        await repository.CommitAsync("docs: add second revision", DateTimeOffset.UtcNow);
        var shallowCheckout = await repository.CloneShallowAsync();

        var result = await new ReportService(new ProcessRunner()).GenerateAsync(new ReportRequest
        {
            Checkout = shallowCheckout,
            Group = "git"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        var overview = result.Document!["report"]!["Git"]!["Overview"]!;
        Assert.False(overview["HistoryComplete"]!.GetValue<bool>());
        Assert.Contains(Assert.IsType<JsonArray>(result.Document["warnings"]), item =>
            item!.GetValue<string>().Contains("checkout is shallow", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GitHygieneMatchesPowerShellCaseInsensitiveConventionalCommitMatching()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        await repository.WriteAsync("README.md", "initial\n");
        await repository.CommitAsync("FIX: uppercase type", DateTimeOffset.UtcNow.AddDays(-1));
        await repository.WriteAsync("README.md", "updated\n");
        await repository.CommitAsync("feat: lowercase type", DateTimeOffset.UtcNow);

        var result = await new ReportService(new ProcessRunner()).GenerateAsync(new ReportRequest
        {
            Checkout = repository.Identity,
            Group = "git"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        var hygiene = result.Document!["report"]!["Git"]!["Hygiene"]!;
        Assert.Equal(2, hygiene["Conventional"]!.GetValue<int>());
        Assert.Equal(100.0, hygiene["ConventionalPct"]!.GetValue<double>());
    }

    [Fact]
    public async Task GitChurnCarriesLineCountsAcrossSimilarityDetectedSourceRenames()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        await repository.WriteAsync("src/OldName.cs", "using System;\nnamespace Example;\npublic sealed class OldName\n{\n    public int Value => 1;\n}\n");
        await repository.CommitAsync("feat: add source type", DateTimeOffset.UtcNow.AddDays(-1));
        repository.Move("src/OldName.cs", "src/NewName.cs");
        await repository.WriteAsync("src/NewName.cs", "using System;\nnamespace Example;\npublic sealed class NewName\n{\n    public int Value => 1;\n}\n");
        await repository.CommitAsync("refactor: rename source type", DateTimeOffset.UtcNow);

        var result = await new ReportService(new ProcessRunner()).GenerateAsync(new ReportRequest
        {
            Checkout = repository.Identity,
            Group = "git",
            Top = 10
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        var git = result.Document!["report"]!["Git"]!;
        Assert.Equal(7, git["Churn"]!["TotalAdded"]!.GetValue<int>());
        Assert.Equal(1, git["Churn"]!["TotalDeleted"]!.GetValue<int>());
        Assert.Contains(Assert.IsType<JsonArray>(git["Churn"]!["TopAdded"]), item =>
            item!["Path"]!.GetValue<string>() == "src/NewName.cs" && item["Added"]!.GetValue<int>() == 7 && item["Deleted"]!.GetValue<int>() == 1);
    }

    [Fact]
    public async Task ReportProvenanceDoesNotCountItsOwnRunDirectoryAsSourceChanges()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        await repository.WriteAsync("README.md", "clean input\n");
        await repository.CommitAsync("docs: add readme", DateTimeOffset.UtcNow);

        var result = await new ReportService(new ProcessRunner()).GenerateAsync(new ReportRequest
        {
            Checkout = repository.Identity,
            Group = "git"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.False(result.Document!["sourceIdentity"]!["dirty"]!.GetValue<bool>());
    }

    [Fact]
    public async Task FileReportAppliesPathGlobAndProducesDeterministicExtensionCounts()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        await repository.WriteAsync("docs/one.md", "one\n");
        await repository.WriteAsync("docs/two.md", "two\n");
        await repository.WriteAsync("src/ignored.cs", "class Ignored {}\n");
        await repository.CommitAsync("docs: add files", DateTimeOffset.UtcNow);

        var result = await new ReportService(new ProcessRunner()).GenerateAsync(new ReportRequest
        {
            Checkout = repository.Identity,
            Group = "files",
            Path = "docs/**",
            Top = 10
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        var byType = Assert.IsType<JsonArray>(result.Document!["report"]!["Files"]!["ByType"]);
        var markdown = Assert.Single(byType);
        Assert.Equal("md", markdown!["Extension"]!.GetValue<string>());
        Assert.Equal(2, markdown["Count"]!.GetValue<int>());
    }

    [Fact]
    public async Task FileHistoryHandlesNewlinesTabsUnicodeAndRenamesInTrackedPaths()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        var tabPath = "docs/with\t tab.md";
        var newlinePath = "docs/with\nnewline.md";
        var renamedPath = "docs/α renamed.md";
        await repository.CommitTreeAsync(
            "docs: add unusual paths",
            DateTimeOffset.UtcNow.AddDays(-1),
            (tabPath, "first revision\n"),
            (newlinePath, "newline path\n"));
        await repository.CommitTreeAsync(
            "docs: rename unusual path",
            DateTimeOffset.UtcNow,
            (renamedPath, "first revision\n"),
            (newlinePath, "newline path\n"));

        var result = await new ReportService(new ProcessRunner()).GenerateAsync(new ReportRequest
        {
            Checkout = repository.Identity,
            Group = "files",
            Top = 10
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        var history = Assert.IsType<JsonObject>(result.Document!["report"]!["Files"]!["History"]);
        var touched = Assert.IsType<JsonArray>(history["MostTouched"]);
        Assert.Contains(touched, item => item!["Path"]!.GetValue<string>() == renamedPath && item["Touches"]!.GetValue<int>() == 2);
        Assert.Contains(touched, item => item!["Path"]!.GetValue<string>() == newlinePath);
    }

    [Fact]
    public async Task StrictReportReturnsFailureWhenTrackedNamesBreakCrossPlatformRules()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        await repository.CommitTreeAsync(
            "feat: add a non-portable file name",
            DateTimeOffset.UtcNow,
            ("src/core/Bad?.cs", "namespace Example; public sealed class BadName { }\n"));

        var result = await new ReportService(new ProcessRunner()).GenerateAsync(new ReportRequest
        {
            Checkout = repository.Identity,
            Group = "files",
            Strict = true
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        Assert.Equal(1, result.Document!["strictViolationCount"]!.GetValue<int>());
        Assert.Equal("CommandFailure", result.Document["outcome"]!.GetValue<string>());
    }

    [Fact]
    public async Task CodeReportUsesSyntaxFactsAndRetainsAotHygieneFindings()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        await repository.WriteAsync("src/core/Product.cs", "namespace Product; public partial class Product { public dynamic Create() => Activator.CreateInstance(typeof(Product))!; }\n");
        await repository.WriteAsync("src/devops/Rowles.LeanCorpus.Tests.Core/ProductTests.cs", "namespace Tests; public sealed class ProductTests { }\n");
        await repository.CommitAsync("feat: add product", DateTimeOffset.UtcNow);

        var result = await new ReportService(new ProcessRunner()).GenerateAsync(new ReportRequest
        {
            Checkout = repository.Identity,
            Group = "code",
            Top = 10
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        var code = Assert.IsType<JsonObject>(result.Document!["report"]!["Code"]);
        var syntax = Assert.IsType<JsonObject>(code["Syntax"]);
        Assert.Equal(2, syntax["TypeCount"]!.GetValue<int>());
        Assert.Equal(1, syntax["PartialTypeCount"]!.GetValue<int>());
        var aotFiles = Assert.IsType<JsonArray>(code["AotHygiene"]!["AotFiles"]);
        Assert.Contains(aotFiles, item => item!["Path"]!.GetValue<string>() == "src/core/Product.cs");
        Assert.Contains(Assert.IsType<JsonArray>(result.Document!["warnings"]), item =>
            item!.GetValue<string>().Contains("do not prove runtime or reflection behaviour", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeReportUsesTheSelectedSdkToEvaluateProjectReferencesAndPackages()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        await repository.WriteAsync("global.json", "{\"sdk\":{\"version\":\"11.0.100-preview.7.26381.103\",\"rollForward\":\"latestPatch\",\"allowPrerelease\":true}}\n");
        await repository.WriteAsync("Rowles.LeanCorpus.slnx", "<Solution><Project Path=\"src/One/One.csproj\" /><Project Path=\"src/Two/Two.csproj\" /></Solution>\n");
        await repository.WriteAsync("src/One/One.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"../Two/Two.csproj\" /><PackageReference Include=\"Example.Package\" Version=\"1.0.0\" /></ItemGroup></Project>\n");
        await repository.WriteAsync("src/One/One.cs", "namespace One; public sealed class OneType { }\n");
        await repository.WriteAsync("src/Two/Two.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n");
        await repository.WriteAsync("src/Two/Two.cs", "namespace Two; public sealed class TwoType { }\n");
        await repository.CommitAsync("feat: add two projects", DateTimeOffset.UtcNow);

        var result = await new ReportService(new ProcessRunner()).GenerateAsync(new ReportRequest
        {
            Checkout = repository.Identity,
            Group = "code",
            Top = 10
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        var dependencies = Assert.IsType<JsonObject>(result.Document!["report"]!["Code"]!["Dependencies"]);
        Assert.Equal(2, dependencies["ProjectCount"]!.GetValue<int>());
        Assert.Equal(1, dependencies["TotalPackageRefs"]!.GetValue<int>());
        Assert.True(dependencies["SolutionPersistenceMatchesDotNetCli"]!.GetValue<bool>());
        var projects = Assert.IsType<JsonArray>(dependencies["Projects"]);
        var one = Assert.Single(projects, item => item!["Name"]!.GetValue<string>() == "One");
        Assert.Equal("Two", one!["Refs"]![0]!.GetValue<string>());
        Assert.Empty(Assert.IsType<JsonArray>(dependencies["Cycles"]));
    }

    [Fact]
    public async Task GitFailureIsInfrastructureFailureRatherThanAnEmptyCleanReport()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));
        var checkout = new CheckoutIdentity(
            directory.Path,
            Path.Combine(directory.Path, ".git"),
            CheckoutGitMarkerKind.Directory);

        var result = await new ReportService(new ProcessRunner()).GenerateAsync(new ReportRequest
        {
            Checkout = checkout,
            Group = "git"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.InfrastructureFailure, result.CommandResult.Outcome);
        Assert.NotNull(result.Document);
        Assert.Contains("repository", result.CommandResult.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitChildTimeoutRemainsTimeoutInTheReportAndManifest()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        await repository.WriteAsync("README.md", "committed source\n");
        await repository.CommitAsync("docs: add readme", DateTimeOffset.UtcNow);

        var result = await new ReportService(new ResultProcessRunner(OutcomeKind.Timeout)).GenerateAsync(new ReportRequest
        {
            Checkout = repository.Identity,
            Group = "git"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Timeout, result.CommandResult.Outcome);
        Assert.Equal("Timeout", result.Document!["outcome"]!.GetValue<string>());
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.RunDirectory!, "run.json")));
        Assert.Equal("timedOut", manifest.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task InvalidRequestFailsBeforeStartingAnyChildProcess()
    {
        var runner = new RecordingProcessRunner();
        using var directory = new TemporaryDirectory();

        var result = await new ReportService(runner).GenerateAsync(new ReportRequest
        {
            Checkout = new CheckoutIdentity(directory.Path, directory.Path, CheckoutGitMarkerKind.Directory),
            Group = "unknown",
            Top = 0
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.ValidationFailure, result.CommandResult.Outcome);
        Assert.Equal(0, runner.CallCount);
    }

    private sealed class TemporaryGitRepository : IDisposable
    {
        private TemporaryGitRepository(string path)
        {
            Path = path;
            Identity = new CheckoutIdentity(path, System.IO.Path.Combine(path, ".git"), CheckoutGitMarkerKind.Directory);
        }

        public string Path { get; }

        public CheckoutIdentity Identity { get; }

        public static async Task<TemporaryGitRepository> CreateAsync()
        {
            var directory = new TemporaryDirectory();
            await RunGitAsync(directory.Path, "init", "--initial-branch=main");
            await RunGitAsync(directory.Path, "config", "user.name", "Report Test");
            await RunGitAsync(directory.Path, "config", "user.email", "report-test@example.invalid");
            return new TemporaryGitRepository(directory.Path, directory);
        }

        private TemporaryGitRepository(string path, TemporaryDirectory owner) : this(path)
            => _owner = owner;

        private readonly TemporaryDirectory? _owner;

        public async Task WriteAsync(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
        }

        public async Task CommitAsync(string subject, DateTimeOffset time)
        {
            await RunGitAsync(Path, "add", "--all");
            await RunGitAsync(Path, new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_DATE"] = time.ToString("O"),
                    ["GIT_COMMITTER_DATE"] = time.ToString("O")
                }, "-c", "user.name=Report Test", "-c", "user.email=report-test@example.invalid", "commit", "--date", time.ToString("O"), "-m", subject);
        }

        public async Task CommitTreeAsync(string subject, DateTimeOffset time, params (string RelativePath, string Content)[] files)
        {
            var blobs = new List<(string RelativePath, string BlobId)>(files.Length);
            foreach (var (relativePath, content) in files)
            {
                var blobId = await RunGitWithInputForOutputAsync(Path, Encoding.UTF8.GetBytes(content), "hash-object", "-w", "--stdin");
                blobs.Add((relativePath, blobId.Trim()));
            }

            var tree = await CreateTreeAsync(blobs);
            var environment = new Dictionary<string, string>
            {
                ["GIT_AUTHOR_NAME"] = "Report Test",
                ["GIT_AUTHOR_EMAIL"] = "report-test@example.invalid",
                ["GIT_AUTHOR_DATE"] = time.ToString("O"),
                ["GIT_COMMITTER_NAME"] = "Report Test",
                ["GIT_COMMITTER_EMAIL"] = "report-test@example.invalid",
                ["GIT_COMMITTER_DATE"] = time.ToString("O")
            };
            var arguments = new List<string> { "commit-tree", tree };
            if (_treeHead is not null)
            {
                arguments.AddRange(["-p", _treeHead]);
            }

            arguments.AddRange(["-m", subject]);
            _treeHead = (await RunGitForOutputAsync(Path, environment, arguments.ToArray())).Trim();
            await RunGitAsync(Path, "update-ref", "refs/heads/main", _treeHead);
            await RunGitAsync(Path, "config", "core.protectNTFS", "false");
            await RunGitAsync(Path, "read-tree", "HEAD");
        }

        private string? _treeHead;

        private async Task<string> CreateTreeAsync(IReadOnlyList<(string RelativePath, string BlobId)> files)
        {
            var entries = new StringBuilder();
            var groups = files.GroupBy(file =>
            {
                var separator = file.RelativePath.IndexOf('/');
                return separator < 0 ? file.RelativePath : file.RelativePath[..separator];
            }, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal);

            foreach (var group in groups)
            {
                var directFiles = new List<(string RelativePath, string BlobId)>();
                var nestedFiles = new List<(string RelativePath, string BlobId)>();
                foreach (var file in group)
                {
                    var separator = file.RelativePath.IndexOf('/');
                    if (separator < 0)
                    {
                        directFiles.Add(file);
                    }
                    else
                    {
                        nestedFiles.Add((file.RelativePath[(separator + 1)..], file.BlobId));
                    }
                }

                if (directFiles.Count > 0)
                {
                    if (directFiles.Count != 1 || nestedFiles.Count != 0)
                    {
                        throw new InvalidOperationException($"Conflicting Git tree fixture path '{group.Key}'.");
                    }

                    entries.Append("100644 blob ").Append(directFiles[0].BlobId).Append('\t').Append(group.Key).Append('\0');
                }
                else
                {
                    var nestedTree = await CreateTreeAsync(nestedFiles);
                    entries.Append("040000 tree ").Append(nestedTree).Append('\t').Append(group.Key).Append('\0');
                }
            }

            return (await RunGitWithInputForOutputAsync(Path, Encoding.UTF8.GetBytes(entries.ToString()), "mktree", "-z")).Trim();
        }

        public void Move(string sourceRelativePath, string targetRelativePath)
        {
            var source = System.IO.Path.Combine(Path, sourceRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            var target = System.IO.Path.Combine(Path, targetRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.Move(source, target);
        }

        public async Task<CheckoutIdentity> CloneShallowAsync()
        {
            var clonePath = System.IO.Path.Combine(_owner!.Path, "shallow-clone");
            await RunGitAsync(_owner.Path, "clone", "--depth=1", new Uri(Path).AbsoluteUri, clonePath);
            return new CheckoutIdentity(clonePath, System.IO.Path.Combine(clonePath, ".git"), CheckoutGitMarkerKind.Directory);
        }

        public void Dispose() => _owner?.Dispose();

        private static async Task RunGitAsync(string workingDirectory, params string[] arguments)
            => await RunGitAsync(workingDirectory, null, arguments);

        private static async Task RunGitAsync(string workingDirectory, IReadOnlyDictionary<string, string>? environment, params string[] arguments)
            => _ = await RunGitForOutputAsync(workingDirectory, environment, arguments);

        private static async Task<string> RunGitForOutputAsync(string workingDirectory, IReadOnlyDictionary<string, string>? environment, params string[] arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
            if (environment is not null)
            {
                foreach (var pair in environment)
                {
                    process.StartInfo.Environment[pair.Key] = pair.Value;
                }
            }

            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
            }

            return output;
        }

        private static async Task<string> RunGitWithInputForOutputAsync(string workingDirectory, byte[] input, params string[] arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.StandardInput.BaseStream.WriteAsync(input);
            process.StandardInput.Close();
            await process.WaitForExitAsync();
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
            }

            return output;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"devops report fixture {Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                if (OperatingSystem.IsWindows())
                {
                    foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                    {
                        var attributes = File.GetAttributes(file);
                        if ((attributes & FileAttributes.ReadOnly) != 0)
                        {
                            File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                        }
                    }
                }

                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public int CallCount { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("The invalid report request started a child process.");
        }
    }

    private sealed class ResultProcessRunner(OutcomeKind outcome) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ProcessResult(
                outcome,
                null,
                [],
                [],
                0,
                0,
                TimeSpan.FromSeconds(1),
                request.StandardOutputLogPath,
                request.StandardErrorLogPath,
                outcome == OutcomeKind.Timeout ? "The child process exceeded its timeout." : null));
    }
}
