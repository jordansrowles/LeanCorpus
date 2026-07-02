using Rowles.LeanCorpus.Example.NativeAot;
using System.Linq;
using System.Text;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Linq;
using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

var rootPath = Path.Combine(Path.GetTempPath(), "leancorpus-aot-smoke-" + Guid.NewGuid().ToString("N"));

try
{
    // --- Phase 1: Standalone analysis pipeline (no index required) ---
    RunAnalysisSmoke();

    // --- Phase 2: Index, search, query, highlight, sort, delete, backup ---
    RunPolicy(FieldCompressionPolicy.None, rootPath);
    RunPolicy(FieldCompressionPolicy.Deflate, rootPath);
    RunPolicy(FieldCompressionPolicy.Brotli, rootPath);

    Directory.Delete(rootPath, recursive: true);
    Console.WriteLine("LeanCorpus Native AOT smoke passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

// =========================================================================
// Phase 1: Analysis pipeline — exercises every AOT-sensitive code path
// =========================================================================

static void RunAnalysisSmoke()
{
    RunTokeniserSmoke();
    RunFilterSmoke();
    RunStemmerSmoke();
    RunAnalyserFactorySmoke();
    RunCustomAnalyserSmoke();

    Console.WriteLine("  Analysis pipeline smoke passed.");
}

static void RunTokeniserSmoke()
{
    // PatternTokeniser — regex-based, AOT-critical (now interpreter mode)
    {
        var tokeniser = new PatternTokeniser(@"\S+");
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("the quick brown fox", sink);
        Assert(sink.Count == 4, $"PatternTokeniser \\S+ expected 4 tokens, got {sink.Count}");
    }

    // PatternTokeniser with pre-compiled Regex (constructor 2)
    {
        var regex = new System.Text.RegularExpressions.Regex(@"[^,]+",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var tokeniser = new PatternTokeniser(regex);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("alpha,beta,gamma", sink);
        Assert(sink.Count == 3, $"PatternTokeniser(Regex) expected 3 tokens, got {sink.Count}");
    }

    // CJKBigramTokeniser
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("\u4F60\u597D\u4E16\u754C", sink);
        Assert(sink.Count >= 2, $"CJKBigramTokeniser expected >=2 bigrams, got {sink.Count}");
    }

    // EdgeNGramTokeniser
    {
        var tokeniser = new EdgeNGramTokeniser(minGram: 2, maxGram: 4);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("hello", sink);
        Assert(sink.Count >= 2, $"EdgeNGramTokeniser expected >=2 grams, got {sink.Count}");
    }

    // NGramTokeniser
    {
        var tokeniser = new NGramTokeniser(minGram: 2, maxGram: 3);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("abcd", sink);
        Assert(sink.Count >= 3, $"NGramTokeniser expected >=3 grams, got {sink.Count}");
    }

    // WhitespaceTokeniser
    {
        var tokeniser = new WhitespaceTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise(" a  b c ", sink);
        Assert(sink.Count == 3, $"WhitespaceTokeniser expected 3 tokens, got {sink.Count}");
    }

    // LetterTokeniser
    {
        var tokeniser = new LetterTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("hello, world!", sink);
        Assert(sink.Count == 2, $"LetterTokeniser expected 2 tokens, got {sink.Count}");
    }

    // Tokeniser (standard Unicode word-boundary)
    {
        var tokeniser = new Tokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("Hello, world! This is a test.", sink);
        Assert(sink.Count >= 5, $"Tokeniser expected >=5 tokens, got {sink.Count}");
    }
}

static void RunFilterSmoke()
{
    var sink = new CountingTokenSink();

    // PatternReplaceFilter — regex-based, AOT-critical (now interpreter mode)
    {
        var filter = new PatternReplaceFilter("[0-9]+", "#");
        var localSink = new MaterialisingTokenSink();
        filter.Apply("call12345now", 0, 12, Token.DefaultType, 1, null, localSink);
        Assert(localSink.Tokens.Count == 1 && localSink.Tokens[0].Text == "call#now",
            $"PatternReplaceFilter expected 'call#now', got '{(localSink.Tokens.Count > 0 ? localSink.Tokens[0].Text : "null")}'");
    }

    // PatternReplaceFilter with pre-compiled Regex (constructor 2)
    {
        var regex = new System.Text.RegularExpressions.Regex(@"\s+",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var filter = new PatternReplaceFilter(regex, "-");
        var localSink = new MaterialisingTokenSink();
        filter.Apply("hello world", 0, 11, Token.DefaultType, 1, null, localSink);
        Assert(localSink.Tokens.Count == 1 && localSink.Tokens[0].Text == "hello-world",
            "PatternReplaceFilter(Regex) expected 'hello-world'");
    }

    // PatternReplaceCharFilter — regex-based
    {
        var filter = new PatternReplaceCharFilter(@"\d+", "#");
        var result = filter.Filter("abc123def456".AsSpan());
        Assert(result == "abc#def#",
            $"PatternReplaceCharFilter expected 'abc#def#', got '{result}'");
    }

    // SynonymMap + SynonymGraphFilter
    {
        var map = new SynonymMap();
        map.Add("quick brown", ["fast", "brown"]);
        map.Add("lazy", ["idle"]);
        var analyser = new Analyser(
            new Tokeniser(),
            new LowercaseFilter(),
            new SynonymGraphFilter(map));
        var localSink = new CapturingTokenSink();
        analyser.Analyse("the quick brown fox", localSink);
        Assert(localSink.Count >= 3, $"SynonymGraphFilter expected >=3 tokens, got {localSink.Count}");
    }

    // HunspellDictionary + HunspellStemFilter
    {
        var aff = "SET UTF-8\nTRY abcdefghijklmnopqrstuvwxyz\n\nPFX A Y 1\nPFX A 0 re .\n\nSFX B Y 1\nSFX B 0 ing .\n";
        var dic = "3\nrun\nwalk\njump\n";
        var dict = HunspellDictionary.Parse(aff, dic, maxGeneratedFormsPerEntry: 64);
        Assert(dict is not null, "HunspellDictionary.Parse returned null");

        var filter = new HunspellStemFilter(dict!);
        sink.Reset();
        filter.Apply("running".AsSpan(), 0, 7, Token.DefaultType, 1, null, sink);
        Assert(sink.Count >= 1, $"HunspellStemFilter expected >=1 stem, got {sink.Count}");
    }

    // ShingleFilter
    {
        var shingle = new ShingleFilter(minShingleSize: 2, maxShingleSize: 3, outputUnigrams: false);
        sink.Reset();
        shingle.Apply("the".AsSpan(), 0, 3, Token.DefaultType, 1, null, sink);
        shingle.Apply("quick".AsSpan(), 4, 9, Token.DefaultType, 1, null, sink);
        shingle.Apply("brown".AsSpan(), 10, 15, Token.DefaultType, 1, null, sink);
        ((ISpanTokenFilter)shingle).Finish(sink);
        Assert(sink.Count >= 2, $"ShingleFilter expected >=2 shingles, got {sink.Count}");
    }

    // WordDelimiterFilter
    {
        var wdf = new WordDelimiterFilter();
        sink.Reset();
        wdf.Apply("Wi-Fi".AsSpan(), 0, 5, Token.DefaultType, 1, null, sink);
        Assert(sink.Count >= 1, $"WordDelimiterFilter expected >=1 token, got {sink.Count}");
    }

    // LowercaseFilter
    {
        var lc = new LowercaseFilter();
        sink.Reset();
        lc.Apply("HELLO".AsSpan(), 0, 5, Token.DefaultType, 1, null, sink);
        Assert(sink.Count >= 1, $"LowercaseFilter expected >=1 token, got {sink.Count}");
    }

    // ClassicFilter
    {
        var cf = new ClassicFilter();
        sink.Reset();
        cf.Apply("Wi-Fi.".AsSpan(), 0, 6, Token.DefaultType, 1, null, sink);
        Assert(sink.Count >= 1, $"ClassicFilter expected >=1 token, got {sink.Count}");
    }

    // StopWordFilter
    {
        var sw = new StopWordFilter(StopWords.English);
        sink.Reset();
        sw.Apply("the".AsSpan(), 0, 3, Token.DefaultType, 1, null, sink);
        Assert(sink.Count == 0, $"StopWordFilter expected 0 tokens for 'the', got {sink.Count}");
    }

    // CommonGramsFilter
    {
        var words = System.Collections.Frozen.FrozenSet.ToFrozenSet(["of", "in"]);
        var cg = new CommonGramsFilter(words);
        sink.Reset();
        cg.Apply("king".AsSpan(), 0, 4, Token.DefaultType, 1, null, sink);
        cg.Apply("of".AsSpan(), 5, 7, Token.DefaultType, 1, null, sink);
        cg.Finish(sink);
        Assert(sink.Count >= 1, $"CommonGramsFilter expected >=1 token, got {sink.Count}");
    }

    // KeepWordFilter
    {
        var words = System.Collections.Frozen.FrozenSet.ToFrozenSet(["keep"]);
        var kw = new KeepWordFilter(words);
        sink.Reset();
        kw.Apply("keep".AsSpan(), 0, 4, Token.DefaultType, 1, null, sink);
        kw.Apply("drop".AsSpan(), 0, 4, Token.DefaultType, 1, null, sink);
        Assert(sink.Count == 1, $"KeepWordFilter expected 1 token, got {sink.Count}");
    }

    // LengthFilter
    {
        var lf = new LengthFilter(minLength: 3, maxLength: 5);
        sink.Reset();
        lf.Apply("hi".AsSpan(), 0, 2, Token.DefaultType, 1, null, sink);
        lf.Apply("hello".AsSpan(), 0, 5, Token.DefaultType, 1, null, sink);
        lf.Apply("greetings".AsSpan(), 0, 9, Token.DefaultType, 1, null, sink);
        Assert(sink.Count == 1, $"LengthFilter expected 1 token, got {sink.Count}");
    }

    // LimitTokenCountFilter
    {
        var lt = new LimitTokenCountFilter(maxTokenCount: 2);
        sink.Reset();
        lt.Apply("a".AsSpan(), 0, 1, Token.DefaultType, 1, null, sink);
        lt.Apply("b".AsSpan(), 0, 1, Token.DefaultType, 1, null, sink);
        lt.Apply("c".AsSpan(), 0, 1, Token.DefaultType, 1, null, sink);
        Assert(sink.Count == 2, $"LimitTokenCountFilter expected 2 tokens, got {sink.Count}");
    }

    // ReverseStringFilter
    {
        var rs = new ReverseStringFilter();
        sink.Reset();
        rs.Apply("abc".AsSpan(), 0, 3, Token.DefaultType, 1, null, sink);
        Assert(sink.Count >= 1, $"ReverseStringFilter expected >=1 token, got {sink.Count}");
    }

    // ElisionFilter
    {
        var elision = new ElisionFilter(["l'", "d'"]);
        sink.Reset();
        elision.Apply("l'avion".AsSpan(), 0, 7, Token.DefaultType, 1, null, sink);
        Assert(sink.Count == 1, $"ElisionFilter expected 1 token, got {sink.Count}");
    }

    // KeywordMarkerFilter
    {
        var km = new KeywordMarkerFilter(["important"]);
        sink.Reset();
        km.Apply("important".AsSpan(), 0, 9, Token.DefaultType, 1, null, sink);
        Assert(sink.Count == 1, $"KeywordMarkerFilter expected 1 token, got {sink.Count}");
    }

    // TruncateTokenFilter
    {
        var tt = new TruncateTokenFilter(3);
        sink.Reset();
        tt.Apply("abcdef".AsSpan(), 0, 6, Token.DefaultType, 1, null, sink);
        Assert(sink.Count == 1, $"TruncateTokenFilter expected 1 token, got {sink.Count}");
    }

    // TypeTokenFilter
    {
        var tt = new TypeTokenFilter(["keepme"]);
        sink.Reset();
        tt.Apply("text".AsSpan(), 0, 4, "keepme", 1, null, sink);
        tt.Apply("text".AsSpan(), 0, 4, "dropme", 1, null, sink);
        Assert(sink.Count == 1, $"TypeTokenFilter expected 1 token, got {sink.Count}");
    }

    // FlattenGraphFilter
    {
        var fg = new FlattenGraphFilter();
        sink.Reset();
        fg.Apply("a".AsSpan(), 0, 1, Token.DefaultType, 1, null, sink);
        fg.Apply("b".AsSpan(), 0, 1, Token.DefaultType, 0, null, sink);
        Assert(sink.Count >= 1, $"FlattenGraphFilter expected >=1 token, got {sink.Count}");
    }

    // HyphenatedWordsFilter — stateful, needs multi-token + position=0. Smoke constructor only.
    {
        var hw = new HyphenatedWordsFilter();
        Assert(hw is not null, "HyphenatedWordsFilter constructor failed");
    }

    // DecimalDigitFilter
    {
        var dd = new DecimalDigitFilter();
        sink.Reset();
        dd.Apply("\u0661\u0662\u0663".AsSpan(), 0, 3, Token.DefaultType, 1, null, sink);
        Assert(sink.Count >= 1, $"DecimalDigitFilter expected >=1 token, got {sink.Count}");
    }

    // AccentFoldingFilter
    {
        var af = new AccentFoldingFilter();
        sink.Reset();
        af.Apply("caf\u00E9".AsSpan(), 0, 4, Token.DefaultType, 1, null, sink);
        Assert(sink.Count >= 1, $"AccentFoldingFilter expected >=1 token, got {sink.Count}");
    }

    // StemTokenFilter wrapping an ISpanStemmer
    {
        var stemmer = new EnglishStemmer();
        var stf = new StemTokenFilter(stemmer);
        sink.Reset();
        stf.Apply("running".AsSpan(), 0, 7, Token.DefaultType, 1, null, sink);
        Assert(sink.Count >= 1, $"StemTokenFilter(EnglishStemmer) expected >=1 token, got {sink.Count}");
    }

    // MetaphoneFilter
    {
        var mf = new MetaphoneFilter();
        sink.Reset();
        mf.Apply("smith".AsSpan(), 0, 5, Token.DefaultType, 1, null, sink);
        Assert(sink.Count >= 1, $"MetaphoneFilter expected >=1 token, got {sink.Count}");
    }

    // PhoneticAlternatesFilter
    {
        var pa = new PhoneticAlternatesFilter(maxExpansions: 3);
        sink.Reset();
        pa.Apply("meier".AsSpan(), 0, 5, Token.DefaultType, 1, null, sink);
        Assert(sink.Count >= 1, $"PhoneticAlternatesFilter expected >=1 token, got {sink.Count}");
    }

    // CachingTokenFilter (parameterless — captures tokens after Analyse)
    {
        var caching = new CachingTokenFilter();
        var analyser = new Analyser(new Tokeniser(), new LowercaseFilter(), caching);
        var localSink = new CapturingTokenSink();
        analyser.Analyse("HELLO WORLD", localSink);
        Assert(caching.Tokens.Count >= 2, $"CachingTokenFilter expected >=2 tokens, got {caching.Tokens.Count}");
    }
}

static void RunStemmerSmoke()
{
    // KStemLexicon — from in-memory words
    {
        var lexicon = KStemLexicon.From(["run", "walk", "jump", "swim"]);
        Assert(lexicon.Contains("walk"), "KStemLexicon should contain 'walk'");
        Assert(!lexicon.Contains("flying"), "KStemLexicon should not contain 'flying'");
        Assert(lexicon.ContainsPreLowered("run"), "KStemLexicon.ContainsPreLowered should find 'run'");
        Assert(lexicon.Contains("JUMP".AsSpan()), "KStemLexicon span lookup should find 'JUMP'");
    }

    // KStemmer via StemTokenFilter
    {
        var lexicon = KStemLexicon.From(["running", "walk", "walked", "jumped", "jump"]);
        var kstemmer = new KStemmer(lexicon);
        var filter = new StemTokenFilter(kstemmer);
        var sink = new CountingTokenSink();
        filter.Apply("running".AsSpan(), 0, 7, Token.DefaultType, 1, null, sink);
        Assert(sink.Count >= 1, $"KStemmer expected >=1 token, got {sink.Count}");
    }

    // PorterStemmerFilter
    {
        var porter = new PorterStemmerFilter();
        var sink = new CountingTokenSink();
        porter.Apply("running".AsSpan(), 0, 7, Token.DefaultType, 1, null, sink);
        Assert(sink.Count >= 1, $"PorterStemmerFilter expected >=1 token, got {sink.Count}");
    }

    // ISpanStemmer implementations — exercise directly
    Span<char> buffer = stackalloc char[32];
    foreach (var stemmer in new ISpanStemmer[]
    {
        new EnglishStemmer(),
        new FrenchStemmer(),
        new GermanStemmer(),
        new SpanishStemmer(),
        new ItalianStemmer(),
        new PortugueseStemmer(),
        new DutchStemmer(),
        new RussianStemmer(),
        new ArabicStemmer(),
    })
    {
        "test".AsSpan().CopyTo(buffer[..4]);
        int result = stemmer.Stem(buffer[..4], buffer);
        Assert(result >= 1 || result == -1,
            $"{stemmer.GetType().Name}.Stem expected >=1 or -1, got {result}");
        // -1 means output buffer too small (word didn't change size)
    }
}

static void RunAnalyserFactorySmoke()
{
    // Exercise every language — validates the switch-based dispatch (AOT-safe)
    foreach (var lang in AnalyserFactory.SupportedLanguages)
    {
        try
        {
            var analyser = AnalyserFactory.Create(lang);
            Assert(analyser is not null, $"AnalyserFactory.Create('{lang}') returned null");
        }
        catch (NotSupportedException)
        {
            Assert(false, $"AnalyserFactory.Create('{lang}') threw NotSupportedException unexpectedly");
        }
    }
}

static void RunCustomAnalyserSmoke()
{
    // StandardAnalyser
    {
        var analyser = new StandardAnalyser();
        var sink = new CapturingTokenSink();
        analyser.Analyse("The quick brown fox jumps over the lazy dog.", sink);
        Assert(sink.Count >= 5, $"StandardAnalyser expected >=5 tokens, got {sink.Count}");
    }

    // SimpleAnalyser
    {
        var analyser = new SimpleAnalyser();
        var sink = new CapturingTokenSink();
        analyser.Analyse("Hello World", sink);
        Assert(sink.Count >= 2, $"SimpleAnalyser expected >=2 tokens, got {sink.Count}");
    }

    // WhitespaceAnalyser
    {
        var analyser = new WhitespaceAnalyser();
        var sink = new CapturingTokenSink();
        analyser.Analyse("hello  world", sink);
        Assert(sink.Count == 2, $"WhitespaceAnalyser expected 2 tokens, got {sink.Count}");
    }

    // KeywordAnalyser
    {
        var analyser = new KeywordAnalyser();
        var sink = new CapturingTokenSink();
        analyser.Analyse("entire field as one token", sink);
        Assert(sink.Count == 1, $"KeywordAnalyser expected 1 token, got {sink.Count}");
    }

    // StemmedAnalyser
    {
        var analyser = new StemmedAnalyser();
        var sink = new CapturingTokenSink();
        analyser.Analyse("running jumped walking", sink);
        Assert(sink.Count >= 3, $"StemmedAnalyser expected >=3 tokens, got {sink.Count}");
    }

    // StemmerAnalyser
    {
        var analyser = new StemmerAnalyser(new EnglishStemmer());
        var sink = new CapturingTokenSink();
        analyser.Analyse("cats dogs running", sink);
        Assert(sink.Count >= 3, $"StemmerAnalyser expected >=3 tokens, got {sink.Count}");
    }

    // LanguageAnalyser (English + stemming)
    {
        var analyser = new LanguageAnalyser(
            new Tokeniser(),
            StopWords.English,
            new EnglishStemmer());
        var sink = new CapturingTokenSink();
        analyser.Analyse("The cats were running quickly", sink);
        Assert(sink.Count >= 3, $"LanguageAnalyser(en) expected >=3 tokens, got {sink.Count}");
    }

    // LanguageAnalyser (CJK)
    {
        var analyser = new LanguageAnalyser(
            new CJKBigramTokeniser(),
            StopWords.Chinese,
            stemmer: null);
        var sink = new CapturingTokenSink();
        analyser.Analyse("\u4F60\u597D\u4E16\u754C", sink);
        Assert(sink.Count >= 2, $"LanguageAnalyser(zh) expected >=2 tokens, got {sink.Count}");
    }

    // IcuAnalyser
    {
        var analyser = new IcuAnalyser();
        var sink = new CapturingTokenSink();
        analyser.Analyse("Hello world — testing ICU tokenisation.", sink);
        Assert(sink.Count >= 3, $"IcuAnalyser expected >=3 tokens, got {sink.Count}");
    }
}

// =========================================================================
// Phase 2: Full index/search cycle
// =========================================================================

static void RunPolicy(FieldCompressionPolicy policy, string rootPath)
{
    var indexPath = Path.Combine(rootPath, policy.ToString());

    try
    {
        Directory.CreateDirectory(indexPath);
        using var directory = new MMapDirectory(indexPath);

        // --- Indexing ---
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            CompressionPolicy = policy,
            MaxBufferedDocs = 2,
            RamBufferSizeMB = 1,
            SoftDeletesEnabled = true,
        }))
        {
            foreach (var document in BuildDocuments())
                writer.AddDocument(document);
            writer.Commit();

            // Soft-delete then hard-commit to exercise deletion path
            writer.SoftDeleteDocuments(new TermQuery("status", "archived"));
            writer.Commit();

            // Re-add a document
            writer.AddDocument(Document(
                id: "doc-4b",
                title: "recovered archive",
                body: "re-added after deletion",
                code: "recover",
                status: "archived",
                category: "archive",
                year: 2021,
                latitude: 48.8566,
                longitude: 2.3522,
                embedding: [0f, 0f, 0.9f, 0f]));
            writer.Commit();
        }

        // Re-open for search
        var analyticsPath = Path.Combine(indexPath, "search-analytics.json");
        var slowLogPath = Path.Combine(indexPath, "slow-queries.jsonl");
        var analytics = new SearchAnalytics(capacity: 64);

        using (var slowLog = SlowQueryLog.ToFile(thresholdMs: 0, slowLogPath))
        {
            using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
            {
                SearchAnalytics = analytics,
                SlowQueryLog = slowLog,
            });

            // --- Core queries ---
            AssertHits(searcher.Search(new TermQuery("status", "active"), 10), 3, "term query");
            AssertHits(searcher.Search(new PhraseQuery("title", "native", "aot"), 10), 1, "phrase query");
            AssertHits(searcher.Search(new PrefixQuery("title", "nat"), 10), 1, "prefix query");
            AssertHits(searcher.Search(new RegexpQuery("code", "^nat"), 10), 1, "regexp query");
            AssertHits(searcher.Search(new RangeQuery("year", 2024, 2026), 10), 2, "numeric range query");
            AssertHits(searcher.Search(new GeoDistanceQuery("location", 51.5074, -0.1278, 250_000), 10), 2, "geo distance query");
            AssertHits(searcher.Search(new GeoBoundingBoxQuery("location", 51.0, 52.0, -3.0, 0.5), 10), 2, "geo bounding-box query");
            var vectorResults = searcher.Search(new VectorQuery("embedding", [1f, 0f, 0f, 0f], topK: 2), 2);
            Assert(vectorResults.ScoreDocs.Length == 2, $"vector query returned {vectorResults.ScoreDocs.Length} scored document(s), expected 2.");

            // --- New: WildcardQuery ---
            AssertHits(searcher.Search(new WildcardQuery("title", "corp*"), 10), 1, "wildcard query");

            // --- New: FuzzyQuery ---
            AssertHits(searcher.Search(new FuzzyQuery("title", "nativ", maxEdits: 1), 10), 1, "fuzzy query");

            // --- New: BooleanQuery (manual construction) ---
            {
                var bq = new BooleanQuery.Builder()
                    .Add(new TermQuery("status", "active"), Occur.Must)
                    .Add(new TermQuery("status", "archived"), Occur.MustNot)
                    .Build();
                AssertHits(searcher.Search(bq, 10), 3, "boolean query must+must_not");
            }

            // --- New: BooleanQuery with SHOULD ---
            {
                var bq = new BooleanQuery.Builder()
                    .Add(new TermQuery("status", "active"), Occur.Should)
                    .Add(new TermQuery("status", "archived"), Occur.Should)
                    .Build();
                AssertHits(searcher.Search(bq, 10), 4, "boolean query should+should");
            }

            // --- New: QueryParser (string-based query parsing) ---
            {
                var parser = new QueryParser("title", new StandardAnalyser());
                var parsed = parser.Parse("native aot");
                var result = searcher.Search(parsed, 10);
                Assert(result.TotalHits >= 1, $"query parser 'native aot' returned {result.TotalHits} hit(s), expected >=1.");
            }

            // --- New: QueryParser with field prefix ---
            {
                var parser = new QueryParser("title", new StandardAnalyser());
                var parsed = parser.Parse("status:active");
                var result = searcher.Search(parsed, 10);
                Assert(result.TotalHits >= 3, $"query parser 'status:active' returned {result.TotalHits} hit(s), expected >=3.");
            }

            // --- New: QueryParser with phrase ---
            {
                var parser = new QueryParser("title", new StandardAnalyser());
                var parsed = parser.Parse("\"native aot\"");
                var result = searcher.Search(parsed, 10);
                Assert(result.TotalHits >= 1, $"query parser '\"native aot\"' returned {result.TotalHits} hit(s), expected >=1.");
            }

            // --- New: TermInSetQuery ---
            {
                var q = new TermInSetQuery("status", ["active", "archived"]);
                AssertHits(searcher.Search(q, 10), 4, "term in set query");
            }

            // --- New: MatchAllDocsQuery ---
            {
                var q = new MatchAllDocsQuery();
                AssertHits(searcher.Search(q, 10), 4, "match all docs query");
            }

            // --- New: Sort (numeric sort) ---
            {
                var sorted = searcher.Search(new TermQuery("status", "active"), 10, SortField.Numeric("year", descending: true));
                Assert(sorted.ScoreDocs.Length >= 1, $"numeric sort returned {sorted.ScoreDocs.Length} doc(s), expected >=1.");
            }

            // --- New: Sort (doc ID) ---
            {
                var sorted = searcher.Search(new MatchAllDocsQuery(), 10, SortField.DocId);
                Assert(sorted.ScoreDocs.Length >= 1, $"docid sort returned {sorted.ScoreDocs.Length} doc(s), expected >=1.");
            }

            // --- New: Sort (score) ---
            {
                var sorted = searcher.Search(new MatchAllDocsQuery(), 10, SortField.Score);
                Assert(sorted.ScoreDocs.Length >= 1, $"score sort returned {sorted.ScoreDocs.Length} doc(s), expected >=1.");
            }

            // --- Collapse search ---
            var collapsed = searcher.SearchWithCollapse(new TermQuery("status", "active"), 10, new CollapseField("category"));
            Assert(collapsed.TotalHits == 2, $"collapsed search returned {collapsed.TotalHits} group(s), expected 2.");

            // --- Stored fields ---
            var storedResult = searcher.Search(new TermQuery("id", "doc-1"), 1);
            AssertHits(storedResult, 1, "stored-field lookup");
            var stored = searcher.GetStoredFields(storedResult.ScoreDocs[0].DocId);
            Assert(stored.TryGetValue("title", out var title) && title.Contains("native aot search"),
                "stored field 'title' was not readable after reopening the index.");
            Assert(stored.TryGetValue("location", out var location) && location.Contains("51.5074,-0.1278"),
                "stored geo field was not readable after reopening the index.");

            // --- New: Highlighter ---
            {
                var highlighter = new Highlighter("<b>", "</b>");
                var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "native", "aot" };
                var snippet = highlighter.GetBestFragment(
                    "LeanCorpus supports native AOT publishing for compact binaries.",
                    terms,
                    maxSnippetLength: 200);
                Assert(snippet.Contains("<b>") || snippet.Contains("native"),
                    $"Highlighter snippet doesn't contain expected content: '{snippet}'");
            }

            // --- LINQ queryable (existing, kept intact) ---
            var resolver = (string name) => name switch
            {
                "Title"       => (IFieldDescriptor?)NativeSmokeFields.Title,
                "Year"        => NativeSmokeFields.Year,
                "Status"      => NativeSmokeFields.Status,
                "IsPublished" => NativeSmokeFields.IsPublished,
                _ => null,
            };
            var map = new NativeSmokeDocMap();
            var queryable = map.AsQueryable(searcher, resolver);

            var whereAnd = queryable.Where(d => d.Status == "active" && d.Year > 2023).ToList();
            Assert(whereAnd.Count == 2, $"Where && returned {whereAnd.Count}, expected 2.");

            var whereOr = queryable.Where(d => d.Status == "archived" || d.Status == "active").ToList();
            Assert(whereOr.Count == 4, $"Where || returned {whereOr.Count}, expected 4.");

            var first = queryable.Where(d => d.Status == "archived").First();
            Assert(first.Year == 2021, $"First returned year {first.Year}, expected 2021.");
            var firstDef = queryable.Where(d => d.Status == "nonexistent").FirstOrDefault();
            Assert(firstDef is null, "FirstOrDefault should return null for empty results.");

            var single = queryable.Where(d => d.Status == "archived").Single();
            Assert(single.Year == 2021, $"Single returned year {single.Year}, expected 2021.");
            var singleDef = queryable.Where(d => d.Status == "nonexistent").SingleOrDefault();
            Assert(singleDef is null, "SingleOrDefault should return null for empty results.");

            var count = queryable.Where(d => d.Status == "active").Count();
            Assert(count == 3, $"Count returned {count}, expected 3.");
            var countAll = queryable.Count();
            Assert(countAll == 4, $"Count (all) returned {countAll}, expected 4.");
            Assert(queryable.Where(d => d.Status == "active").Any(), "Any should return true.");
            Assert(!queryable.Where(d => d.Status == "nonexistent").Any(), "Any should return false for no matches.");

            var take = queryable.Where(d => d.Status == "active").Take(2).ToList();
            Assert(take.Count == 2, $"Take(2) returned {take.Count}, expected 2.");
            var skip = queryable.Where(d => d.Status == "active").Skip(1).ToList();
            Assert(skip.Count == 2, $"Skip(1) returned {skip.Count}, expected 2.");

            var titles = queryable.Where(d => d.Year >= 2024).Select(d => d.Title).ToList();
            Assert(titles.Count == 2, $"Select projection returned {titles.Count}, expected 2.");

            var projectedTake = queryable.Where(d => d.Status == "active").Select(d => d.Title).Take(1).ToList();
            Assert(projectedTake.Count == 1, $"Select+Take returned {projectedTake.Count}, expected 1.");

            var projectedSkip = queryable.Where(d => d.Status == "active").Select(d => d.Title).Skip(1).ToList();
            Assert(projectedSkip.Count == 2, $"Select+Skip returned {projectedSkip.Count}, expected 2.");

            var published = queryable.Where(d => d.IsPublished).ToList();
            Assert(published.Count == 4, $"IsPublished returned {published.Count}, expected 4.");

            var starts = queryable.Where(d => d.Title!.StartsWith("native")).ToList();
            Assert(starts.Count == 1, $"StartsWith returned {starts.Count}, expected 1.");
            var ends = queryable.Where(d => d.Title!.EndsWith("indexing")).ToList();
            Assert(ends.Count == 1, $"EndsWith returned {ends.Count}, expected 1.");

            var captured = queryable.Where(d => d.Status == "active");
            var recaptured = captured.Where(d => d.Year > 2023).ToList();
            Assert(recaptured.Count == 2, $"Captured+Where returned {recaptured.Count}, expected 2.");

            var ordered = queryable.Where(d => d.Status == "active").OrderBy(d => d.Year).ToList();
            Assert(ordered.Count == 3, $"OrderBy returned {ordered.Count}, expected 3.");
            Assert(ordered[0].Year == 2023, $"OrderBy first year {ordered[0].Year}, expected 2023.");
            Assert(ordered[2].Year == 2025, $"OrderBy last year {ordered[2].Year}, expected 2025.");
            var orderedDesc = queryable.Where(d => d.Status == "active").OrderByDescending(d => d.Year).ToList();
            Assert(orderedDesc[0].Year == 2025, $"OrderByDesc first year {orderedDesc[0].Year}, expected 2025.");

            var statuses = new[] { "active", "archived" };
            var inSet = queryable.Where(d => statuses.Contains(d.Status!)).ToList();
            Assert(inSet.Count == 4, $"TermInSet returned {inSet.Count}, expected 4.");

            var projectedThenWhere = queryable
                .Where(d => d.Title!.StartsWith("native"))
                .Select(d => d.Title)
                .ToList();
            Assert(projectedThenWhere.Count == 1, $"Where+Select returned {projectedThenWhere.Count}, expected 1.");
        }

        // --- Index backup ---
        {
            var backupPath = Path.Combine(rootPath, "backup-" + policy);
            Directory.CreateDirectory(backupPath);
            var manifest = IndexBackup.CreateManifest(indexPath);
            Assert(manifest is not null, "IndexBackup.CreateManifest returned null");
            Assert(manifest!.Files.Count >= 1, $"backup manifest has {manifest.Files.Count} files, expected >=1.");
        }

        // --- Analytics export ---
        using (var analyticsWriter = File.CreateText(analyticsPath))
            analytics.ExportJson(analyticsWriter);

        Assert(new FileInfo(analyticsPath).Length > 2, "search analytics JSON was not written.");
        Assert(new FileInfo(slowLogPath).Length > 0, "slow-query log was not written.");
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Native AOT smoke failed for index directory '{indexPath}'.", ex);
    }
}

