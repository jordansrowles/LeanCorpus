using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Index.Indexer;

/// <summary>
/// Exercises the legacy field-processing implementation retained on <see cref="IndexWriter"/>.
/// </summary>
public sealed class IndexWriterFieldProcessingTests : IClassFixture<TestDirectoryFixture>
{
    private static readonly MethodInfo AddDocumentCoreMethod =
        typeof(IndexWriter).GetMethod("AddDocumentCore", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("IndexWriter.AddDocumentCore was not found.");

    private readonly TestDirectoryFixture _fixture;

    public IndexWriterFieldProcessingTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "IndexWriter field processing: indexes every field kind and DocValues")]
    public void AddDocumentCore_IndexesEveryFieldKindAndDocValues()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentCore_IndexesEveryFieldKindAndDocValues)));
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            FieldAnalysers = new Dictionary<string, IAnalyser>
            {
                ["body"] = new PayloadWhitespaceAnalyser()
            },
            MaxBufferedDocs = 1,
            StorePayloads = true,
            StoreTermVectors = true
        };

        using var writer = new IndexWriter(directory, config);

        var document = new LeanDocument();
        document.Add(new TextField(
            "body",
            "alpha beta",
            stored: true,
            boost: 2.0f,
            storeDocValues: true,
            indexOptions: FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets));
        document.Add(new TextField("not_stored", "hidden", stored: false));
        document.Add(new StringField(
            "tag",
            "alpha",
            stored: true,
            boost: 1.5f,
            docValues: StringDocValues.Sorted | StringDocValues.SortedSet | StringDocValues.Binary));
        document.Add(new StringField(
            "tag",
            "beta",
            stored: false,
            boost: 1.5f,
            docValues: StringDocValues.Sorted | StringDocValues.SortedSet | StringDocValues.Binary));
        document.Add(new StringField("no_dv", "exact", stored: false, boost: 1.0f, docValues: StringDocValues.None));
        document.Add(new NumericField("price", 12.5, stored: true, boost: 1.25f, storeDocValues: true));
        document.Add(new NumericField("price_no_dv", 8.5, stored: false, boost: 1.0f, storeDocValues: false));
        document.Add(new Int64Field("count", 42, stored: true, boost: 1.1f, storeDocValues: true));
        document.Add(new Int64Field("count_no_dv", 7, stored: false, boost: 1.0f, storeDocValues: false));
        document.Add(new VectorField("embedding", new float[] { 1.0f, 2.0f }, boost: 1.2f));
        document.Add(new VectorField("embedding", new float[] { 3.0f, 4.0f }, boost: 1.2f));
        document.Add(new GeoPointField("place", 51.5, -0.12, boost: 1.3f));
        document.Add(new StoredField("note", "hello"));
        document.Add(new BinaryField("blob", new byte[] { 0xCA, 0xFE }));
        document.Add(new InetAddressField("ip", IPAddress.Parse("192.0.2.1")));

        InvokeAddDocumentCore(writer, document);

        var secondDocument = new LeanDocument();
        secondDocument.Add(new TextField("body", "gamma", stored: false));
        InvokeAddDocumentCore(writer, secondDocument);

        var buffer = writer.Buffer;

        Assert.Equal(2, buffer.DocCount);
        Assert.Equal([0, 8], buffer.StoredDocStarts);
        Assert.Equal(
            ["body", "tag", "price", "count", "place", "note", "blob", "ip"],
            buffer.StoredFieldIdToName);
        Assert.Equal("alpha beta", buffer.StoredFieldValues[0].StringValue);
        Assert.Equal("alpha", buffer.StoredFieldValues[1].StringValue);
        Assert.Equal("12.5", buffer.StoredFieldValues[2].StringValue);
        Assert.Equal("42", buffer.StoredFieldValues[3].StringValue);
        Assert.Equal("51.5,-0.12", buffer.StoredFieldValues[4].StringValue);
        Assert.Equal("hello", buffer.StoredFieldValues[5].StringValue);
        Assert.Equal(new byte[] { 0xCA, 0xFE }, buffer.StoredFieldValues[6].BinaryValue);
        Assert.Equal(IPAddress.Parse("192.0.2.1").MapToIPv6().GetAddressBytes(), buffer.StoredFieldValues[7].BinaryValue);

        Assert.True(buffer.TryGetAccumulator("body\0alpha", out var alpha));
        Assert.True(buffer.TryGetAccumulator("body\0beta", out var beta));
        Assert.True(buffer.TryGetAccumulator("body\0gamma", out var gamma));
        Assert.Equal([0], alpha.GetPositions(0).ToArray());
        Assert.Equal([1], beta.GetPositions(0).ToArray());
        Assert.Equal([0], gamma.GetPositions(0).ToArray());
        Assert.Equal(new byte[] { (byte)'a' }, alpha.GetPayload(0, 0));
        Assert.Equal(new byte[] { (byte)'b' }, beta.GetPayload(0, 0));
        Assert.True(alpha.HasOffsets);
        var (starts, ends) = alpha.GetOffsets(0);
        Assert.Equal([0], starts!);
        Assert.Equal([5], ends!);

        Assert.True(buffer.TryGetAccumulator("tag\0alpha", out var tagAlpha));
        Assert.True(buffer.TryGetAccumulator("tag\0beta", out var tagBeta));
        Assert.Equal(0, tagAlpha.GetFreq(0));
        Assert.Empty(tagAlpha.GetPositions(0).ToArray());
        Assert.Equal(0, tagBeta.GetFreq(0));
        Assert.True(buffer.TryGetAccumulator("no_dv\0exact", out _));
        Assert.Contains("body", buffer.FieldNames);
        Assert.Contains("tag", buffer.FieldNames);
        Assert.Contains("no_dv", buffer.FieldNames);
        Assert.Equal(2, buffer.DocTokenCounts["body"][0]);
        Assert.Equal(1, buffer.DocTokenCounts["body"][1]);
        Assert.True(buffer.DocTokenCounts["body"].Length >= 2);

        Assert.Equal(12.5, buffer.NumericIndex["price"][0]);
        Assert.Equal(8.5, buffer.NumericIndex["price_no_dv"][0]);
        Assert.Equal(51.5, buffer.NumericIndex["place_lat"][0]);
        Assert.Equal(-0.12, buffer.NumericIndex["place_lon"][0]);
        Assert.Equal([12.5], buffer.NumericDocValues["price"]);
        Assert.Equal([12.5], buffer.SortedNumericDocValues["price"][0]);
        Assert.False(buffer.NumericDocValues.ContainsKey("price_no_dv"));

        Assert.Equal(42L, buffer.Int64Index["count"][0]);
        Assert.Equal(7L, buffer.Int64Index["count_no_dv"][0]);
        Assert.Equal([42L], buffer.Int64DocValues["count"]);
        Assert.Equal([42L], buffer.Int64SortedDocValues["count"][0]);
        Assert.False(buffer.Int64DocValues.ContainsKey("count_no_dv"));

        Assert.Equal([3.0f, 4.0f], buffer.Vectors["embedding"][0].ToArray());
        Assert.Equal("beta", buffer.SortedDocValues["tag"][0]);
        Assert.Equal(["alpha", "beta"], buffer.SortedSetDocValues["tag"][0]);
        Assert.Equal(
            ["alpha", "beta"],
            buffer.BinaryDocValues["tag"][0].Select(static value => System.Text.Encoding.UTF8.GetString(value)));
        Assert.False(buffer.BinaryDocValues.ContainsKey("body"));
        Assert.Equal("12.5", System.Text.Encoding.UTF8.GetString(buffer.BinaryDocValues["price"][0][0]));
        Assert.Equal("42", System.Text.Encoding.UTF8.GetString(buffer.BinaryDocValues["count"][0][0]));
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(buffer.BinaryDocValues["note"][0][0]));
        Assert.Equal(new byte[] { 0xCA, 0xFE }, buffer.BinaryDocValues["blob"][0][0]);
        Assert.Equal(IPAddress.Parse("192.0.2.1").MapToIPv6().GetAddressBytes(), buffer.BinaryDocValues["ip"][0][0]);

        Assert.Equal(2.0f, buffer.FieldBoosts["body"][0]);
        Assert.Equal(1.5f, buffer.FieldBoosts["tag"][0]);
        Assert.Equal(1.25f, buffer.FieldBoosts["price"][0]);
        Assert.Equal(1.1f, buffer.FieldBoosts["count"][0]);
        Assert.Equal(1.2f, buffer.FieldBoosts["embedding"][0]);
        Assert.Equal(1.3f, buffer.FieldBoosts["place"][0]);
    }

    [Fact(DisplayName = "IndexWriter field processing: applies char filters and caches analysers")]
    public void AddDocumentCore_AppliesCharFiltersAndCachesAnalysers()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentCore_AppliesCharFiltersAndCachesAnalysers)));
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            CharFilters = [new MappingCharFilter(new Dictionary<string, string> { ["-"] = " " })]
        };

        using var writer = new IndexWriter(directory, config);
        InvokeAddDocumentCore(writer, TextDocument("body", "alpha-beta"));
        InvokeAddDocumentCore(writer, TextDocument("body", "gamma-delta"));

        var buffer = writer.Buffer;
        Assert.True(buffer.TryGetAccumulator("body\0alpha", out _));
        Assert.True(buffer.TryGetAccumulator("body\0beta", out _));
        Assert.True(buffer.TryGetAccumulator("body\0gamma", out _));
        Assert.True(buffer.TryGetAccumulator("body\0delta", out _));
        Assert.False(buffer.TryGetAccumulator("body\0alpha-beta", out _));
        Assert.Equal(2, buffer.DocTokenCounts["body"][0]);
        Assert.Equal(2, buffer.DocTokenCounts["body"][1]);
    }

    [Fact(DisplayName = "IndexWriter field processing: truncates text at token budget")]
    public void AddDocumentCore_TruncatesTokensAtConfiguredBudget()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentCore_TruncatesTokensAtConfiguredBudget)));
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            MaxTokensPerDocument = 2,
            TokenBudgetPolicy = TokenBudgetPolicy.Truncate
        };

        using var writer = new IndexWriter(directory, config);
        InvokeAddDocumentCore(writer, TextDocument("body", "one two three"));

        Assert.True(writer.Buffer.TryGetAccumulator("body\0one", out _));
        Assert.True(writer.Buffer.TryGetAccumulator("body\0two", out _));
        Assert.False(writer.Buffer.TryGetAccumulator("body\0three", out _));
        Assert.Equal(2, writer.Buffer.DocTokenCounts["body"][0]);
    }

    [Fact(DisplayName = "IndexWriter field processing: warn budget keeps all tokens")]
    public void AddDocumentCore_WarnBudgetKeepsAllTokens()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentCore_WarnBudgetKeepsAllTokens)));
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            MaxTokensPerDocument = 2,
            TokenBudgetPolicy = TokenBudgetPolicy.Warn
        };

        using var writer = new IndexWriter(directory, config);
        InvokeAddDocumentCore(writer, TextDocument("body", "one two three"));

        Assert.True(writer.Buffer.TryGetAccumulator("body\0three", out _));
        Assert.Equal(3, writer.Buffer.DocTokenCounts["body"][0]);
    }

    [Fact(DisplayName = "IndexWriter field processing: rejects text over token budget")]
    public void AddDocumentCore_RejectsTextOverTokenBudget()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentCore_RejectsTextOverTokenBudget)));
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            MaxTokensPerDocument = 2,
            TokenBudgetPolicy = TokenBudgetPolicy.Reject
        };

        using var writer = new IndexWriter(directory, config);
        var exception = Assert.Throws<TokenBudgetExceededException>(
            () => InvokeAddDocumentCore(writer, TextDocument("body", "one two three")));

        Assert.Equal(3, exception.TokenCount);
        Assert.Equal(2, exception.Budget);
        Assert.Equal(0, writer.Buffer.DocCount);
    }

    [Fact(DisplayName = "IndexWriter field processing: handles zero position increments")]
    public void AddDocumentCore_HandlesZeroPositionIncrements()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentCore_HandlesZeroPositionIncrements)));
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new ZeroPositionIncrementAnalyser()
        };

        using var writer = new IndexWriter(directory, config);
        InvokeAddDocumentCore(writer, TextDocument("body", "one two"));

        Assert.True(writer.Buffer.TryGetAccumulator("body\0one", out var one));
        Assert.True(writer.Buffer.TryGetAccumulator("body\0two", out var two));
        Assert.Equal([0], one.GetPositions(0).ToArray());
        Assert.Equal([0], two.GetPositions(0).ToArray());
    }

    [Fact(DisplayName = "IndexWriter field processing: rejects conflicting field boosts")]
    public void AddDocumentCore_RejectsConflictingFieldBoosts()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentCore_RejectsConflictingFieldBoosts)));
        using var writer = new IndexWriter(directory, new IndexWriterConfig());

        var document = new LeanDocument();
        document.Add(new TextField("body", "one", stored: false, boost: 2.0f));
        document.Add(new TextField("body", "two", stored: false, boost: 3.0f));

        var exception = Assert.Throws<InvalidOperationException>(() => InvokeAddDocumentCore(writer, document));

        Assert.Contains("conflicting boosts", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2.0f, writer.Buffer.FieldBoosts["body"][0]);
        Assert.Equal(0, writer.Buffer.DocCount);
    }

    [Fact(DisplayName = "IndexWriter field processing: flushes at document threshold and tracks sequence range")]
    public void AddDocumentCore_FlushesAtDocumentThresholdAndTracksSequenceRange()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentCore_FlushesAtDocumentThresholdAndTracksSequenceRange)));
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            TrackSequenceNumbers = true,
            MergePolicy = NoMergePolicy.Instance
        };

        using var writer = new IndexWriter(directory, config);
        InvokeAddDocumentCore(writer, TextDocument("body", "one"), suppressFlush: false);

        var segment = Assert.Single(writer.CommittedSegments);
        Assert.Equal(1, segment.DocCount);
        Assert.Equal(0L, segment.MinSequenceNumber);
        Assert.Equal(0L, segment.MaxSequenceNumber);
        Assert.Equal(0, writer.Buffer.DocCount);
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static LeanDocument TextDocument(string fieldName, string value)
    {
        var document = new LeanDocument();
        document.Add(new TextField(fieldName, value, stored: false));
        return document;
    }

    private static void InvokeAddDocumentCore(IndexWriter writer, LeanDocument document, bool suppressFlush = true)
    {
        try
        {
            _ = AddDocumentCoreMethod.Invoke(writer, [document, suppressFlush]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    private sealed class PayloadWhitespaceAnalyser : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            int index = 0;
            while (index < input.Length)
            {
                while (index < input.Length && char.IsWhiteSpace(input[index]))
                    index++;

                if (index == input.Length)
                    break;

                int start = index;
                while (index < input.Length && !char.IsWhiteSpace(input[index]))
                    index++;

                sink.Add(
                    input.Slice(start, index - start),
                    start,
                    index,
                    payload: [(byte)input[start]]);
            }
        }
    }

    private sealed class ZeroPositionIncrementAnalyser : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            sink.Add(input.Slice(0, 3), 0, 3, positionIncrement: 0);
            sink.Add(input.Slice(4, 3), 4, 7, positionIncrement: 0);
        }
    }
}
