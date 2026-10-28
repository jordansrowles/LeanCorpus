using System.CommandLine;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Rowles.LeanCorpus.Cli.TestLab;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Cli;

internal static partial class WorkbenchCommands
{
    private static Command BuildTestLabCommands()
    {
        var command = new Command("testlab", "Build deterministic corruption and portable index fixtures.");
        var source = new Argument<string>("index-path") { Description = "Source index directory. It is copied before mutation." };
        var output = new Argument<string>("output-path") { Description = "New destination for the mutated fixture." };
        var component = new Option<string>("--component") { Description = "postings, stored-fields, terms, vectors, hnsw, live-docs or any.", DefaultValueFactory = _ => "postings" };
        var mode = new Option<string>("--mode") { Description = "truncate or flip-byte (default truncate).", DefaultValueFactory = _ => "truncate" };
        var seed = new Option<ulong>("--seed") { Description = "Deterministic target/offset seed (default 123).", DefaultValueFactory = _ => 123UL };
        var segment = new Option<string?>("--segment") { Description = "Restrict the target to one segment ID." };
        var corrupt = new Command("corrupt", "Create one deterministic corrupted copy and mutation manifest.");
        corrupt.Add(source);
        corrupt.Add(output);
        corrupt.Add(component);
        corrupt.Add(mode);
        corrupt.Add(seed);
        corrupt.Add(segment);
        corrupt.SetAction(result => RunCorrupt(GetRequiredValue(result, source), GetRequiredValue(result, output), result.GetValue(component) ?? "postings", result.GetValue(mode) ?? "truncate", result.GetValue(seed), result.GetValue(segment), result.InvocationConfiguration.Output));
        command.Add(corrupt);

        var mutationSource = new Argument<string>("index-path");
        var mutationOutput = new Argument<string>("output-path");
        var mutationMode = new Option<string>("--mode") { Description = "add, delete, update, many-commits, many-small-segments or merge-heavy.", DefaultValueFactory = _ => "add" };
        var mutationSeed = new Option<ulong>("--seed") { Description = "Deterministic mutation seed (default 123).", DefaultValueFactory = _ => 123UL };
        var mutationCount = new Option<int>("--count") { Description = "Number of generated, deleted or replaced documents (default 16).", DefaultValueFactory = _ => 16 };
        var mutate = new Command("mutate", "Create a deterministic stress mutation in a copied index.");
        mutate.Add(mutationSource);
        mutate.Add(mutationOutput);
        mutate.Add(mutationMode);
        mutate.Add(mutationSeed);
        mutate.Add(mutationCount);
        mutate.SetAction(result => RunMutate(GetRequiredValue(result, mutationSource), GetRequiredValue(result, mutationOutput),
            result.GetValue(mutationMode) ?? "add", result.GetValue(mutationSeed), result.GetValue(mutationCount), result.InvocationConfiguration.Output));
        command.Add(mutate);

        var fixture = new Command("fixture", "Package and unpack portable .lcf fixtures.");
        var packSource = new Argument<string>("index-path");
        var packagePath = new Argument<string>("package-path") { Description = "Output .lcf package path." };
        var pack = new Command("pack", "Package an index directory as a portable .lcf archive.");
        pack.Add(packSource);
        pack.Add(packagePath);
        pack.SetAction(result => RunFixturePack(GetRequiredValue(result, packSource), GetRequiredValue(result, packagePath), result.InvocationConfiguration.Output));
        fixture.Add(pack);

        var unpackSource = new Argument<string>("package-path");
        var unpackPath = new Argument<string>("output-directory");
        var unpack = new Command("unpack", "Safely unpack a portable .lcf archive into a new directory.");
        unpack.Add(unpackSource);
        unpack.Add(unpackPath);
        unpack.SetAction(result => RunFixtureUnpack(GetRequiredValue(result, unpackSource), GetRequiredValue(result, unpackPath), result.InvocationConfiguration.Output));
        fixture.Add(unpack);
        command.Add(fixture);

        var from = new Option<ulong>("--from") { Description = "First seed in the inclusive search range.", DefaultValueFactory = _ => 1UL };
        var to = new Option<ulong>("--to") { Description = "Last seed in the inclusive search range.", DefaultValueFactory = _ => 100UL };
        var find = new Option<string>("--find") { Description = "zero-results, segment-count, validation-failure or size-outlier.", DefaultValueFactory = _ => "zero-results" };
        var preset = new Option<string>("--preset") { Description = "Registered deterministic preset (default search-small).", DefaultValueFactory = _ => "search-small" };
        var query = new Option<string>("--query") { Description = "Query used by zero-results search (default quasarneedle).", DefaultValueFactory = _ => "quasarneedle" };
        var field = new Option<string>("--field") { Description = "Query field (default body).", DefaultValueFactory = _ => "body" };
        var count = new Option<int>("--count") { Description = "Documents per generated seed (default 64).", DefaultValueFactory = _ => 64 };
        var threshold = new Option<double>("--threshold") { Description = "Minimum segment count, or size-outlier multiplier (default 8 or 2).", DefaultValueFactory = _ => 8d };
        var json = Flag("--json", "Write matching seeds as JSON.");
        var seedSearch = new Command("seed-search", "Explore a bounded seed range for deterministic fixture properties.");
        seedSearch.Add(from);
        seedSearch.Add(to);
        seedSearch.Add(find);
        seedSearch.Add(preset);
        seedSearch.Add(query);
        seedSearch.Add(field);
        seedSearch.Add(count);
        seedSearch.Add(threshold);
        seedSearch.Add(json);
        seedSearch.SetAction(result => RunSeedSearch(result.GetValue(from), result.GetValue(to), result.GetValue(find) ?? "zero-results",
            result.GetValue(preset) ?? "search-small", result.GetValue(query) ?? "quasarneedle", result.GetValue(field) ?? "body",
            result.GetValue(count), result.GetValue(threshold), result.GetValue(json), result.InvocationConfiguration.Output));
        command.Add(seedSearch);
        return command;
    }