// =========================================================================
// Test data
// =========================================================================

static LeanDocument[] BuildDocuments()
{
    return
    [
        Document(
            id: "doc-1",
            title: "native aot search",
            body: "lean corpus supports compact native publishing",
            code: "native",
            status: "active",
            category: "docs",
            year: 2025,
            latitude: 51.5074,
            longitude: -0.1278,
            embedding: [1f, 0f, 0f, 0f]),
        Document(
            id: "doc-2",
            title: "fast corpus indexing",
            body: "stored fields and diagnostics stay available",
            code: "index",
            status: "active",
            category: "docs",
            year: 2024,
            latitude: 51.4545,
            longitude: -2.5879,
            embedding: [0.9f, 0.1f, 0f, 0f]),
        Document(
            id: "doc-3",
            title: "api filtering",
            body: "geo vector and collapse queries are covered",
            code: "api",
            status: "active",
            category: "api",
            year: 2023,
            latitude: 40.7128,
            longitude: -74.0060,
            embedding: [0f, 1f, 0f, 0f]),
        Document(
            id: "doc-4",
            title: "archived sample",
            body: "inactive documents remain searchable by explicit status",
            code: "archive",
            status: "archived",
            category: "archive",
            year: 2020,
            latitude: 48.8566,
            longitude: 2.3522,
            embedding: [0f, 0f, 1f, 0f]),
    ];
}

