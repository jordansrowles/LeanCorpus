using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Build.Locator;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Build;

internal sealed record SolutionProjectEntry(string Name, string ProjectPath);

internal sealed record SolutionInspectionResult(
    OutcomeKind Outcome,
    string SolutionPath,
    IReadOnlyList<SolutionProjectEntry> Projects,
    bool MatchesDotNetCli,
    string? Failure);

internal sealed record ProjectInspectionResult(
    OutcomeKind Outcome,
    string ProjectPath,
    string? Configuration,
    string? TargetFrameworks,
    string? TargetFramework,
    string? TargetPath,
    bool UsedMsBuildApi,
    bool ApiMatchesCli,
    string? MsBuildVersion,
    string? Failure);

internal interface IDotNetProjectInspector
{
    Task<SolutionInspectionResult> InspectSolutionAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken);

    Task<ProjectInspectionResult> InspectProjectAsync(
        CheckoutIdentity checkout,
        string projectPath,
        string configuration,
        string framework,
        string sdkDirectory,
        string runDirectory,
        CancellationToken cancellationToken);
}

internal sealed class DotNetProjectInspector(IProcessRunner processRunner, string dotnetExecutable = "dotnet") : IDotNetProjectInspector
{
    private static readonly object RegistrationGate = new();
    private static string? _registeredSdkDirectory;

    public async Task<SolutionInspectionResult> InspectSolutionAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
    {
        var solutionPath = RepositoryPaths.GetSolutionPath(checkout.RepositoryRoot);
        if (!File.Exists(solutionPath))
        {
            return new SolutionInspectionResult(OutcomeKind.ValidationFailure, solutionPath, [], false, "The LeanCorpus .slnx file is missing.");
        }

        try
        {
            var serializer = SolutionSerializers.SlnXml;
            var model = await serializer.OpenAsync(solutionPath, cancellationToken).ConfigureAwait(false);
            var projects = model.SolutionProjects
                .Select(project =>
                {
                    var relativePath = project.FilePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    return new SolutionProjectEntry(project.ActualDisplayName, Path.GetFullPath(Path.Combine(checkout.RepositoryRoot, relativePath)));
                })
                .ToArray();
            var outside = projects.FirstOrDefault(project => !IsWithin(checkout.RepositoryRoot, project.ProjectPath));
            if (outside is not null)
            {
                return new SolutionInspectionResult(OutcomeKind.ValidationFailure, solutionPath, projects, false,
                    $"Solution project '{outside.ProjectPath}' resolves outside the selected checkout.");
            }

            var missing = projects.FirstOrDefault(project => !File.Exists(project.ProjectPath));
            if (missing is not null)
            {
                return new SolutionInspectionResult(OutcomeKind.ValidationFailure, solutionPath, projects, false,
                    $"Solution project '{missing.ProjectPath}' does not exist.");
            }

            var cliResult = await processRunner.RunAsync(new ProcessRequest
            {
                ExecutablePath = dotnetExecutable,
                Arguments = ["sln", solutionPath, "list"],
                WorkingDirectory = checkout.RepositoryRoot,
                StandardOutputLogPath = Path.Combine(runDirectory, "solution-list.stdout.log"),
                StandardErrorLogPath = Path.Combine(runDirectory, "solution-list.stderr.log"),
                Timeout = TimeSpan.FromMinutes(1)
            }, cancellationToken).ConfigureAwait(false);
            if (cliResult.Outcome != OutcomeKind.Success)
            {
                return new SolutionInspectionResult(cliResult.Outcome, solutionPath, projects, false,
                    $"dotnet sln list failed: {cliResult.Failure}");
            }

            var cliProjects = cliResult.StandardOutputTail
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("Project(s)", StringComparison.OrdinalIgnoreCase) && line.Any(char.IsLetterOrDigit))
                .Where(line => !line.All(character => character is '-' or '='))
                .Select(path => new SolutionProjectEntry(Path.GetFileNameWithoutExtension(path), Path.GetFullPath(Path.Combine(checkout.RepositoryRoot, path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)))))
                .ToArray();
            var apiProjectPaths = projects.Select(project => Path.GetFullPath(project.ProjectPath)).Order(StringComparer.Ordinal).ToArray();
            var cliProjectPaths = cliProjects.Select(project => Path.GetFullPath(project.ProjectPath)).Order(StringComparer.Ordinal).ToArray();
            var matchesCli = apiProjectPaths.SequenceEqual(cliProjectPaths, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            return matchesCli
                ? new SolutionInspectionResult(OutcomeKind.Success, solutionPath, projects, true, null)
                : new SolutionInspectionResult(OutcomeKind.Success, solutionPath, cliProjects, false,
                    $"SolutionPersistence enumerated {projects.Length} projects; dotnet sln list enumerated {cliProjects.Length}. The CLI project list is retained.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            return new SolutionInspectionResult(OutcomeKind.InfrastructureFailure, solutionPath, [], false, exception.Message);
        }
    }

