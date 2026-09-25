using System.Globalization;
using System.Security.Cryptography;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tool;

public static class DataForgeCommandLine
{
    private static readonly IReadOnlyList<IDataForgeProfile> Profiles =
        [new LeanCorpusSearchProfile(), new LeanCorpusVectorProfile(), new LeanCorpusHybridProfile(),
            new LeanCorpusStressProfile(), new RowlesTextMultilingualProfile(), new LeanCorpusE2eProfile()];

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
                "reference" => RunReference(tail, output, repositoryRoot),
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
        catch (OperationCanceledException)
        {
            error.WriteLine("DataForge operation cancelled; resumable partial files are retained.");
            return 130;
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
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Force", "AllowLarge" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Profile", "Version", "Seed", "Count", "Output", "Mode", "Language", "Dimension", "VectorDistribution", "ClusterCount", "QueryCount",
                    "VeryLongShareBasisPoints", "LongShareBasisPoints", "CategoryCardinality", "RegionCardinality", "RareAnchorBasisPoints", "NarrowFilterBasisPoints" });
        var profileId = Required(options.Values, "Profile");
        var profile = Profiles.FirstOrDefault(item => string.Equals(item.Descriptor.ProfileId, profileId, StringComparison.Ordinal))
            ?? throw new CommandLineException($"Unknown generated profile '{profileId}'.");
        var stress = profile is LeanCorpusStressProfile;
        var multilingual = profile is RowlesTextMultilingualProfile;
        var optionalDefaults = stress || multilingual || profile is LeanCorpusE2eProfile;
        var mode = stress ? Required(options.Values, "Mode") : null;
        var defaultCount = mode switch { "search" => 100_000, "vector" or "hybrid" => 50_000, _ => profile.Descriptor.DefaultCount };
        var version = ParseInt(optionalDefaults ? options.Values.GetValueOrDefault("Version") ?? "1" : Required(options.Values, "Version"), "Version");
        var seed = ParseUInt64(optionalDefaults ? options.Values.GetValueOrDefault("Seed") ?? "42" : Required(options.Values, "Seed"), "Seed");
        var count = ParseInt(optionalDefaults ? options.Values.GetValueOrDefault("Count") ?? defaultCount.ToString(CultureInfo.InvariantCulture)
            : Required(options.Values, "Count"), "Count");
        if (version != profile.Descriptor.ProfileVersion)
            throw new CommandLineException($"Profile '{profileId}' supports version {profile.Descriptor.ProfileVersion}, not {version}.");
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (mode is not null)
            parameters.Add("mode", mode);
        if (multilingual)
            parameters.Add("language", Required(options.Values, "Language"));
        foreach (var (flag, parameter) in new[]
                 {
                     ("Dimension", "dimension"),
                     ("VectorDistribution", "vectorDistribution"),
                     ("ClusterCount", "clusterCount"),
                     ("QueryCount", "queryCount"),
                     ("VeryLongShareBasisPoints", "veryLongShareBasisPoints"),
                     ("LongShareBasisPoints", "longShareBasisPoints"),
                     ("CategoryCardinality", "categoryCardinality"),
                     ("RegionCardinality", "regionCardinality"),
                     ("RareAnchorBasisPoints", "rareAnchorBasisPoints"),
                     ("NarrowFilterBasisPoints", "narrowFilterBasisPoints")
                 })
        {
            if (options.Values.TryGetValue(flag, out var value))
                parameters.Add(parameter, value);
        }
        if (options.Flags.Contains("AllowLarge") && !stress)
            throw new CommandLineException("-AllowLarge is available only for the stress profile.");
        if (!stress && options.Values.ContainsKey("Mode"))
            throw new CommandLineException("-Mode is available only for the stress profile.");
        if (!multilingual && options.Values.ContainsKey("Language"))
            throw new CommandLineException("-Language is available only for the multilingual profile.");
        if (parameters.Count != 0 && profile is not (LeanCorpusVectorProfile or LeanCorpusHybridProfile or LeanCorpusStressProfile or RowlesTextMultilingualProfile))
            throw new CommandLineException("Profile parameters are not accepted by this profile.");
        if (multilingual && parameters.Keys.Any(static key => key != "language"))
            throw new CommandLineException("The multilingual profile accepts only the language parameter.");
        if (profile is LeanCorpusHybridProfile && parameters.Keys.Any(static key => key != "dimension"))
            throw new CommandLineException("The hybrid profile accepts only the dimension parameter.");
        if (profile is LeanCorpusVectorProfile && parameters.Keys.Any(static key => key is not ("dimension" or "vectorDistribution" or "clusterCount" or "queryCount")))
            throw new CommandLineException("The vector profile accepts only vector parameters.");
        if (stress && mode is not ("search" or "vector" or "hybrid"))
            throw new CommandLineException($"Stress generation failed for {ReplayIdentity(profileId, version, seed, count, parameters)}: mode must be search, vector or hybrid.");
        if (stress && !options.Flags.Contains("AllowLarge"))
        {
            if (mode == "search" && count > 1_000_000)
                throw new CommandLineException($"Stress generation requires -AllowLarge for {ReplayIdentity(profileId, version, seed, count, parameters)}: search count exceeds 1,000,000.");
            if (mode == "vector")
            {
                var dimension = parameters.TryGetValue("dimension", out var dimensionText)
                    ? ParseInt(dimensionText, "Dimension") : 128;
                if ((long)count * dimension * sizeof(float) > 16L * 1024 * 1024 * 1024)
                    throw new CommandLineException($"Stress generation requires -AllowLarge for {ReplayIdentity(profileId, version, seed, count, parameters)}: raw vector payload exceeds 16 GiB.");
            }
        }
        DataForgeGenerationOptions generationOptions;
        try
        {
            generationOptions = new DataForgeGenerationOptions(seed, count, parameters);
        }
        catch (ArgumentException exception) when (stress)
        {
            throw new CommandLineException($"Invalid stress identity {ReplayIdentity(profileId, version, seed, count, parameters)}: {exception.Message}");
        }
        var outputPath = options.Values.TryGetValue("Output", out var requestedOutput)
            ? ResolveOutputPath(repositoryRoot, requestedOutput)
            : GetDefaultOutputPath(repositoryRoot, profile.Descriptor, generationOptions);
        DataForgeMaterialisationResult result;
        try
        {
            result = profile switch
            {
                LeanCorpusSearchProfile search => DataForgeMaterialiser.Materialise(search, generationOptions, outputPath, options.Flags.Contains("Force")),
                LeanCorpusVectorProfile vector => DataForgeMaterialiser.Materialise(vector, generationOptions, outputPath, options.Flags.Contains("Force")),
                LeanCorpusHybridProfile hybrid => DataForgeMaterialiser.Materialise(hybrid, generationOptions, outputPath, options.Flags.Contains("Force")),
                LeanCorpusStressProfile stressProfile => DataForgeMaterialiser.Materialise(stressProfile, generationOptions, outputPath, options.Flags.Contains("Force")),
                RowlesTextMultilingualProfile text => DataForgeMaterialiser.Materialise(text, generationOptions, outputPath, options.Flags.Contains("Force")),
                LeanCorpusE2eProfile e2e => DataForgeMaterialiser.Materialise(e2e, generationOptions, outputPath, options.Flags.Contains("Force")),
                _ => throw new CommandLineException($"Unknown generated profile '{profileId}'.")
            };
        }
        catch (Exception exception) when (stress && exception is not CommandLineException)
        {
            throw new CommandLineException($"Stress generation failed for {ReplayIdentity(profileId, version, seed, count, parameters)}: {exception.Message}");
        }

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
        if (manifest.Source is not null)
            foreach (var source in manifest.Source.OrderBy(static item => item.Key, StringComparer.Ordinal))
                output.WriteLine($"Source.{source.Key}: {source.Value}");
        foreach (var summary in manifest.Summaries)
            output.WriteLine($"Summary.{summary.Name}: {summary.Value}");
        return 0;
    }

    private static int RunVerify(string[] args, TextWriter output)
    {
        if (args.Length != 1)
            throw new CommandLineException("Usage: verify <dataset-dir-or-manifest>");
        var manifestPath = DataForgeVerifier.ResolveManifestPath(args[0]);
        var manifest = DataForgeManifestCodec.Read(manifestPath);
        if (manifest.SourceKind == DataForgeSourceKind.Imported && manifest.Source?.GetValueOrDefault("datasetId") == WikipediaReferenceContract.DatasetId)
        {
            var wikipedia = WikipediaReferenceVerifier.Verify(manifestPath);
            output.WriteLine("Verified: true");
            output.WriteLine($"DatasetId: {wikipedia.DatasetId}");
            output.WriteLine($"DatasetVersion: {wikipedia.DatasetVersion.ToString(CultureInfo.InvariantCulture)}");
            output.WriteLine($"Count: {wikipedia.RecordCount.ToString(CultureInfo.InvariantCulture)}");
            output.WriteLine($"ContentSha256: {wikipedia.ContentSha256}");
            return 0;
        }
        var result = DataForgeVerifier.VerifyMaterialised(manifestPath);
        output.WriteLine("Verified: true");
        output.WriteLine($"Profile: {result.Manifest.ProfileId ?? "(none)"}");
        output.WriteLine($"Count: {result.Manifest.RecordCount.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"ContentSha256: {result.Manifest.ContentSha256}");
        return 0;
    }

    private static int RunReference(string[] args, TextWriter output, string repositoryRoot)
    {
        if (args.Length == 0)
            throw new CommandLineException("Usage: reference <download|build|inspect|verify>");
        var operation = args[0].ToLowerInvariant();
        var tail = args[1..];
        switch (operation)
        {
            case "download":
            {
                var options = ParseFlags(tail,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Force" },
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Cache" });
                var cache = options.Values.TryGetValue("Cache", out var requestedCache)
                    ? Path.GetFullPath(requestedCache)
                    : WikipediaReferenceContract.CachePath(repositoryRoot);
                using var cancellation = new CancellationTokenSource();
                ConsoleCancelEventHandler handler = (_, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    cancellation.Cancel();
                };
                Console.CancelKeyPress += handler;
                try
                {
                    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
                    var source = new WikipediaReferenceDownloader(client).DownloadAsync(cache, options.Flags.Contains("Force"), cancellation.Token).GetAwaiter().GetResult();
                    output.WriteLine($"Wiki: {source.Wiki}");
                    output.WriteLine($"DumpDate: {source.DumpDate}");
                    output.WriteLine($"PrimarySha256: {source.PrimarySha256}");
                    output.WriteLine($"IndexSha256: {source.IndexSha256}");
                    output.WriteLine($"Cache: {cache}");
                    return 0;
                }
                finally
                {
                    Console.CancelKeyPress -= handler;
                }
            }
            case "build":
            {
                var options = ParseFlags(tail, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Cache", "Output" });
                var cache = options.Values.TryGetValue("Cache", out var requestedCache)
                    ? Path.GetFullPath(requestedCache)
                    : WikipediaReferenceContract.CachePath(repositoryRoot);
                var destination = options.Values.TryGetValue("Output", out var requestedOutput)
                    ? ResolveOutputPath(repositoryRoot, requestedOutput)
                    : WikipediaReferenceContract.ReferencePath(repositoryRoot);
                var result = WikipediaReferenceBuilder.Build(cache, destination);
                output.WriteLine($"DatasetId: {WikipediaReferenceContract.DatasetId}");
                output.WriteLine($"DatasetVersion: {WikipediaReferenceContract.DatasetVersion.ToString(CultureInfo.InvariantCulture)}");
                output.WriteLine($"Count: {result.SelectedCount.ToString(CultureInfo.InvariantCulture)}");
                output.WriteLine($"CandidateLimit: {result.CandidateLimit.ToString(CultureInfo.InvariantCulture)}");
                output.WriteLine($"ContentSha256: {result.Manifest.ContentSha256}");
                output.WriteLine($"ArtefactSha256: {result.Manifest.ArtefactSha256}");
                output.WriteLine($"Output: {result.OutputDirectory}");
                return 0;
            }
            case "inspect":
            case "verify":
            {
                var options = ParseFlags(tail, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ReferencePath" });
                var reference = options.Values.TryGetValue("ReferencePath", out var requestedPath)
                    ? Path.GetFullPath(requestedPath)
                    : WikipediaReferenceContract.ReferencePath(repositoryRoot);
                var result = WikipediaReferenceVerifier.Verify(reference);
                if (operation == "verify")
                    output.WriteLine("Verified: true");
                output.WriteLine($"DatasetId: {result.DatasetId}");
                output.WriteLine($"DatasetVersion: {result.DatasetVersion.ToString(CultureInfo.InvariantCulture)}");
                output.WriteLine($"DumpDate: {result.DumpDate}");
                output.WriteLine($"Count: {result.RecordCount.ToString(CultureInfo.InvariantCulture)}");
                output.WriteLine($"CandidateLimit: {result.CandidateLimit.ToString(CultureInfo.InvariantCulture)}");
                output.WriteLine($"ContentSha256: {result.ContentSha256}");
                output.WriteLine($"ArtefactSha256: {result.ArtefactSha256}");
                if (operation == "inspect")
                {
                    var manifest = DataForgeManifestCodec.Read(DataForgeVerifier.ResolveManifestPath(reference));
                    foreach (var summary in manifest.Summaries)
                        output.WriteLine($"Summary.{summary.Name}: {summary.Value}");
                    output.WriteLine("Licence: CC BY-SA 4.0");
                }
                return 0;
            }
            default:
                throw new CommandLineException($"Unknown reference command '{args[0]}'.");
        }
    }

    private static int RunReproduce(string[] args, TextWriter output)
    {
        if (args.Length != 1)
            throw new CommandLineException("Usage: reproduce <dataset-dir-or-manifest>");
        var manifestPath = DataForgeVerifier.ResolveManifestPath(args[0]);
        var manifest = DataForgeManifestCodec.Read(manifestPath);
        if (manifest.ProfileId is null || manifest.ProfileVersion is null)
            throw new InvalidDataException("Reproduction requires a generated profile identity.");
        var result = manifest.ProfileId switch
        {
            "leancorpus-search" => DataForgeVerifier.Reproduce(manifestPath, new LeanCorpusSearchProfile()),
            "leancorpus-vector" => DataForgeVerifier.Reproduce(manifestPath, new LeanCorpusVectorProfile()),
            "leancorpus-hybrid" => DataForgeVerifier.Reproduce(manifestPath, new LeanCorpusHybridProfile()),
            "leancorpus-stress" => DataForgeVerifier.Reproduce(manifestPath, new LeanCorpusStressProfile()),
            "rowles-text-multilingual" => DataForgeVerifier.Reproduce(manifestPath, new RowlesTextMultilingualProfile()),
            "leancorpus-e2e" => DataForgeVerifier.Reproduce(manifestPath, new LeanCorpusE2eProfile()),
            _ => throw new InvalidDataException($"No generated profile is registered for '{manifest.ProfileId}'.")
        };
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
        output.WriteLine("  ./devops dataforge generate -Profile leancorpus-vector -Version 1 -Seed 42 -Count 1000 [-Dimension 64] [-VectorDistribution Uniform] [-ClusterCount 8] [-QueryCount 1] [-Output <path>] [-Force]");
        output.WriteLine("  ./devops dataforge generate -Profile leancorpus-hybrid -Version 1 -Seed 42 -Count 20000 [-Dimension 64] [-Output <path>] [-Force]");
        output.WriteLine("  ./devops dataforge generate -Profile leancorpus-stress -Mode search|vector|hybrid [-Version 1] [-Seed 42] [-Count <count>] [-AllowLarge] [-Output <path>] [-Force]");
        output.WriteLine("  ./devops dataforge generate -Profile rowles-text-multilingual -Language en|fr|de|es|it|pt|nl|ru|ar|zh|ja|ko [-Version 1] [-Seed 42] [-Count 256] [-Output <path>] [-Force]");
        output.WriteLine("  ./devops dataforge generate -Profile leancorpus-e2e [-Version 1] [-Seed 42] [-Count 256] [-Output <path>] [-Force]");
        output.WriteLine("  ./devops dataforge reference download [-Cache <path>] [-Force]");
        output.WriteLine("  ./devops dataforge reference build [-Cache <path>] [-Output <path>]");
        output.WriteLine("  ./devops dataforge reference inspect [-ReferencePath <path>]");
        output.WriteLine("  ./devops dataforge reference verify [-ReferencePath <path>]");
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

    private static string ReplayIdentity(string profileId, int version, ulong seed, int count, IReadOnlyDictionary<string, string> parameters) =>
        $"{profileId}/v{version} seed={seed} count={count} parameters=[{string.Join(",", parameters.OrderBy(static item => item.Key, StringComparer.Ordinal).Select(static item => $"{item.Key}={item.Value}"))}]";

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
