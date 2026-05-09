using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Provides a unified pool of real-world document bodies sourced from
/// bench/data/ (Gutenberg ebooks, 20 Newsgroups, Reuters-21578).
/// Falls back to synthetic content when no real data is present.
/// </summary>
/// <remarks>
/// Span/ReadOnlyMemory can reduce intermediate copies during the parsing phase,
/// but since IndexWriter requires string values, slices must ultimately be
/// materialised via ToString() -- same heap footprint. The practical limit is
/// capping per-source document counts to avoid loading all 18K newsgroup files.
/// </remarks>
internal static class RealDataPool
{
    private sealed class LoadedDataSet
    {
        public string[] Bodies { get; init; } = [];
        public BenchmarkDataSourceReport[] Sources { get; init; } = [];
    }

    private static readonly Lazy<LoadedDataSet> DataSet =
        new(LoadAll, LazyThreadSafetyMode.ExecutionAndPublication);

    private const int MinBodyLength = 40;

    /// <summary>Maximum documents loaded from the 20 Newsgroups source.</summary>
    private const int MaxNewsgroups = 15_000;

    /// <summary>Maximum documents loaded from the Reuters source.</summary>
    private const int MaxReuters = 15_000;

    /// <summary>
    /// Returns <paramref name="count"/> document bodies from the real-data pool,
    /// wrapping round-robin when count exceeds the pool size.
    /// </summary>
    public static string[] GetBodies(int count)
    {
        var pool = DataSet.Value.Bodies;
        if (pool.Length == 0)
            return BuildSynthetic(count);

        var result = new string[count];
        for (int i = 0; i < count; i++)
            result[i] = pool[i % pool.Length];
        return result;
    }

    public static BenchmarkDataSourceReport[] GetLoadedDataSources()
        => DataSet.IsValueCreated ? DataSet.Value.Sources : [];

    private static LoadedDataSet LoadAll()
    {
        var root = GutenbergDataLoader.FindRepositoryRoot();
        var benchDir = Path.Combine(root, "bench", "data");

        if (!Directory.Exists(benchDir))
        {
            Console.Error.WriteLine(
                $"[RealDataPool] bench/data not found at '{benchDir}'. " +
                "Run the download scripts first. Falling back to synthetic data.");
            return new LoadedDataSet
            {
                Sources =
                [
                    new BenchmarkDataSourceReport
                    {
                        Name = "synthetic",
                        Path = benchDir,
                        FallbackUsed = true,
                    }
                ]
            };
        }

        var bodies = new List<string>(60_000);
        var sources = new List<BenchmarkDataSourceReport>(3);

        sources.Add(LoadGutenberg(benchDir, bodies));
        sources.Add(Load20Newsgroups(benchDir, bodies));
        sources.Add(LoadReuters(benchDir, bodies));

        if (bodies.Count == 0)
        {
            Console.Error.WriteLine(
                "[RealDataPool] No documents loaded from bench/data. " +
                "Run the download scripts first. Falling back to synthetic data.");
            return new LoadedDataSet
            {
                Sources =
                [
                    new BenchmarkDataSourceReport
                    {
                        Name = "synthetic",
                        Path = benchDir,
                        FallbackUsed = true,
                    }
                ]
            };
        }

        Console.Error.WriteLine($"[RealDataPool] Loaded {bodies.Count:N0} real-data documents.");
        return new LoadedDataSet
        {
            Bodies = [.. bodies],
            Sources = [.. sources]
        };
    }

    private static BenchmarkDataSourceReport LoadGutenberg(string benchDir, List<string> bodies)
    {
        var dir = Path.Combine(benchDir, "gutenberg-ebooks");
        if (!Directory.Exists(dir))
            return BuildMissingSource("gutenberg", dir);

        var before = bodies.Count;
        try
        {
            // Reuse existing loader; extract paragraph bodies
            var paragraphs = GutenbergDataLoader.Load(dir);
            foreach (var p in paragraphs)
                bodies.Add(p.Body);
        }
        catch (FileNotFoundException)
        {
            // No .txt files found - skip silently
        }
        return BuildSourceReport("gutenberg", dir, bodies.Count - before);
    }

