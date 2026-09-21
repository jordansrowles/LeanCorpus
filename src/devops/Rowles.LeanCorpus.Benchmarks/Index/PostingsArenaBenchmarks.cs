using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Indexer.Postings;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Exercises the DWPT postings store without directory or segment-output work for
/// most workloads, while also measuring one full high-cardinality IndexWriter flush.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[WarmupCount(2)]
[IterationCount(5)]
[InvocationCount(1)]
public class PostingsArenaBenchmarks
{
    private const int NormalDocumentCount = 20_000;
    private const int HighCardinalityDocumentCount = 10_000;
    private const int HighCardinalityTermsPerDocument = 20;
    private const int LowVocabularyDocumentCount = 50_000;
    private const int LowVocabularyTokensPerDocument = 64;
    private const int LowVocabularyTermCount = 128;
    private const int DocsOnlyDocumentCount = 100_000;
    private const int PayloadLength = 8;

    private string[] _normalDocuments = [];
    private string[] _highCardinalityTerms = [];
    private string[] _highCardinalityDocuments = [];
    private string[] _lowVocabulary = [];
    private EnglishAnalyser _normalAnalyser = null!;
    private PayloadAnalyser _payloadAnalyser = null!;
    private readonly StoreTokenSink _sink = new();
    private Diagnostics _lastDiagnostics;
    private Workload _lastWorkload;
    private string? _fullFlushPath;

    [GlobalSetup]
    public void Setup()
    {
        _normalDocuments = BenchmarkData.BuildDocuments(NormalDocumentCount);

        _highCardinalityTerms = new string[
            HighCardinalityDocumentCount * HighCardinalityTermsPerDocument];
        for (int document = 0; document < HighCardinalityDocumentCount; document++)
        {
            for (int slot = 0; slot < HighCardinalityTermsPerDocument; slot++)
            {
                int ordinal = document * HighCardinalityTermsPerDocument + slot;
                _highCardinalityTerms[ordinal] = $"high_{document:D5}_{slot:D2}";
            }
        }

        _highCardinalityDocuments = new string[HighCardinalityDocumentCount];
        for (int document = 0; document < HighCardinalityDocumentCount; document++)
        {
            int firstTerm = document * HighCardinalityTermsPerDocument;
            _highCardinalityDocuments[document] = string.Join(
                ' ', _highCardinalityTerms, firstTerm, HighCardinalityTermsPerDocument);
        }

        _lowVocabulary = new string[LowVocabularyTermCount];
        for (int i = 0; i < _lowVocabulary.Length; i++)
            _lowVocabulary[i] = $"low_{i:D3}";

        _normalAnalyser = new EnglishAnalyser();
        _payloadAnalyser = new PayloadAnalyser();
    }

    [IterationCleanup]
    public void ReportDiagnostics()
    {
        if (_lastDiagnostics.DocumentCount == 0)
            return;

        Console.WriteLine(
            "[postings-arena] " +
            $"workload={_lastWorkload} " +
            $"documents={_lastDiagnostics.DocumentCount:N0} " +
            $"uniqueTerms={_lastDiagnostics.UniqueTerms:N0} " +
            $"arenaBlocksRented={_lastDiagnostics.ArenaBlocksRented:N0} " +
            $"arenaBytesOwned={_lastDiagnostics.ArenaBytesOwned:N0} " +
            $"termStateCapacity={_lastDiagnostics.TermStateCapacity:N0} " +
            $"termHashAllocatedBytes={_lastDiagnostics.TermHashAllocatedBytes:N0}");
    }

