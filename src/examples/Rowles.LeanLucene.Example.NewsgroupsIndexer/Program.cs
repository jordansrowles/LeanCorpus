using System.Globalization;
using System.Text;
using Rowles.LeanLucene.Codecs.StoredFields;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Store;

try
{
    if (args is ["--help"] or ["-h"])
    {
        PrintUsage();
        return 0;
    }

    var options = NewsgroupsIndexOptions.Parse(args);
    int documentCount = BuildIndex(options);

    Console.WriteLine($"Indexed {documentCount} newsgroup document(s).");
    Console.WriteLine($"Source: {options.SourcePath}");
    Console.WriteLine($"Index:  {options.IndexPath}");
    Console.WriteLine();
    Console.WriteLine($"Check it with: src\\devops\\Rowles.LeanLucene.Cli\\bin\\Release\\net10.0\\leanlucene-cli.exe check \"{options.IndexPath}\" --deep");
    return 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    PrintUsage();
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

static int BuildIndex(NewsgroupsIndexOptions options)
{
    if (!Directory.Exists(options.SourcePath))
        throw new ArgumentException($"Newsgroups source path was not found: {options.SourcePath}");

    string sourcePath = Path.GetFullPath(options.SourcePath);
    string indexPath = Path.GetFullPath(options.IndexPath);
    if (string.Equals(sourcePath, indexPath, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("The source path and index path must be different.");

    if (options.Clean && Directory.Exists(indexPath))
        Directory.Delete(indexPath, recursive: true);

    Directory.CreateDirectory(indexPath);
    using var directory = new MMapDirectory(indexPath);
    using var writer = new IndexWriter(directory, new IndexWriterConfig
    {
        CompressionPolicy = FieldCompressionPolicy.Brotli,
        MaxBufferedDocs = 128,
        RamBufferSizeMB = 16,
        StoreTermVectors = true,
        HnswSeed = 1_337,
    });

    int documentCount = 0;
    foreach (string filePath in EnumerateNewsgroupFiles(sourcePath))
    {
        if (documentCount == options.Limit)
            break;

        var message = NewsgroupMessage.Read(sourcePath, filePath);
        writer.AddDocument(CreateDocument(message));
        documentCount++;
    }

    if (documentCount == 0)
        throw new ArgumentException($"No newsgroup files were found under: {sourcePath}");

    writer.Commit();
    return documentCount;
}

static IEnumerable<string> EnumerateNewsgroupFiles(string sourcePath)
{
    foreach (string filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories).OrderBy(static path => path, StringComparer.Ordinal))
    {
        string fileName = Path.GetFileName(filePath);
        if (fileName.Length == 0 || fileName.Any(static c => !char.IsDigit(c)))
            continue;

        yield return filePath;
    }
}

static LeanDocument CreateDocument(NewsgroupMessage message)
{
    var document = new LeanDocument();
    document.Add(new StringField("id", message.Id));
    document.Add(new StringField("category", message.Category));
    document.Add(new StringField("split", message.Split));
    document.Add(new StringField("from", message.From));
    document.Add(new TextField("subject", message.Subject));
    document.Add(new TextField("body", message.Body));
    document.Add(new TextField("organisation", message.Organisation));
    document.Add(new NumericField("line_count", message.LineCount));
    document.Add(new NumericField("byte_count", message.ByteCount));
    document.Add(new StoredField("path", message.RelativePath));
    document.Add(new StoredField("headers", message.Headers));
    document.Add(new VectorField("embedding", BuildEmbedding(message)));
    return document;
}

static ReadOnlyMemory<float> BuildEmbedding(NewsgroupMessage message)
{
    const int dimensions = 8;
    var vector = new float[dimensions];
    AddTokens(vector, message.Category);
    AddTokens(vector, message.Subject);
    AddTokens(vector, message.Body);

    float lengthSquared = 0;
    for (int i = 0; i < vector.Length; i++)
        lengthSquared += vector[i] * vector[i];

    if (lengthSquared == 0)
    {
        vector[0] = 1;
        return vector;
    }

    float length = MathF.Sqrt(lengthSquared);
    for (int i = 0; i < vector.Length; i++)
        vector[i] /= length;

    return vector;
}

static void AddTokens(float[] vector, string text)
{
    var token = new StringBuilder(capacity: 32);
    int tokenCount = 0;

    foreach (char c in text)
    {
        if (char.IsLetterOrDigit(c))
        {
            token.Append(char.ToLowerInvariant(c));
            continue;
        }

        FlushToken(vector, token, ref tokenCount);
        if (tokenCount >= 256)
            return;
    }

    FlushToken(vector, token, ref tokenCount);
}

static void FlushToken(float[] vector, StringBuilder token, ref int tokenCount)
{
    if (token.Length == 0)
        return;

    ulong hash = Fnv1A(token);
    int dimension = (int)(hash % (ulong)vector.Length);
    float sign = (hash & 0x100) == 0 ? 1 : -1;
    vector[dimension] += sign;
    token.Clear();
    tokenCount++;
}

static ulong Fnv1A(StringBuilder value)
{
    const ulong offset = 14_695_981_039_346_656_037;
    const ulong prime = 1_099_511_628_211;

    ulong hash = offset;
    for (int i = 0; i < value.Length; i++)
    {
        hash ^= value[i];
        hash *= prime;
    }

    return hash;
}

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run --project src\\examples\\Rowles.LeanLucene.Example.NewsgroupsIndexer -- [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --source <path>  20 Newsgroups root. Defaults to the bundled data\\20newsgroups copy.");
    Console.WriteLine("  --index <path>   Output index path. Defaults to artifacts\\newsgroups-index.");
    Console.WriteLine("  --limit <count>  Maximum documents to index. Defaults to 500.");
    Console.WriteLine("  --append         Keep existing index files instead of recreating the output directory.");
    Console.WriteLine("  -h, --help       Show this help.");
}

internal sealed record NewsgroupsIndexOptions(string SourcePath, string IndexPath, int Limit, bool Clean)
{
    public static NewsgroupsIndexOptions Parse(string[] args)
    {
        string? sourcePath = null;
        string? indexPath = null;
        int limit = 500;
        bool clean = true;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--source":
                    sourcePath = ReadValue(args, ref i, arg);
                    break;
                case "--index":
                    indexPath = ReadValue(args, ref i, arg);
                    break;
                case "--limit":
                    string limitText = ReadValue(args, ref i, arg);
                    if (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit <= 0)
                        throw new ArgumentException("--limit must be a positive integer.");
                    break;
                case "--append":
                    clean = false;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        sourcePath ??= FindDefaultSourcePath();
        indexPath ??= Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "newsgroups-index");
        return new NewsgroupsIndexOptions(
            Path.GetFullPath(sourcePath),
            Path.GetFullPath(indexPath),
            limit,
            clean);
    }

    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{optionName} requires a value.");

        index++;
        return args[index];
    }

    private static string FindDefaultSourcePath()
    {
        foreach (string root in CandidateRoots())
        {
            string bundledCandidate = Path.Combine(root, "data", "20newsgroups");
            if (Directory.Exists(bundledCandidate))
                return bundledCandidate;
        }

        foreach (string root in CandidateRoots())
        {
            string projectCandidate = Path.Combine(root, "src", "examples", "Rowles.LeanLucene.Example.NewsgroupsIndexer", "data", "20newsgroups");
            if (Directory.Exists(projectCandidate))
                return projectCandidate;
        }

        foreach (string root in CandidateRoots())
        {
            string candidate = Path.Combine(root, "_notes", "example data", "data", "20newsgroups");
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new ArgumentException("Could not find bundled data\\20newsgroups. Pass --source <path>.");
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (seen.Add(directory.FullName))
                    yield return directory.FullName;

                directory = directory.Parent;
            }
        }
    }
}