    public async Task<ProjectInspectionResult> InspectProjectAsync(
        CheckoutIdentity checkout,
        string projectPath,
        string configuration,
        string framework,
        string sdkDirectory,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        var fullProjectPath = Path.GetFullPath(Path.Combine(checkout.RepositoryRoot, projectPath));
        if (!IsWithin(checkout.RepositoryRoot, fullProjectPath) || !File.Exists(fullProjectPath))
        {
            return new ProjectInspectionResult(OutcomeKind.ValidationFailure, fullProjectPath, null, null, null, null, false, false, null,
                "The project path must resolve to an existing project inside the selected checkout.");
        }

        ProjectMetadata? apiMetadata = null;
        string? apiFailure = null;
        try
        {
            RegisterSdk(sdkDirectory);
            apiMetadata = EvaluateWithMsBuild(fullProjectPath, configuration, framework);
        }
        catch (Exception exception)
        {
            apiFailure = $"SDK-selected MSBuild API probe failed: {exception.GetType().Name}: {exception.Message}";
        }

        var cliMetadata = await EvaluateWithCliAsync(fullProjectPath, checkout.RepositoryRoot, configuration, framework, runDirectory, cancellationToken).ConfigureAwait(false);
        if (cliMetadata.Outcome != OutcomeKind.Success)
        {
            return cliMetadata with
            {
                UsedMsBuildApi = false,
                Failure = apiFailure is null ? cliMetadata.Failure : $"{apiFailure} {cliMetadata.Failure}".Trim()
            };
        }

        if (apiMetadata is null)
        {
            return cliMetadata with
            {
                ApiMatchesCli = false,
                Failure = apiFailure
            };
        }

        var matches = string.Equals(apiMetadata.Configuration, cliMetadata.Configuration, StringComparison.Ordinal) &&
            string.Equals(apiMetadata.TargetFrameworks, cliMetadata.TargetFrameworks, StringComparison.Ordinal) &&
            string.Equals(apiMetadata.TargetFramework, cliMetadata.TargetFramework, StringComparison.Ordinal) &&
            PathsEqual(apiMetadata.TargetPath, cliMetadata.TargetPath);
        if (!matches)
        {
            return cliMetadata with
            {
                UsedMsBuildApi = false,
                ApiMatchesCli = false,
                Failure = "SDK-selected MSBuild API metadata differed from dotnet msbuild -getProperty; the CLI metadata is retained."
            };
        }

        return new ProjectInspectionResult(
            OutcomeKind.Success,
            fullProjectPath,
            apiMetadata.Configuration,
            apiMetadata.TargetFrameworks,
            apiMetadata.TargetFramework,
            apiMetadata.TargetPath,
            true,
            true,
            apiMetadata.MsBuildVersion,
            null);
    }

    private static void RegisterSdk(string sdkDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sdkDirectory);
        var fullSdkDirectory = Path.GetFullPath(sdkDirectory);
        if (!File.Exists(Path.Combine(fullSdkDirectory, "MSBuild.dll")))
        {
            throw new FileNotFoundException($"Selected SDK MSBuild.dll was not found in '{fullSdkDirectory}'.");
        }