    private static BenchmarkDataSourceReport Load20Newsgroups(string benchDir, List<string> bodies)
    {
        var dir = Path.Combine(benchDir, "20newsgroups");
        if (!Directory.Exists(dir))
            return BuildMissingSource("20newsgroups", dir);

        var count = 0;
        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                     .Where(f => IsNumericFilename(f))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (count >= MaxNewsgroups)
                break;

            var body = ExtractNewsBody(file);
            if (body.Length >= MinBodyLength)
            {
                bodies.Add(body);
                count++;
            }
        }
        return BuildSourceReport("20newsgroups", dir, count);
    }

    private static BenchmarkDataSourceReport LoadReuters(string benchDir, List<string> bodies)
    {
        var dir = Path.Combine(benchDir, "reuters21578");
        if (!Directory.Exists(dir))
            return BuildMissingSource("reuters21578", dir);

        var count = 0;
        foreach (var file in Directory.GetFiles(dir, "*.sgm", SearchOption.TopDirectoryOnly)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (count >= MaxReuters)
                break;

            var raw = File.ReadAllText(file, System.Text.Encoding.Latin1);
            foreach (Match m in ReutersBodyPattern.Matches(raw))
            {
                if (count >= MaxReuters)
                    break;
                var body = m.Groups[1].Value.Trim();
                if (body.Length >= MinBodyLength)
                {
                    bodies.Add(body);
                    count++;
                }
            }
        }
        return BuildSourceReport("reuters21578", dir, count);
    }

    private static BenchmarkDataSourceReport BuildMissingSource(string name, string path)
    {
        return new BenchmarkDataSourceReport
        {
            Name = name,
            Path = path,
            FallbackUsed = false,
        };
    }

    private static BenchmarkDataSourceReport BuildSourceReport(string name, string path, int documentCount)
    {
        var files = Directory.Exists(path)
            ? Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        long byteCount = 0;
        var builder = new StringBuilder();
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            byteCount += info.Length;
            builder
                .Append(Path.GetRelativePath(path, file)).Append('\0')
                .Append(info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\0')
                .Append(info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\0');
        }

        return new BenchmarkDataSourceReport
        {
            Name = name,
            Path = path,
            FileCount = files.Length,
            ByteCount = byteCount,
            DocumentCount = documentCount,
            FingerprintSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))),
        };
    }

    // Non-greedy match of <BODY>...</BODY> in Reuters SGML
    private static readonly Regex ReutersBodyPattern = new(
        @"<BODY>(.*?)</BODY>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(30));

    private static bool IsNumericFilename(string path)
    {
        var name = Path.GetFileName(path);
        return name.Length > 0 && name.All(char.IsDigit);
    }

    private static string ExtractNewsBody(string filePath)
    {
        try
        {
            // Usenet/email format: RFC 2822 headers end at first blank line
            var lines = File.ReadAllLines(filePath, System.Text.Encoding.Latin1);
            var bodyStart = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    bodyStart = i + 1;
                    break;
                }
            }

            if (bodyStart >= lines.Length)
                return string.Empty;

            return string.Join(" ", lines[bodyStart..]).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Synthetic fallback used when bench/data is absent or empty.</summary>
    private static string[] BuildSynthetic(int count)
    {
        var topics  = new[] { "government", "economics", "politics", "science", "technology" };
        var domains = new[] { "national", "international", "regional", "local" };
        var docs = new string[count];
        for (int i = 0; i < count; i++)
        {
            var kw = (i % 3) switch { 0 => "said", 1 => "people", _ => "market" };
            docs[i] = $"doc {i} {kw} {topics[i % topics.Length]} {domains[(i * 7) % domains.Length]} " +
                      "president company reported financial political economic national government";
        }
        return docs;
    }
}
