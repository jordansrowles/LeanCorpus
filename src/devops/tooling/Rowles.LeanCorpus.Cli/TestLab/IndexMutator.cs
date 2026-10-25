using System.Text.Json;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Cli.Creation;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Inspection;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Cli.TestLab;

internal sealed record IndexMutationResult(string IndexPath, string Mode, ulong Seed, int RequestedCount,
    int AffectedCount, IReadOnlyList<string> AffectedIds, IndexFingerprintResult Before,
    IndexFingerprintResult After, string ManifestPath);

internal sealed record IndexMutationManifest(int SchemaVersion, string Mode, ulong Seed, int RequestedCount,
    IReadOnlyList<string> AffectedIds, string? BaseRecipeIdentity, string BaseLogicalFingerprint,
    string BasePhysicalFingerprint, string ResultLogicalFingerprint, string ResultPhysicalFingerprint);

internal static class IndexMutator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static IndexMutationResult Apply(string sourcePath, string outputPath, string mode, ulong seed, int count)
    {
        string source = Path.GetFullPath(sourcePath);
        string destination = Path.GetFullPath(outputPath);
        EnsureNewDestination(source, destination);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Source index '{source}' does not exist.");
        if (count is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(count), "Mutation count must be between 1 and 100,000.");
        if (mode == "many-small-segments" && count > 10_000)
            throw new ArgumentOutOfRangeException(nameof(count), "many-small-segments is bounded to 10,000 documents.");
        if (mode is not ("add" or "delete" or "update" or "many-commits" or "many-small-segments" or "merge-heavy"))
            throw new ArgumentException($"Unknown mutation mode '{mode}'.");

        IndexFingerprintResult before = IndexFingerprinter.Calculate(source);
        IReadOnlyList<IndexInspectionTerm> idTerms = [];
        if (mode is "delete" or "update")
        {
            using var sourceInspector = new IndexInspector(source);
            idTerms = sourceInspector.GetTerms("id", take: 100_000);
            if (idTerms.Count == 0)
                throw new InvalidOperationException("The source index has no indexed 'id' field to delete or update.");
        }
        CopyDirectory(source, destination);

        var affectedIds = new List<string>();
        using (var directory = new MMapDirectory(destination))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MergePolicy = mode == "many-small-segments" ? NoMergePolicy.Instance : new TieredMergePolicy()
        }))
        {
            if (mode is "delete" or "update")
            {
                string[] selectedIds = SelectIds(idTerms, count, seed);
                foreach (string id in selectedIds)
                {
                    writer.DeleteDocuments(new TermQuery("id", id));
                    affectedIds.Add(id);
                }

                if (mode == "update")
                    AddGenerated(writer, seed, selectedIds.Length, mode, selectedIds, affectedIds);
            }
            else
                AddGenerated(writer, seed, count, mode, null, affectedIds);

            if (mode is "many-commits" or "many-small-segments" or "merge-heavy")
                writer.Commit();
            if (mode == "merge-heavy")
                writer.ForceMerge(1);
            writer.Commit();
        }

        IndexFingerprintResult after = IndexFingerprinter.Calculate(destination);
        string metadataDirectory = Path.Combine(destination, ".leancorpus");
        Directory.CreateDirectory(metadataDirectory);
        string manifestPath = Path.Combine(metadataDirectory, "mutation.json");
        var manifest = new IndexMutationManifest(1, mode, seed, count, Array.AsReadOnly(affectedIds.ToArray()),
            before.RecipeIdentity, before.LogicalFingerprint, before.PhysicalFingerprint,
            after.LogicalFingerprint, after.PhysicalFingerprint);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine);
        return new IndexMutationResult(destination, mode, seed, count, affectedIds.Count,
            Array.AsReadOnly(affectedIds.ToArray()), before, after, manifestPath);
    }

    private static string[] SelectIds(IReadOnlyList<IndexInspectionTerm> terms, int count, ulong seed)
    {
        int selectedCount = Math.Min(count, terms.Count);
        int start = (int)(seed % (ulong)terms.Count);
        var selected = new string[selectedCount];
        for (int index = 0; index < selectedCount; index++)
            selected[index] = terms[(start + index) % terms.Count].Term;
        return selected;
    }

    private static void AddGenerated(IndexWriter writer, ulong seed, int count, string mode,
        IReadOnlyList<string>? replacementIds, List<string> affectedIds)
    {
        var profile = new LeanCorpusSearchProfile();
        var options = new DataForgeGenerationOptions(seed, count, new Dictionary<string, string>(StringComparer.Ordinal));
        int ordinal = 0;
        foreach (SearchRecord record in profile.Generate(options))
        {
            string id = replacementIds is null ? $"mutation-{seed:x16}-{record.Id}" : replacementIds[ordinal];
            var document = new LeanDocument();
            document.Add(new StringField("id", id));
            document.Add(new TextField("title", record.Title, stored: true));
            document.Add(new TextField("body", record.Body, stored: true));
            document.Add(new StringField("locale", record.Locale));
            document.Add(new StringField("archetype", record.Archetype));
            document.Add(new StringField("category", record.Category));
            document.Add(new StringField("region", record.Region));
            document.Add(new NumericField("price", record.PriceMinor));
            document.Add(new GeoPointField("location", record.LatitudeE6 / 1_000_000.0, record.LongitudeE6 / 1_000_000.0));
            writer.AddDocument(document);
            if (replacementIds is null)
                affectedIds.Add(id);
            if (mode is "many-commits" or "many-small-segments" or "merge-heavy")
                writer.Commit();
            ordinal++;
        }
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
}