        lock (RegistrationGate)
        {
            if (MSBuildLocator.IsRegistered)
            {
                if (_registeredSdkDirectory is not null && !PathsEqual(_registeredSdkDirectory, fullSdkDirectory))
                {
                    throw new InvalidOperationException($"This process is already registered to MSBuild at '{_registeredSdkDirectory}', not '{fullSdkDirectory}'. Start a fresh DevOps process for the selected SDK.");
                }

                _registeredSdkDirectory ??= fullSdkDirectory;
                return;
            }

            MSBuildLocator.RegisterMSBuildPath(fullSdkDirectory);
            _registeredSdkDirectory = fullSdkDirectory;
        }
    }

    private static ProjectMetadata EvaluateWithMsBuild(string projectPath, string configuration, string framework)
    {
        return EvaluateProjectAfterLocatorRegistration(projectPath, configuration, framework);
    }

    private static ProjectMetadata EvaluateProjectAfterLocatorRegistration(string projectPath, string configuration, string framework)
    {
        using var collection = new Microsoft.Build.Evaluation.ProjectCollection(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = configuration,
            ["TargetFramework"] = framework
        });
        var project = collection.LoadProject(projectPath);
        try
        {
            var metadata = new ProjectMetadata(
                EmptyToNull(project.GetPropertyValue("Configuration")),
                EmptyToNull(project.GetPropertyValue("TargetFrameworks")),
                EmptyToNull(project.GetPropertyValue("TargetFramework")),
                EmptyToNull(project.GetPropertyValue("TargetPath")),
                typeof(Microsoft.Build.Evaluation.Project).Assembly.GetName().Version?.ToString());
            return metadata;
        }
        finally
        {
            collection.UnloadProject(project);
            collection.UnloadAllProjects();
        }
    }

    private async Task<ProjectInspectionResult> EvaluateWithCliAsync(
        string projectPath,
        string workingDirectory,
        string configuration,
        string framework,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        var process = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = dotnetExecutable,
            Arguments = [
                "msbuild", projectPath,
                "-getProperty:Configuration,TargetFrameworks,TargetFramework,TargetPath",
                $"-p:Configuration={configuration}",
                $"-p:TargetFramework={framework}"
            ],
            WorkingDirectory = workingDirectory,
            StandardOutputLogPath = Path.Combine(runDirectory, "msbuild-cli.stdout.log"),
            StandardErrorLogPath = Path.Combine(runDirectory, "msbuild-cli.stderr.log"),
            Timeout = TimeSpan.FromMinutes(2)
        }, cancellationToken).ConfigureAwait(false);

        if (process.Outcome != OutcomeKind.Success)
        {
            return new ProjectInspectionResult(process.Outcome, projectPath, null, null, null, null, false, false, null, process.Failure);
        }

        try
        {
            var json = JsonDocument.Parse(string.Join(Environment.NewLine, process.StandardOutputTail));
            using (json)
            {
                var properties = json.RootElement.TryGetProperty("Properties", out var propertyObject) ? propertyObject : json.RootElement;
                var metadata = new ProjectInspectionResult(
                    OutcomeKind.Success,
                    projectPath,
                    ReadProperty(properties, "Configuration"),
                    ReadProperty(properties, "TargetFrameworks"),
                    ReadProperty(properties, "TargetFramework"),
                    ReadProperty(properties, "TargetPath"),
                    false,
                    false,
                    null,
                    null);
                if (string.IsNullOrWhiteSpace(metadata.Configuration) || string.IsNullOrWhiteSpace(metadata.TargetFramework) || string.IsNullOrWhiteSpace(metadata.TargetPath))
                {
                    return metadata with { Outcome = OutcomeKind.InfrastructureFailure, Failure = "dotnet msbuild -getProperty did not return Configuration, TargetFramework and TargetPath." };
                }

                if (!string.Equals(metadata.Configuration, configuration, StringComparison.Ordinal) ||
                    !string.Equals(metadata.TargetFramework, framework, StringComparison.Ordinal))
                {
                    return metadata with { Outcome = OutcomeKind.InfrastructureFailure, Failure = "dotnet msbuild -getProperty did not honour the requested Configuration and TargetFramework." };
                }

                return metadata;
            }
        }
        catch (JsonException exception)
        {
            return new ProjectInspectionResult(OutcomeKind.InfrastructureFailure, projectPath, null, null, null, null, false, false, null,
                $"Could not parse dotnet msbuild -getProperty output: {exception.Message}");
        }
    }

    private static string? ReadProperty(JsonElement properties, string name)
        => properties.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? EmptyToNull(value.GetString())
            : null;

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool PathsEqual(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        var leftPath = Path.IsPathRooted(left) ? left : Path.GetFullPath(left);
        var rightPath = Path.IsPathRooted(right) ? right : Path.GetFullPath(right);
        return string.Equals(Path.GetFullPath(leftPath), Path.GetFullPath(rightPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool IsWithin(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return Path.GetFullPath(path).StartsWith(normalizedRoot + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private sealed record ProjectMetadata(string? Configuration, string? TargetFrameworks, string? TargetFramework, string? TargetPath, string? MsBuildVersion);
}
