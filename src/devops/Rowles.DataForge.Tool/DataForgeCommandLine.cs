using System.Globalization;
using System.Security.Cryptography;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tool;

public static class DataForgeCommandLine
{
    private static readonly IReadOnlyList<IDataForgeProfile> Profiles = [new LeanCorpusSearchProfile()];

    public static int Run(string[] args, TextWriter output, TextWriter error, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        try
        {
            if (args.Length == 0)
                throw new CommandLineException("A DataForge command is required.");

            var command = args[0].ToLowerInvariant();
            var tail = args[1..];
            return command switch
            {
                "profiles" => RunProfiles(tail, output),
                "generate" => RunGenerate(tail, output, repositoryRoot),
                "inspect" => RunInspect(tail, output),
                "verify" => RunVerify(tail, output),
                "reproduce" => RunReproduce(tail, output),
                "help" or "--help" or "-h" => WriteHelp(tail, output),
                _ => throw new CommandLineException($"Unknown DataForge command '{args[0]}'.")
            };
        }
        catch (CommandLineException exception)
        {
            error.WriteLine($"Invalid command: {exception.Message}");
            error.WriteLine("Run './devops dataforge help' for usage.");
            return 2;
        }
        catch (Exception exception)
        {
            error.WriteLine($"DataForge failed: {exception.Message}");
            return 1;
        }
    }

    private static int RunProfiles(string[] args, TextWriter output)
    {
        if (args.Length != 0)
            throw new CommandLineException("The profiles command does not accept arguments.");

        output.WriteLine("ID | Version | Kind | Default seed | Default count | Description");
        foreach (var profile in Profiles.OrderBy(static item => item.Descriptor.ProfileId, StringComparer.Ordinal))
        {
            var descriptor = profile.Descriptor;
            output.WriteLine(string.Join(" | ",
                descriptor.ProfileId,
                descriptor.ProfileVersion.ToString(CultureInfo.InvariantCulture),
                descriptor.Kind,
                descriptor.DefaultSeed.ToString(CultureInfo.InvariantCulture),
                descriptor.DefaultCount.ToString(CultureInfo.InvariantCulture),
                descriptor.Description));
        }
        return 0;
    }

    private static int RunGenerate(string[] args, TextWriter output, string repositoryRoot)
    {
        var options = ParseFlags(
            args,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Force" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Profile", "Version", "Seed", "Count", "Output" });
        var profileId = Required(options.Values, "Profile");
        var version = ParseInt(Required(options.Values, "Version"), "Version");
        var seed = ParseUInt64(Required(options.Values, "Seed"), "Seed");
        var count = ParseInt(Required(options.Values, "Count"), "Count");
        if (!string.Equals(profileId, "leancorpus-search", StringComparison.Ordinal))
            throw new CommandLineException($"Unknown generated profile '{profileId}'.");

        var profile = new LeanCorpusSearchProfile();
        if (version != profile.Descriptor.ProfileVersion)
            throw new CommandLineException($"Profile '{profileId}' supports version {profile.Descriptor.ProfileVersion}, not {version}.");
        var generationOptions = new DataForgeGenerationOptions(seed, count);
        var outputPath = options.Values.TryGetValue("Output", out var requestedOutput)
            ? ResolveOutputPath(repositoryRoot, requestedOutput)
            : GetDefaultOutputPath(repositoryRoot, profile.Descriptor, generationOptions);
        var result = DataForgeMaterialiser.Materialise(profile, generationOptions, outputPath, options.Flags.Contains("Force"));