    private static int RunSeedSearch(ulong firstSeed, ulong lastSeed, string predicate, string preset, string query,
        string field, int count, double threshold, bool json, TextWriter output)
    {
        SeedSearchResult result = SeedSearchRunner.Search(firstSeed, lastSeed, predicate, preset, query, field, count, threshold);
        if (json)
            WriteJson(output, result);
        else
        {
            output.WriteLine($"Searched seeds {result.FirstSeed}..{result.LastSeed} for {result.Predicate} using {result.Preset} ({result.DocumentCount} documents per seed).");
            foreach (SeedSearchMatch match in result.Matches)
                output.WriteLine($"seed={match.Seed}\t{match.Observation}");
            if (result.Matches.Count == 0)
                output.WriteLine("No matching seeds were found.");
        }
        return CliExitCodes.Success;
    }

    private static int RunMutate(string sourcePath, string outputPath, string mode, ulong seed, int count, TextWriter output)
    {
        IndexMutationResult result = IndexMutator.Apply(sourcePath, outputPath, mode, seed, count);
        output.WriteLine($"Created {mode} mutation at {result.IndexPath}; affected {result.AffectedCount} document(s). Manifest: {result.ManifestPath}");
        return CliExitCodes.Success;
    }

    private static Command BuildTransformCommands()
    {
        var source = new Argument<string>("source-index") { Description = "Source index. It is copied before transformation." };
        var output = new Argument<string>("output-index") { Description = "New output index directory." };
        var targetSegments = new Option<int>("--segments") { Description = "Target segment count (default 1).", DefaultValueFactory = _ => 1 };
        var command = new Command("transform", "Create a transformed index at a separate destination.");
        var compact = new Command("compact", "Copy and force-merge an index to the requested segment count.");
        compact.Add(source);
        compact.Add(output);
        compact.Add(targetSegments);
        compact.SetAction(result => RunCompact(GetRequiredValue(result, source), GetRequiredValue(result, output), result.GetValue(targetSegments), result.InvocationConfiguration.Output));
        command.Add(compact);
        return command;
    }

