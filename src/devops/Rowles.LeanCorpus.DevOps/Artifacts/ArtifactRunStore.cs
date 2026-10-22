using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rowles.LeanCorpus.DevOps.Artifacts;

internal interface IAtomicFileWriter
{
    Task WriteReplaceAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}

internal sealed class AtomicFileWriter : IAtomicFileWriter
{
    public async Task WriteReplaceAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A manifest path must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

internal sealed class ArtifactRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly ArtifactPaths _paths;
    private readonly IAtomicFileWriter _writer;
    private readonly string _runCategory;

    public ArtifactRunStore(ArtifactPaths paths, IAtomicFileWriter? writer = null, string runCategory = "build")
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _writer = writer ?? new AtomicFileWriter();
        _runCategory = runCategory;
        _ = _paths.GetRunRoot(_runCategory);
    }

    public async Task<RunManifest> StartAsync(
        string command,
        IReadOnlyDictionary<string, string>? inputs = null,
        Guid? parentRunId = null,
        string? sourceCommit = null,
        bool sourceDirty = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var runId = Guid.NewGuid();
        var runDirectory = _paths.CreateRunDirectory(_runCategory, runId);
        var manifest = new RunManifest
        {
            SchemaVersion = RunManifest.CurrentSchemaVersion,
            RunId = runId,
            ParentRunId = parentRunId,
            Command = command,
            State = RunState.Running,
            StartedUtc = DateTimeOffset.UtcNow,
            SourceCommit = sourceCommit,
            SourceDirty = sourceDirty,
            Platform = $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
            Inputs = (inputs ?? ImmutableDictionary<string, string>.Empty).ToImmutableDictionary(StringComparer.Ordinal),
            ChildExitCodes = [],
            Artifacts = [],
            OwnerProcessId = Environment.ProcessId,
            OwnerProcessStartedUtc = GetCurrentProcessStartedUtc()
        };

        await WriteAsync(Path.Combine(runDirectory, "run.json"), manifest, cancellationToken).ConfigureAwait(false);
        return manifest;
    }

    public async Task<RunManifest> FinishAsync(
        RunManifest manifest,
        RunState terminalState,
        int? childExitCode = null,
        IEnumerable<string>? artifacts = null,
        string? failure = null,
        CancellationToken cancellationToken = default,
        IEnumerable<int>? childExitCodes = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.State != RunState.Running)
        {
            throw new InvalidOperationException($"Run '{manifest.RunId}' is '{manifest.State}' and cannot be finalised again.");
        }

        if (!IsTerminal(terminalState))
        {
            throw new ArgumentOutOfRangeException(nameof(terminalState), terminalState, "A terminal run state is required.");
        }

        var finished = manifest with
        {
            State = terminalState,
            CompletedUtc = DateTimeOffset.UtcNow,
            ChildExitCodes = manifest.ChildExitCodes
                .Concat(childExitCode is null ? [] : [childExitCode.Value])
                .Concat(childExitCodes ?? [])
                .ToArray(),
            Artifacts = artifacts is null ? manifest.Artifacts : manifest.Artifacts.Concat(artifacts).Distinct(StringComparer.Ordinal).ToArray(),
            Failure = failure
        };
        await WriteAsync(GetManifestPath(manifest.RunId), finished, cancellationToken).ConfigureAwait(false);
        return finished;
    }

    public async Task<RunManifest> UpdateRunningAsync(
        RunManifest manifest,
        string? sourceCommit,
        string? sourceBranch,
        bool sourceDirty,
        IReadOnlyDictionary<string, string>? toolVersions = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? additionalInputs = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.State != RunState.Running)
        {
            throw new InvalidOperationException($"Run '{manifest.RunId}' is '{manifest.State}' and cannot be updated.");
        }

        var tools = new Dictionary<string, string>(manifest.ToolVersions, StringComparer.OrdinalIgnoreCase);
        if (toolVersions is not null)
        {
            foreach (var (name, version) in toolVersions)
            {
                tools[name] = version;
            }
        }

        var inputs = manifest.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (additionalInputs is not null)
        {
            foreach (var (name, value) in additionalInputs)
            {
                inputs[name] = value;
            }
        }

        var updated = manifest with
        {
            SourceCommit = sourceCommit,
            SourceBranch = sourceBranch,
            SourceDirty = sourceDirty,
            ToolVersions = tools,
            Inputs = inputs
        };
        await WriteAsync(GetManifestPath(manifest.RunId), updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<RunManifest?> ReadAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var path = GetManifestPath(runId);
        if (!File.Exists(path))
        {
            var historicalPath = Path.Combine(Path.GetDirectoryName(path)!, "manifest.json");
            if (!File.Exists(historicalPath))
            {
                return null;
            }
            path = historicalPath;
        }

        RunManifest manifest;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16_384, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            manifest = await JsonSerializer.DeserializeAsync<RunManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Run manifest '{path}' is empty.");
        }

        ValidateManifest(manifest, runId, path);
        if (manifest.State is RunState.Created or RunState.Running && !IsOwnerAlive(manifest))
        {
            manifest = manifest with
            {
                State = RunState.Interrupted,
                CompletedUtc = DateTimeOffset.UtcNow,
                Failure = "The run owner process ended before writing a terminal manifest."
            };
            await WriteAsync(GetManifestPath(runId), manifest, cancellationToken).ConfigureAwait(false);
        }

        return manifest;
    }

    public async Task<IReadOnlyList<RunManifest>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        var runRoot = _paths.GetRunRoot(_runCategory);
        if (!Directory.Exists(runRoot))
        {
            return [];
        }

        var manifests = new List<RunManifest>();
        foreach (var directory in Directory.EnumerateDirectories(runRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(Path.GetFileName(directory), out var runId))
            {
                continue;
            }

            var manifest = await ReadAsync(runId, cancellationToken).ConfigureAwait(false);
            if (manifest is not null)
            {
                manifests.Add(manifest);
            }
        }

        return manifests.OrderBy(manifest => manifest.StartedUtc).ToArray();
    }

    private string GetManifestPath(Guid runId)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty run identity is required.", nameof(runId));
        }

        return Path.Combine(_paths.GetRunRoot(_runCategory), runId.ToString("D"), "run.json");
    }

    private async Task WriteAsync(string path, RunManifest manifest, CancellationToken cancellationToken)
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        await _writer.WriteReplaceAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateManifest(RunManifest manifest, Guid expectedId, string path)
    {
        if (manifest.SchemaVersion != RunManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Run manifest '{path}' uses unsupported schema version {manifest.SchemaVersion}.");
        }

        if (manifest.RunId != expectedId || string.IsNullOrWhiteSpace(manifest.Command) || !Enum.IsDefined(manifest.State))
        {
            throw new InvalidDataException($"Run manifest '{path}' has invalid identity, command or state fields.");
        }
    }

    private static bool IsTerminal(RunState state)
        => state is RunState.Succeeded or RunState.Failed or RunState.Cancelled or RunState.TimedOut or RunState.Interrupted;

    private static bool IsOwnerAlive(RunManifest manifest)
    {
        if (manifest.OwnerProcessId is not { } processId || manifest.OwnerProcessStartedUtc is not { } expectedStart)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return Math.Abs((process.StartTime.ToUniversalTime() - expectedStart.UtcDateTime).TotalSeconds) < 2;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static DateTimeOffset GetCurrentProcessStartedUtc()
    {
        using var process = Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