static LeanDocument Document(
    string id,
    string title,
    string body,
    string code,
    string status,
    string category,
    int year,
    double latitude,
    double longitude,
    float[] embedding,
    bool isPublished = true)
{
    var document = new LeanDocument();
    document.Add(new StringField("id", id));
    document.Add(new TextField("title", title));
    document.Add(new TextField("body", body));
    document.Add(new StringField("code", code));
    document.Add(new StringField("status", status));
    document.Add(new StringField("category", category));
    document.Add(new NumericField("year", year));
    document.Add(new GeoPointField("location", latitude, longitude));
    document.Add(new VectorField("embedding", new ReadOnlyMemory<float>(embedding)));
    document.Add(new StringField("isPublished", isPublished ? "true" : "false", stored: true));
    return document;
}

// =========================================================================
// Helpers
// =========================================================================

static void AssertHits(TopDocs results, int expectedHits, string scenario)
    => Assert(results.TotalHits == expectedHits, $"{scenario} returned {results.TotalHits} hit(s), expected {expectedHits}.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

/// <summary>Captures tokens into a list for assertions.</summary>
namespace Rowles.LeanCorpus.Example.NativeAot
{
file sealed class MaterialisingTokenSink : ISpanTokenSink
{
    public List<Token> Tokens { get; } = [];
    public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
        string type = Token.DefaultType, int positionIncrement = 1, byte[]? payload = null)
        => Tokens.Add(new Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
}

/// <summary>Captures tokens into a list via IAnalyser.Analyse.</summary>
file sealed class CapturingTokenSink : ISpanTokenSink
{
    public List<Token> Tokens { get; } = [];
    public int Count => Tokens.Count;
    public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
        string type = Token.DefaultType, int positionIncrement = 1, byte[]? payload = null)
        => Tokens.Add(new Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
}

// =========================================================================
// LINQ smoke test model (existing)
// =========================================================================

file sealed class NativeSmokeDoc
{
    public string? Title { get; set; }
    public int Year { get; set; }
    public string? Status { get; set; }
    public bool IsPublished { get; set; }
}

file static class NativeSmokeFields
{
    public static readonly LeanField<NativeSmokeDoc, string> Title       = new("title",       FieldType.Text,    true, true, true);
    public static readonly LeanField<NativeSmokeDoc, int>    Year        = new("year",        FieldType.Numeric, true, true, true);
    public static readonly LeanField<NativeSmokeDoc, string> Status      = new("status",      FieldType.String,  true, true, true);
    public static readonly LeanField<NativeSmokeDoc, bool>   IsPublished = new("isPublished", FieldType.String,  true, true, true);
}

file sealed class NativeSmokeDocMap : LeanDocumentMap<NativeSmokeDoc>
{
    public override string DocumentName => "doc";
    public override bool StrictSchema => true;
    public override IReadOnlyList<LeanFieldBinding<NativeSmokeDoc>> Fields { get; } = new[]
    {
        new LeanFieldBinding<NativeSmokeDoc>("title",       FieldType.Text,    true, true, true),
        new LeanFieldBinding<NativeSmokeDoc>("year",        FieldType.Numeric, true, true, true),
        new LeanFieldBinding<NativeSmokeDoc>("status",      FieldType.String,  true, true, true),
        new LeanFieldBinding<NativeSmokeDoc>("isPublished", FieldType.String,  true, true, true),
    };
    public override LeanDocument ToDocument(NativeSmokeDoc d) => throw new NotSupportedException();
    public override NativeSmokeDoc FromStoredDocument(StoredDocument d) => new()
    {
        Title       = d.GetFirst("title"),
        Year        = d.GetFirst("year") is { } s && int.TryParse(s, out var y) ? y : 0,
        Status      = d.GetFirst("status"),
        IsPublished = d.GetFirst("isPublished") == "true",
    };
    public override IndexSchema CreateSchema(bool strict)
    {
        var s = new IndexSchema { StrictMode = strict };
        foreach (var f in Fields) s.Add(new FieldMapping(f.Name, f.FieldType) { IsStored = f.IsStored, IsIndexed = f.IsIndexed, IsRequired = f.IsRequired });
        return s;
    }
}
}