internal sealed record NewsgroupMessage(
    string Id,
    string RelativePath,
    string Split,
    string Category,
    string From,
    string Subject,
    string Organisation,
    string Body,
    string Headers,
    int LineCount,
    long ByteCount)
{
    public static NewsgroupMessage Read(string sourcePath, string filePath)
    {
        string text = File.ReadAllText(filePath, Encoding.UTF8);
        string normalisedText = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        int headerEnd = normalisedText.IndexOf("\n\n", StringComparison.Ordinal);
        string headerText = headerEnd >= 0 ? normalisedText[..headerEnd] : string.Empty;
        string body = headerEnd >= 0 ? normalisedText[(headerEnd + 2)..].Trim() : normalisedText.Trim();
        var headers = ParseHeaders(headerText);

        string relativePath = Path.GetRelativePath(sourcePath, filePath);
        string[] parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string split = parts.Length >= 3 ? parts[0] : "unknown";
        string category = parts.Length >= 2 ? parts[^2] : "unknown";
        string id = relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        string subject = GetHeader(headers, "Subject", Path.GetFileName(filePath));

        return new NewsgroupMessage(
            id,
            relativePath,
            split,
            category,
            GetHeader(headers, "From", "unknown"),
            subject,
            GetHeader(headers, "Organization", string.Empty),
            body,
            headerText,
            CountLines(body),
            new FileInfo(filePath).Length);
    }

    private static Dictionary<string, string> ParseHeaders(string headerText)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentName = null;

        foreach (string line in headerText.Split('\n'))
        {
            if ((line.StartsWith(' ') || line.StartsWith('\t')) && currentName is not null)
            {
                headers[currentName] += " " + line.Trim();
                continue;
            }

            int separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
                continue;

            currentName = line[..separator].Trim();
            headers[currentName] = line[(separator + 1)..].Trim();
        }

        return headers;
    }

    private static string GetHeader(IReadOnlyDictionary<string, string> headers, string name, string fallback)
        => headers.TryGetValue(name, out string? value) && value.Length > 0 ? value : fallback;

    private static int CountLines(string value)
    {
        if (value.Length == 0)
            return 0;

        int count = 1;
        foreach (char c in value)
        {
            if (c == '\n')
                count++;
        }

        return count;
    }
}
