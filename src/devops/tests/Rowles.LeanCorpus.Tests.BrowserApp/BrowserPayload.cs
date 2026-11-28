using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.BrowserApp;

/// <summary>One query outcome compared between the desktop builder and the browser reader.</summary>
public sealed record PayloadQueryResult(string Key, int TotalHits, string[] Ids, float[] Scores);

/// <summary>One file of a prebuilt index payload.</summary>
public sealed record PayloadFile(string Name, long Length, string Sha256);

/// <summary>
/// Deterministic prebuilt-index payloads shared by the desktop host (builder) and browser client (reader).
/// Trusted test data only: this is not a public import format.
/// </summary>
public static class BrowserPayload
{
    public const int VectorDimensions = 64;
    public const string ChineseSample = "朋友在美国学习电脑工作时间";
    public const string JapaneseSample = "東京都に住んでいます";
    public const string MixedSample = "Searching browsers' indexes quickly: Überprüfung, рецепты, البحث, 검색 엔진, búsquedas";
    private static readonly string[] s_terms = ["w0", "w3", "w17", "w250", "w4000", "marker"];

    public static (int Documents, bool Compound, FieldCompressionPolicy Compression) Variant(string name) => name switch
    {
        "small-cfs" => (2_000, true, FieldCompressionPolicy.Deflate),
        "large-cfs" => (40_000, true, FieldCompressionPolicy.Deflate),
        "large-loose" => (40_000, false, FieldCompressionPolicy.Deflate),
        "brotli" => (500, true, FieldCompressionPolicy.Brotli),
        _ => throw new ArgumentException("Unknown payload variant: " + name, nameof(name))
    };

    public static LeanDocument MakeDocument(int index)
    {
        var random = new Random(unchecked(index * 7919 + 17)); // NOSONAR: deterministic synthetic corpus, not security-sensitive
        var body = new StringBuilder(320);
        for (int word = 0; word < 40; word++)
        {
            double u = random.NextDouble();
            body.Append('w').Append((int)(5_000 * u * u * u)).Append(' ');
        }
        if (index % 97 == 0) body.Append("marker");

        var document = new LeanDocument();
        document.Add(new StringField("id", "d" + index.ToString(CultureInfo.InvariantCulture)));
        document.Add(new TextField("body", body.ToString()));
        document.Add(new NumericField("number", index % 1_000));
        document.Add(new VectorField("embedding", UnitVector(random)));
        return document;
    }

    public static float[] QueryVector() => UnitVector(new Random(12_345)); // NOSONAR: deterministic query vector

    private static float[] UnitVector(Random random)
    {
        var vector = new float[VectorDimensions];
        double norm = 0;
        for (int d = 0; d < vector.Length; d++)
        {
            vector[d] = (float)(random.NextDouble() * 2 - 1);
            norm += vector[d] * vector[d];
        }
        float scale = (float)(1 / Math.Sqrt(norm));
        for (int d = 0; d < vector.Length; d++) vector[d] *= scale;
        return vector;
    }

    /// <summary>Builds a single-segment payload index in <paramref name="path"/> and returns its manifest.</summary>
    public static byte[] Create(string path, string variant)
    {
        var (documents, compound, compression) = Variant(variant);
        using (var directory = new MMapDirectory(path))
        {
            using (var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                DurableCommits = false,
                UseCompoundFile = compound,
                CompressionPolicy = compression,
                BuildHnswOnFlush = true,
                MaxBufferedDocs = 10_000
            }))
            {
                var batch = new List<LeanDocument>(1_000);
                for (int i = 0; i < documents; i++)
                {
                    batch.Add(MakeDocument(i));
                    if (batch.Count == 1_000) { writer.AddDocuments(batch); batch.Clear(); }
                }
                if (batch.Count > 0) writer.AddDocuments(batch);
                writer.Commit();
                writer.ForceMerge(1);
                writer.Commit();
            }