    private static int RunCorrupt(string sourcePath, string outputPath, string component, string mode,
        ulong seed, string? segmentId, TextWriter output)
    {
        string source = Path.GetFullPath(sourcePath);
        string destination = Path.GetFullPath(outputPath);
        EnsureNewDestination(source, destination);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Source index '{source}' does not exist.");
        string extension = component.ToLowerInvariant() switch
        {
            "postings" => ".pos",
            "stored-fields" => ".fdt",
            "terms" or "dictionary" => ".dic",
            "vectors" => ".vec",
            "hnsw" => ".hnsw",
            "live-docs" => ".del",
            "any" => "",
            _ => throw new ArgumentException($"Unknown corruption component '{component}'.")
        };
        if (mode is not ("truncate" or "flip-byte"))
            throw new ArgumentException($"Unknown corruption mode '{mode}'. Use truncate or flip-byte.");

        using var inspector = new Rowles.LeanCorpus.Index.Inspection.IndexInspector(source);
        CodecFileInventory[] candidates = inspector.GetFormatInventory().Segments
            .SelectMany(static item => item.Files)
            .Where(file => (extension.Length == 0 || string.Equals(file.Extension, extension, StringComparison.OrdinalIgnoreCase))
                && (segmentId is null || string.Equals(file.SegmentId, segmentId, StringComparison.Ordinal))
                && !string.IsNullOrEmpty(file.PhysicalFileName))
            .GroupBy(static file => file.PhysicalFileName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .Where(file => File.Exists(Path.Combine(source, file.PhysicalFileName))
                && new FileInfo(Path.Combine(source, file.PhysicalFileName)).Length > 0)
            .OrderBy(static file => file.PhysicalFileName, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException($"No physical file was found for component '{component}'.");
        CodecFileInventory target = candidates[(int)(seed % (ulong)candidates.Length)];
        IndexFingerprintResult baseFingerprint = IndexFingerprinter.Calculate(source);

        CopyDirectory(source, destination);
        string targetPath = Path.Combine(destination, target.PhysicalFileName);
        long originalLength = new FileInfo(targetPath).Length;
        long offset = -1;
        long resultingLength = originalLength;
        if (mode == "truncate")
        {
            resultingLength = Math.Max(1, originalLength / 2);
            using var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Write, FileShare.None);
            stream.SetLength(resultingLength);
        }
        else
        {
            offset = (long)(seed % (ulong)originalLength);
            using var stream = new FileStream(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.Position = offset;
            int value = stream.ReadByte();
            stream.Position = offset;
            stream.WriteByte((byte)(value ^ 0x5a));
            stream.Flush(flushToDisk: true);
        }

        string metadataDirectory = Path.Combine(destination, ".leancorpus");
        Directory.CreateDirectory(metadataDirectory);
        var manifest = new MutationManifest(1, baseFingerprint.RecipeIdentity, baseFingerprint.LogicalFingerprint, baseFingerprint.PhysicalFingerprint,
            component, mode, seed, target.PhysicalFileName, target.SegmentId, offset, originalLength, resultingLength);
        File.WriteAllText(Path.Combine(metadataDirectory, "mutation.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        output.WriteLine($"Created fixture at {destination}; {mode} applied to {target.PhysicalFileName}. Mutation manifest: {Path.Combine(metadataDirectory, "mutation.json")}");
        return CliExitCodes.Success;
    }

    private static int RunFixturePack(string sourcePath, string packagePath, TextWriter output)
    {
        string source = Path.GetFullPath(sourcePath);
        string package = Path.GetFullPath(packagePath);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Index directory '{source}' does not exist.");
        EnsureNewDestination(source, package);
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        ZipFile.CreateFromDirectory(source, package, CompressionLevel.Optimal, includeBaseDirectory: false);
        output.WriteLine($"Created fixture package {package} ({new FileInfo(package).Length} bytes).");
        return CliExitCodes.Success;
    }

    private static int RunFixtureUnpack(string packagePath, string outputPath, TextWriter output)
    {
        string package = Path.GetFullPath(packagePath);
        string destination = Path.GetFullPath(outputPath);
        if (!File.Exists(package))
            throw new FileNotFoundException("Fixture package was not found.", package);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException($"Output path '{destination}' already exists.");
        Directory.CreateDirectory(destination);
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(package);
            string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                if (!target.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidDataException($"Archive entry '{entry.FullName}' escapes the output directory.");
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: false);
            }
        }
        catch
        {
            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            throw;
        }
        output.WriteLine($"Unpacked fixture package to {destination}.");
        return CliExitCodes.Success;
    }

    private static int RunCompact(string sourcePath, string outputPath, int segmentCount, TextWriter output)
    {
        string source = Path.GetFullPath(sourcePath);
        string destination = Path.GetFullPath(outputPath);
        EnsureNewDestination(source, destination);
        if (segmentCount < 1)
            throw new ArgumentOutOfRangeException(nameof(segmentCount), "Target segment count must be positive.");
        CopyDirectory(source, destination);
        using (var directory = new MMapDirectory(destination))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            writer.ForceMerge(segmentCount);
            writer.Commit();
        }
        IndexDiffResult diff = IndexFingerprinter.Diff(source, destination, logicalOnly: true);
        output.WriteLine($"Compacted index into {destination}; logical equivalence: {diff.LogicalEquivalent}.");
        return diff.LogicalEquivalent ? CliExitCodes.Success : CliExitCodes.ValidationErrors;
    }

    private static void EnsureNewDestination(string source, string destination)
    {
        string relative = Path.GetRelativePath(source, destination);
        if (relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new ArgumentException("The output destination must be outside the source directory.");
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException($"Output path '{destination}' already exists.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private sealed record MutationManifest(int SchemaVersion, string? BaseRecipeIdentity, string BaseLogicalFingerprint, string BasePhysicalFingerprint,
        string Component, string Mode, ulong Seed, string TargetFile, string? SegmentId, long ByteOffset,
        long OriginalLength, long ResultingLength);
}
