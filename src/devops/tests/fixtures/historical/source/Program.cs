using System.Reflection;
using System.Security.Cryptography;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.HistoricalFixtures;

if (args.Length != 6)
    throw new ArgumentException("Supply the scenario, output directory, restored package archive and expected package and assembly identities.");

string scenario = args[0];
string outputRoot = Path.GetFullPath(args[1]);
string packageArchive = Path.GetFullPath(args[2]);
string expectedPackageSha256 = args[3];
string expectedAssemblySha256 = args[4];
string expectedAssemblyVersion = args[5];

string packageSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(packageArchive)));
string assemblyPath = typeof(IndexWriter).Assembly.Location;
string assemblySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
string assemblyVersion = typeof(IndexWriter).Assembly.GetName().Version?.ToString()
    ?? throw new InvalidDataException("The restored LeanCorpus assembly has no version.");

if (!string.Equals(packageSha256, expectedPackageSha256, StringComparison.Ordinal) ||
    !string.Equals(assemblySha256, expectedAssemblySha256, StringComparison.Ordinal) ||
    !string.Equals(assemblyVersion, expectedAssemblyVersion, StringComparison.Ordinal))
    throw new InvalidDataException("The restored package archive or loaded assembly does not match the historical fixture catalog pins.");

if (Directory.Exists(outputRoot) && Directory.EnumerateFileSystemEntries(outputRoot).Any())
    throw new IOException($"Output directory '{outputRoot}' must be empty.");
Directory.CreateDirectory(outputRoot);

switch (scenario)
{
    case "migration":
        MigrationScenario.Generate(outputRoot, packageArchive);
        break;
    case "sparse-hnsw":
        SparseHnswScenario.Generate(outputRoot);
        break;
    default:
        throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "The historical fixture scenario is not supported by this generator.");
}

Console.WriteLine($"Generated historical {scenario} fixtures with LeanCorpus {assemblyVersion}.");
