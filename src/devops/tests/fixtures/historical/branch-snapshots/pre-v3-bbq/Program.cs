using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Buffers.Binary;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

const string sourceCommit = "3dab5b656c0077470cd216646ba085a04fa40ef3";
const string sourceBranch = "fix/bbq-retrieval-quality";
const long graphSeed = 311031L;
const int dimension = 128;
const int documentCount = 8;

if (args.Length != 1)
    throw new ArgumentException("Pass one output directory outside the repository.");

string repositoryRoot = RunGit("rev-parse", "--show-toplevel");
string outputRoot = Path.GetFullPath(args[0]);
if (outputRoot.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
    string.Equals(outputRoot, repositoryRoot, StringComparison.Ordinal))
    throw new InvalidOperationException("Fixture generation output must be outside the repository.");
if (Directory.Exists(outputRoot) || File.Exists(outputRoot))
    throw new IOException($"Output path '{outputRoot}' already exists.");
if (RunGit("rev-parse", "HEAD") != sourceCommit ||
    RunGit("branch", "--show-current") != sourceBranch ||
    RunGit("status", "--porcelain", "--", "src/core/Rowles.LeanCorpus").Length != 0)
    throw new InvalidOperationException("This one-time snapshot generator requires the pinned clean Core source commit.");

Directory.CreateDirectory(outputRoot);
string generatedRoot = Path.Combine(outputRoot, "indexes");
Directory.CreateDirectory(generatedRoot);

foreach (bool compound in new[] { false, true })
    GenerateIndex(Path.Combine(generatedRoot, compound ? "compound" : "loose"), compound);

string[] files = Directory.EnumerateFiles(generatedRoot, "*", SearchOption.AllDirectories)
    .OrderBy(path => Path.GetRelativePath(generatedRoot, path), StringComparer.Ordinal)
    .ToArray();
foreach (string path in files)
    AssertBodyV2IfLoose(path);

byte[] archive = CreateArchive(generatedRoot);
string fixtureName = "pre-v3-bbq-v2.fixture.b64";
File.WriteAllText(Path.Combine(outputRoot, fixtureName), Convert.ToBase64String(archive) + "\n");