    [Benchmark(Baseline = true, Description = "Normal text, positions")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NormalText() => RunNormalText();

    [Benchmark(Description = "High cardinality, 20 terms per document")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_HighCardinality() => RunHighCardinality();

    [Benchmark(Description = "Low vocabulary, 64 positional tokens")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_LowVocabulary() => RunLowVocabulary();

    [Benchmark(Description = "DocsOnly unique IDs")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_DocsOnly() => RunDocsOnly();

    [Benchmark(Description = "Normal text with term vectors")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_TermVectors() => RunTermVectors();

    [Benchmark(Description = "Positions with deterministic payloads")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Payloads() => RunPayloads();

    [IterationSetup(Target = nameof(LeanCorpus_HighCardinality_FullFlush))]
    public void SetupHighCardinalityFullFlush()
    {
        _fullFlushPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-full-flush-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_fullFlushPath);
    }

    [IterationCleanup(Target = nameof(LeanCorpus_HighCardinality_FullFlush))]
    public void CleanupHighCardinalityFullFlush()
    {
        if (_fullFlushPath is not null)
            BenchmarkHelpers.DeleteDirectory(_fullFlushPath);
        _fullFlushPath = null;
    }

    [Benchmark(Description = "High cardinality full IndexWriter flush")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_HighCardinality_FullFlush()
    {
        string path = _fullFlushPath ?? throw new InvalidOperationException("The full-flush benchmark directory was not prepared.");
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            IndexingConcurrency = 1,
            MaxBufferedDocs = HighCardinalityDocumentCount,
            RamBufferSizeMB = 1024,
            RamPerThreadHardLimitMB = 1024,
            DurableCommits = false,
            UseCompoundFile = false,
            StorePayloads = false,
            StoreTermVectors = false,
            MergePolicy = NoMergePolicy.Instance
        });

        for (int documentId = 0; documentId < _highCardinalityDocuments.Length; documentId++)
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", _highCardinalityDocuments[documentId]));
            writer.AddDocument(document);
        }
        writer.Commit();
        return _highCardinalityDocuments.Length;
    }

    private int RunNormalText()
    {
        using var store = new PostingsStore();
        for (int document = 0; document < _normalDocuments.Length; document++)
        {
            _sink.Reset(store, "body", document, FieldIndexOptions.DocsAndFreqsAndPositions);
            _normalAnalyser.Analyse(_normalDocuments[document].AsSpan(), _sink);
        }

        return Complete(store, Workload.NormalText, NormalDocumentCount);
    }

    private int RunHighCardinality()
    {
        using var store = new PostingsStore();
        for (int document = 0; document < HighCardinalityDocumentCount; document++)
        {
            int firstTerm = document * HighCardinalityTermsPerDocument;
            for (int slot = 0; slot < HighCardinalityTermsPerDocument; slot++)
            {
                store.Add(
                    "body",
                    _highCardinalityTerms[firstTerm + slot].AsSpan(),
                    document,
                    slot,
                    FieldIndexOptions.DocsAndFreqsAndPositions,
                    payload: null,
                    startOffset: slot * 8,
                    endOffset: slot * 8 + 7);
            }
        }

        return Complete(store, Workload.HighCardinality, HighCardinalityDocumentCount);
    }

    private int RunLowVocabulary()
    {
        using var store = new PostingsStore();
        for (int document = 0; document < LowVocabularyDocumentCount; document++)
        {
            for (int position = 0; position < LowVocabularyTokensPerDocument; position++)
            {
                string term = _lowVocabulary[(document * LowVocabularyTokensPerDocument + position) % LowVocabularyTermCount];
                store.Add(
                    "body",
                    term.AsSpan(),
                    document,
                    position,
                    FieldIndexOptions.DocsAndFreqsAndPositions,
                    payload: null,
                    startOffset: position * 4,
                    endOffset: position * 4 + term.Length);
            }
        }

        return Complete(store, Workload.LowVocabulary, LowVocabularyDocumentCount);
    }

    private int RunDocsOnly()
    {
        using var store = new PostingsStore();
        for (int document = 0; document < DocsOnlyDocumentCount; document++)
        {
            string term = $"id_{document:D6}";
            store.AddDocOnly("id", term.AsSpan(), document);
        }

        return Complete(store, Workload.DocsOnly, DocsOnlyDocumentCount);
    }

    private int RunTermVectors()
    {
        using var store = new PostingsStore(storeTermVectors: true);
        for (int document = 0; document < _normalDocuments.Length; document++)
        {
            _sink.Reset(
                store,
                "body",
                document,
                FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets);
            _normalAnalyser.Analyse(_normalDocuments[document].AsSpan(), _sink);
        }

        return Complete(store, Workload.TermVectors, NormalDocumentCount);
    }

    private int RunPayloads()
    {
        using var store = new PostingsStore(storePayloads: true);
        for (int document = 0; document < _normalDocuments.Length; document++)
        {
            _sink.Reset(store, "body", document, FieldIndexOptions.DocsAndFreqsAndPositions);
            _payloadAnalyser.Analyse(_normalDocuments[document].AsSpan(), _sink);
        }

        return Complete(store, Workload.Payloads, NormalDocumentCount);
    }

    private int Complete(PostingsStore store, Workload workload, int documentCount)
    {
        store.Freeze();
        _lastWorkload = workload;
        _lastDiagnostics = new Diagnostics(
            documentCount,
            store.TermCount,
            store.Arena.BlockCount,
            store.Arena.AllocatedBytes,
            store.TermStateCapacity,
            store.TermHash.AllocatedBytes);
        return documentCount;
    }

    private enum Workload
    {
        NormalText,
        HighCardinality,
        LowVocabulary,
        DocsOnly,
        TermVectors,
        Payloads,
    }

    private readonly record struct Diagnostics(
        int DocumentCount,
        int UniqueTerms,
        int ArenaBlocksRented,
        long ArenaBytesOwned,
        int TermStateCapacity,
        long TermHashAllocatedBytes);

    private sealed class StoreTokenSink : ISpanTokenSink
    {
        private PostingsStore _store = null!;
        private string _fieldName = string.Empty;
        private FieldIndexOptions _indexOptions;
        private int _docId;
        private int _position;

        internal void Reset(PostingsStore store, string fieldName, int docId, FieldIndexOptions indexOptions)
        {
            _store = store;
            _fieldName = fieldName;
            _docId = docId;
            _indexOptions = indexOptions;
            _position = -1;
        }

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            _position = checked(_position + positionIncrement);
            _store.Add(
                _fieldName,
                text,
                _docId,
                _position,
                _indexOptions,
                payload,
                startOffset,
                endOffset);
        }

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type,
            int positionIncrement,
            int positionLength,
            byte[]? payload)
            => Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }

    private sealed class PayloadAnalyser : IThreadLocalAnalyser
    {
        private static readonly byte[] Payload = [0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38];

        public IAnalyser CreateThreadLocalAnalyser() => new PayloadAnalyser();

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            int tokenIndex = 0;
            int i = 0;
            while (i < input.Length)
            {
                while (i < input.Length && char.IsWhiteSpace(input[i]))
                    i++;
                if (i >= input.Length)
                    break;

                int start = i;
                while (i < input.Length && !char.IsWhiteSpace(input[i]))
                    i++;

                byte[]? payload = tokenIndex++ % 4 == 3 ? Payload : null;
                sink.Add(input[start..i], start, i, Token.DefaultType, 1, payload);
            }
        }
    }
}