        output.WriteLine($"Profile: {profileId}");
        output.WriteLine($"Version: {version.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"Seed: {seed.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"Count: {count.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"ContentSha256: {result.Manifest.ContentSha256}");
        output.WriteLine($"ArtefactSha256: {result.Manifest.ArtefactSha256}");
        output.WriteLine($"Output: {result.OutputDirectory}");
        return 0;
    }

    private static int RunInspect(string[] args, TextWriter output)
    {
        if (args.Length != 1)
            throw new CommandLineException("Usage: inspect <dataset-dir-or-manifest>");
        var manifest = DataForgeManifestCodec.Read(DataForgeVerifier.ResolveManifestPath(args[0]));
        output.WriteLine($"SourceKind: {manifest.SourceKind}");
        output.WriteLine($"DataForgeVersion: {manifest.DataForgeVersion.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"Profile: {manifest.ProfileId ?? "(none)"}");
        output.WriteLine($"Version: {manifest.ProfileVersion?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}");
        output.WriteLine($"Seed: {manifest.Seed?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}");
        output.WriteLine($"Count: {manifest.RecordCount.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"LogicalByteCount: {manifest.LogicalByteCount.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"ContentSha256: {manifest.ContentSha256}");
        output.WriteLine($"ArtefactSha256: {manifest.ArtefactSha256}");
        foreach (var parameter in manifest.Parameters.OrderBy(static item => item.Name, StringComparer.Ordinal))
            output.WriteLine($"Parameter.{parameter.Name}: {parameter.Value}");
        foreach (var dependency in manifest.Dependencies.OrderBy(static item => item.Name, StringComparer.Ordinal))
            output.WriteLine($"Dependency.{dependency.Name}: {dependency.Version}");
        return 0;
    }

    private static int RunVerify(string[] args, TextWriter output)
    {
        if (args.Length != 1)
            throw new CommandLineException("Usage: verify <dataset-dir-or-manifest>");
        var result = DataForgeVerifier.VerifyMaterialised(args[0]);
        output.WriteLine("Verified: true");
        output.WriteLine($"Profile: {result.Manifest.ProfileId ?? "(none)"}");
        output.WriteLine($"Count: {result.Manifest.RecordCount.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"ContentSha256: {result.Manifest.ContentSha256}");
        return 0;
    }

    private static int RunReproduce(string[] args, TextWriter output)
    {
        if (args.Length != 1)
            throw new CommandLineException("Usage: reproduce <dataset-dir-or-manifest>");
        var manifestPath = DataForgeVerifier.ResolveManifestPath(args[0]);
        var manifest = DataForgeManifestCodec.Read(manifestPath);
        if (manifest.ProfileId is null || manifest.ProfileVersion is null)
            throw new InvalidDataException("Reproduction requires a generated profile identity.");
        if (!string.Equals(manifest.ProfileId, "leancorpus-search", StringComparison.Ordinal))
            throw new InvalidDataException($"No generated profile is registered for '{manifest.ProfileId}'.");
        var result = DataForgeVerifier.Reproduce(manifestPath, new LeanCorpusSearchProfile());
        output.WriteLine("Reproduced: true");
        output.WriteLine($"Profile: {result.Manifest.ProfileId}");
        output.WriteLine($"Version: {result.Manifest.ProfileVersion?.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"Seed: {result.Manifest.Seed?.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"Count: {result.Manifest.RecordCount.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"ContentSha256: {result.Manifest.ContentSha256}");
        return 0;
    }

    private static int WriteHelp(string[] args, TextWriter output)
    {
        if (args.Length != 0)
            throw new CommandLineException("Help does not accept arguments.");
        output.WriteLine("Usage:");
        output.WriteLine("  ./devops dataforge profiles");
        output.WriteLine("  ./devops dataforge generate -Profile leancorpus-search -Version 1 -Seed 42 -Count 20000 [-Output <path>] [-Force]");
        output.WriteLine("  ./devops dataforge inspect <dataset-dir-or-manifest>");
        output.WriteLine("  ./devops dataforge verify <dataset-dir-or-manifest>");
        output.WriteLine("  ./devops dataforge reproduce <dataset-dir-or-manifest>");
        return 0;
    }

    private static ParsedFlags ParseFlags(string[] args, IReadOnlySet<string> allowedFlags, IReadOnlySet<string> allowedValues)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("-", StringComparison.Ordinal))
                throw new CommandLineException($"Unexpected positional argument '{argument}'.");
            var name = argument.TrimStart('-');
            if (allowedFlags.Contains(name))
            {
                if (!flags.Add(name))
                    throw new CommandLineException($"Flag '-{name}' was supplied more than once.");
                continue;
            }
            if (!allowedValues.Contains(name))
                throw new CommandLineException($"Unknown option '{argument}'.");
            if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                throw new CommandLineException($"Option '-{name}' requires a value.");
            if (!values.TryAdd(name, args[++index]))
                throw new CommandLineException($"Option '-{name}' was supplied more than once.");
        }
        return new ParsedFlags(flags, values);
    }

    private static string GetDefaultOutputPath(string repositoryRoot, DataForgeProfileDescriptor profile, DataForgeGenerationOptions options)
    {
        var parameterBytes = new MemoryStream();
        foreach (var pair in options.Parameters.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            parameterBytes.Write(DataForgeSeedDerivation.GetStrictUtf8Bytes(pair.Key));
            parameterBytes.WriteByte(0);
            parameterBytes.Write(DataForgeSeedDerivation.GetStrictUtf8Bytes(pair.Value));
            parameterBytes.WriteByte(0);
        }
        var parameterHash = Convert.ToHexString(SHA256.HashData(parameterBytes.GetBuffer().AsSpan(0, checked((int)parameterBytes.Length))).AsSpan(0, 6)).ToLowerInvariant();
        var directoryName = string.Concat(
            profile.ProfileId,
            "-v", profile.ProfileVersion.ToString(CultureInfo.InvariantCulture),
            "-seed", options.Seed.ToString(CultureInfo.InvariantCulture),
            "-count", options.RecordCount.ToString(CultureInfo.InvariantCulture),
            "-", parameterHash);
        return Path.Combine(Path.GetFullPath(repositoryRoot), "artifacts", "dataforge", "generated", directoryName);
    }

    private static string ResolveOutputPath(string repositoryRoot, string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        var segments = output.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(static segment => string.Equals(segment, "..", StringComparison.Ordinal)))
            throw new CommandLineException("Output paths must not contain parent-directory traversal segments.");
        return Path.GetFullPath(Path.IsPathRooted(output) ? output : Path.Combine(Path.GetFullPath(repositoryRoot), output));
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) ? value : throw new CommandLineException($"Option '-{name}' is required.");

    private static int ParseInt(string value, string name) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new CommandLineException($"Option '-{name}' must be an invariant integer.");

    private static ulong ParseUInt64(string value, string name) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new CommandLineException($"Option '-{name}' must be an unsigned invariant integer.");

    private sealed record ParsedFlags(HashSet<string> Flags, Dictionary<string, string> Values);

    private sealed class CommandLineException(string message) : Exception(message);
}