var manifest = new
{
    FixtureFormatVersion = 1,
    FixtureId = "pre-v3-bbq-v2",
    SourceRepository = "https://github.com/jordansrowles/LeanCorpus",
    SourceBranch = sourceBranch,
    SourceCommit = sourceCommit,
    SourceCommitParent = RunGit("rev-parse", sourceCommit + "^").Trim(),
    GeneratorSdk = RunDotnetVersion(),
    GeneratorRuntime = Environment.Version.ToString(),
    GenerationSeed = graphSeed,
    Dimension = dimension,
    DocumentCount = documentCount,
    FieldName = "embedding",
    Normalised = true,
    Quantisation = "BBQ",
    QuantisedVectorBodyVersion = 2,
    Layouts = new[] { "loose", "compound" },
    PresentVectorDocumentIds = new[] { 0, 1, 2, 4, 6 },
    ExplicitZeroVectorDocumentIds = new[] { 0 },
    MissingVectorDocumentIds = new[] { 3, 5, 7 },
    ArchiveFile = fixtureName,
    ArchiveSha256 = Sha256(archive),
    Files = files.Select(path =>
    {
        byte[] bytes = File.ReadAllBytes(path);
        return new
        {
            Name = Path.GetRelativePath(generatedRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
            Length = bytes.LongLength,
            Sha256 = Sha256(bytes)
        };
    }).ToArray()
};
File.WriteAllText(Path.Combine(outputRoot, "pre-v3-bbq-v2.manifest.json"),
    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");

Console.WriteLine($"Fixture: {Path.Combine(outputRoot, fixtureName)}");
Console.WriteLine($"SHA-256: {Sha256(archive)}");
Console.WriteLine($"Manifest: {Path.Combine(outputRoot, "pre-v3-bbq-v2.manifest.json")}");

void GenerateIndex(string indexPath, bool compound)
{
    Directory.CreateDirectory(indexPath);
    using var directory = new MMapDirectory(indexPath);
    var config = new IndexWriterConfig
    {
        BuildHnswOnFlush = true,
        HnswSeed = graphSeed,
        MaxBufferedDocs = 16,
        MergePolicy = NoMergePolicy.Instance,
        NormaliseVectors = true,
        UseCompoundFile = compound,
        VectorQuantisation = Rowles.LeanCorpus.Codecs.Vectors.VectorQuantisation.BBQ
    };

    float[][] vectors = CreateVectors();
    using var writer = new IndexWriter(directory, config);
    for (int docId = 0; docId < documentCount; docId++)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", docId.ToString(System.Globalization.CultureInfo.InvariantCulture), stored: true));
        if (docId is 0 or 1 or 2 or 4 or 6)
            document.Add(new VectorField("embedding", vectors[docId]));
        writer.AddDocument(document);
    }
    writer.Commit();

    string[] indexFiles = Directory.GetFiles(indexPath);
    bool hasContainer = indexFiles.Any(path => path.EndsWith(compound ? ".cfs" : ".vq", StringComparison.Ordinal));
    bool hasGraph = compound || indexFiles.Any(path => path.EndsWith(".hnsw", StringComparison.Ordinal));
    if (!hasContainer || !hasGraph)
        throw new InvalidDataException("The generated fixture is missing its quantised vectors or HNSW data.");
}

float[][] CreateVectors()
{
    var vectors = new float[documentCount][];
    vectors[0] = new float[dimension];
    vectors[1] = Basis(0, 1f);
    vectors[2] = Basis(1, 1f);
    vectors[3] = new float[dimension];
    vectors[4] = Basis(0, -1f);
    vectors[5] = new float[dimension];
    vectors[6] = CreateDenseVector();
    vectors[7] = new float[dimension];
    return vectors;

    float[] Basis(int index, float value)
    {
        var vector = new float[dimension];
        vector[index] = value;
        return vector;
    }

    float[] CreateDenseVector()
    {
        var vector = new float[dimension];
        for (int index = 0; index < vector.Length; index++)
            vector[index] = index % 3 == 0 ? 1f : index % 3 == 1 ? -0.5f : 0.25f;
        return vector;
    }
}

void AssertBodyV2IfLoose(string path)
{
    if (!path.EndsWith(".vq", StringComparison.Ordinal))
        return;
    byte[] bytes = File.ReadAllBytes(path);
    int formatIdLength = bytes[5];
    int bodyStart = checked(16 + formatIdLength);
    int bodyVersion = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(6, sizeof(int)));
    if (bodyVersion != 2 || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(bodyStart + 4, sizeof(int))) != dimension ||
        bytes[bodyStart + 8] != 2)
        throw new InvalidDataException("Pinned source did not emit a v2 BBQ vector body.");
}

static byte[] CreateArchive(string root)
{
    using var compressed = new MemoryStream();
    using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
    using (var tar = new TarWriter(gzip, leaveOpen: true))
    {
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(item => Path.GetRelativePath(root, item), StringComparer.Ordinal))
        {
            string name = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            using Stream data = File.OpenRead(path);
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = data });
        }
    }
    return compressed.ToArray();
}

static string Sha256(ReadOnlySpan<byte> bytes)
    => Convert.ToHexStringLower(SHA256.HashData(bytes));

static string RunGit(params string[] arguments)
    => Run("git", arguments).Trim();

static string RunDotnetVersion()
    => Run("dotnet", ["--version"]).Trim();

static string Run(string executable, string[] arguments)
{
    var start = new ProcessStartInfo(executable)
    {
        WorkingDirectory = Directory.GetCurrentDirectory(),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    foreach (string argument in arguments)
        start.ArgumentList.Add(argument);
    using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"{executable} exited {process.ExitCode}: {error}");
    return output;
}