            using var searcher = new IndexSearcher(directory);
            return WriteManifest(variant, path, Evaluate(searcher, readStoredFields: compression != FieldCompressionPolicy.Brotli));
        }
    }

    private static byte[] WriteManifest(string variant, string path, IReadOnlyList<PayloadQueryResult> expected)
    {
        using var output = new MemoryStream();
        using (var json = new Utf8JsonWriter(output))
        {
            json.WriteStartObject();
            json.WriteString("variant", variant);
            json.WriteStartArray("files");
            foreach (string file in Directory.GetFiles(path).Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(file);
                if (name == "write.lock") continue;
                json.WriteStartObject();
                json.WriteString("name", name);
                json.WriteNumber("length", new FileInfo(file).Length);
                using (var stream = File.OpenRead(file)) json.WriteString("sha256", Convert.ToHexString(SHA256.HashData(stream)));
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("expected");
            foreach (var result in expected)
            {
                json.WriteStartObject();
                json.WriteString("key", result.Key);
                json.WriteNumber("totalHits", result.TotalHits);
                json.WriteStartArray("ids");
                foreach (string id in result.Ids) json.WriteStringValue(id);
                json.WriteEndArray();
                json.WriteStartArray("scores");
                foreach (float score in result.Scores) json.WriteNumberValue(score);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return output.ToArray();
    }

    public static (PayloadFile[] Files, PayloadQueryResult[] Expected) ReadManifest(byte[] manifest)
    {
        using var document = JsonDocument.Parse(manifest);
        var root = document.RootElement;
        var files = root.GetProperty("files").EnumerateArray()
            .Select(file => new PayloadFile(file.GetProperty("name").GetString()!, file.GetProperty("length").GetInt64(), file.GetProperty("sha256").GetString()!))
            .ToArray();
        var expected = root.GetProperty("expected").EnumerateArray()
            .Select(result => new PayloadQueryResult(
                result.GetProperty("key").GetString()!,
                result.GetProperty("totalHits").GetInt32(),
                result.GetProperty("ids").EnumerateArray().Select(static id => id.GetString()!).ToArray(),
                result.GetProperty("scores").EnumerateArray().Select(static score => score.GetSingle()).ToArray()))
            .ToArray();
        return (files, expected);
    }

    /// <summary>Runs a fixed query set touching postings, norms, stored fields, numeric and vector structures.</summary>
    public static PayloadQueryResult[] Evaluate(IndexSearcher searcher, bool readStoredFields = true)
    {
        var results = new List<PayloadQueryResult>();
        foreach (string term in s_terms)
            results.Add(Capture("term:" + term, searcher, searcher.Search(new TermQuery("body", term), 10), readStoredFields));
        var boolean = new BooleanQuery.Builder()
            .Add(new TermQuery("body", "w3"), Occur.Must)
            .Add(new TermQuery("body", "w17"), Occur.Should)
            .Add(new TermQuery("body", "w0"), Occur.MustNot)
            .Build();
        results.Add(Capture("boolean", searcher, searcher.Search(boolean, 10), readStoredFields));
        results.Add(Capture("phrase", searcher, searcher.Search(new PhraseQuery("body", "w0", "w1"), 10), readStoredFields));
        results.Add(Capture("range", searcher, searcher.Search(new RangeQuery("number", 100, 199), 10), readStoredFields));
        results.Add(Capture("vector", searcher, searcher.Search(new VectorQuery("embedding", QueryVector(), topK: 10), 10), readStoredFields));
        return results.ToArray();
    }

    private static PayloadQueryResult Capture(string key, IndexSearcher searcher, TopDocs top, bool readStoredFields)
    {
        var ids = readStoredFields
            ? top.ScoreDocs.Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0]).ToArray()
            : top.ScoreDocs.Select(static hit => "#" + hit.DocId.ToString(CultureInfo.InvariantCulture)).ToArray();
        return new PayloadQueryResult(key, top.TotalHits, ids, top.ScoreDocs.Select(static hit => hit.Score).ToArray());
    }

    /// <summary>Returns human-readable differences; empty when the results agree within float tolerance.</summary>
    public static List<string> Compare(IReadOnlyList<PayloadQueryResult> expected, IReadOnlyList<PayloadQueryResult> actual)
    {
        var differences = new List<string>();
        foreach (var want in expected)
        {
            var got = actual.FirstOrDefault(result => result.Key == want.Key);
            if (got is null) { differences.Add(want.Key + ": missing"); continue; }
            if (got.TotalHits != want.TotalHits) differences.Add($"{want.Key}: totalHits {got.TotalHits} != {want.TotalHits}");
            if (!got.Ids.SequenceEqual(want.Ids)) differences.Add($"{want.Key}: ids [{string.Join(',', got.Ids)}] != [{string.Join(',', want.Ids)}]");
            for (int i = 0; i < Math.Min(got.Scores.Length, want.Scores.Length); i++)
            {
                float tolerance = Math.Max(1e-5f, Math.Abs(want.Scores[i]) * 1e-5f);
                if (Math.Abs(got.Scores[i] - want.Scores[i]) > tolerance)
                {
                    differences.Add($"{want.Key}: score[{i}] {got.Scores[i]:R} != {want.Scores[i]:R}");
                    break;
                }
            }
        }
        return differences;
    }

    /// <summary>Analysis outcomes for one host: lexicon size and tokens, or the failure.</summary>
    public static byte[] AnalysisReport()
    {
        using var output = new MemoryStream();
        using (var json = new Utf8JsonWriter(output))
        {
            json.WriteStartObject();
            json.WriteNumber("zhLexiconWords", ChineseLexicon.Default.Count);
            json.WriteString("zhTokens", Tokens("zh", ChineseSample));
            json.WriteString("jaTokens", Tokens("ja", JapaneseSample));
            json.WriteString("zhAnalysisId", AnalyserFactory.GetDefaultAnalysisId("zh"));
            foreach (string language in AnalyserFactory.SupportedLanguages)
                json.WriteString("tokens:" + language, Tokens(language, MixedSample));
            json.WriteEndObject();
        }
        return output.ToArray();
    }

    public static string Tokens(string language, string text)
    {
        try
        {
            var sink = new CollectingSink();
            AnalyserFactory.Create(language).Analyse(text, sink);
            return string.Join('|', sink.Tokens);
        }
        catch (Exception error)
        {
            return "!" + error.GetType().Name + ": " + error.Message;
        }
    }

    private sealed class CollectingSink : ISpanTokenSink
    {
        public List<string> Tokens { get; } = [];

        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type = Token.DefaultType, int positionIncrement = 1, byte[]? payload = null)
            => Tokens.Add(text.ToString());
    }
}
