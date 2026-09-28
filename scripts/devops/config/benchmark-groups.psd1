# Benchmark dependency mappings used by `benchmark affected`.
#
# Keyed by area entry name. Each value maps source globs to the benchmark
# project and a narrow set of representative benchmark classes. `Project` is
# `core` (the LeanCorpus benchmark runner) or `text` (Rowles.Text.Benchmarks).
# Globs are matched with PowerShell -like semantics (`*` spans path separators).
#
# BenchmarkDotNet `--filter` accepts a single glob, so `benchmark affected`
# runs one class per invocation. Keep these lists short (2-4 classes) to bound
# that fan-out while still covering the area.

@{
    'MMapIO'        = @{ Area = 'Store';         Globs = @('src/core/Rowles.LeanCorpus/Store/**');                 Project = 'core'; Benchmarks = @('MMapDirectoryIOBenchmarks', 'CompoundFileBenchmarks') }
    'CodecKit'      = @{ Area = 'CodecKit';      Globs = @('src/core/Rowles.LeanCorpus/Codecs/**');                Project = 'core'; Benchmarks = @('PackedIntCodecBenchmarks', 'CodecFrameBenchmarks', 'DocValuesReadBenchmarks', 'BinaryDocValuesWriteBenchmarks', 'FstLookupBenchmarks') }
    'StoredFields'  = @{ Area = 'CodecKit';      Globs = @('src/core/Rowles.LeanCorpus/Codecs/StoredFields/**'); Project = 'core'; Benchmarks = @('StoredFieldsReadBenchmarks', 'StoredFieldsWriteBenchmarks', 'StoredFieldsByteBoundedBenchmarks') }
    'Search'        = @{ Area = 'Search';        Globs = @('src/core/Rowles.LeanCorpus/Search/**', 'src/devops/Rowles.LeanCorpus.Benchmarks/Search/PhraseQueryBenchmarks.cs'); Project = 'core'; Benchmarks = @('TermQueryBenchmarks', 'BooleanQueryBenchmarks', 'PhraseQueryBenchmarks', 'HnswSearchBenchmarks') }
    'TermCaches'    = @{ Area = 'Index';         Globs = @('src/core/Rowles.LeanCorpus/Index/Segment/QualifiedTermCache.cs', 'src/core/Rowles.LeanCorpus/Index/Segment/SegmentReader.cs', 'src/core/Rowles.LeanCorpus/Index/Segment/SegmentReader.Facade.cs', 'src/core/Rowles.LeanCorpus/Search/Searcher/IndexSearcher.cs', 'src/core/Rowles.LeanCorpus/Search/Searcher/BoundedGenerationCache.cs', 'src/devops/Rowles.LeanCorpus.Benchmarks/Search/QualifiedTermCacheBenchmarks.cs'); Project = 'core'; Benchmarks = @('QualifiedTermCacheBenchmarks') }
    'BoundedLruCacheFailures' = @{ Area = 'Index'; Globs = @('src/core/Rowles.LeanCorpus/Index/Segment/BoundedLruCache.cs', 'src/devops/Rowles.LeanCorpus.Tests.Core/Index/Unit/BoundedLruCacheTests.cs', 'src/devops/Rowles.LeanCorpus.Benchmarks/Search/BoundedLruCacheFailureBenchmarks.cs'); Project = 'core'; Benchmarks = @('BoundedLruCacheFailureBenchmarks') }
    'Parser'        = @{ Area = 'Search';        Globs = @('src/devops/Rowles.LeanCorpus.Benchmarks/Search/QueryParserBenchmarks.cs'); Project = 'core'; Benchmarks = @('QueryParserBenchmarks') }
    'Indexing'      = @{ Area = 'Index';         Globs = @('src/core/Rowles.LeanCorpus/Index/**', 'src/devops/Rowles.LeanCorpus.Benchmarks/Index/LanguageAnalysisIndexingBenchmarks.cs'); Project = 'core'; Benchmarks = @('IndexingBenchmarks', 'LanguageAnalysisIndexingBenchmarks', 'MergeBenchmarks', 'FlushBenchmarks', 'PostingsArenaBenchmarks', 'DeletionQueueBenchmarks', 'VectorFirstTouchBenchmarks') }
    'Diagnostics'   = @{ Area = 'Diagnostics';   Globs = @('src/core/Rowles.LeanCorpus/Diagnostics/**');           Project = 'core'; Benchmarks = @('DiagnosticsBenchmarks', 'NumericAggregatorSimdBenchmarks') }
    'Document'      = @{ Area = 'Document';      Globs = @('src/core/Rowles.LeanCorpus/Document/**');              Project = 'core'; Benchmarks = @('IndexingBenchmarks') }
    'Linq'          = @{ Area = 'Linq';          Globs = @('src/core/Rowles.LeanCorpus/Linq/**');                  Project = 'core'; Benchmarks = @('TermQueryBenchmarks') }
    'Mapping'       = @{ Area = 'Mapping';       Globs = @('src/core/Rowles.LeanCorpus/Mapping/**');               Project = 'core'; Benchmarks = @('IndexingBenchmarks') }
    'Serialization' = @{ Area = 'Serialization'; Globs = @('src/core/Rowles.LeanCorpus/Serialization/**');         Project = 'core'; Benchmarks = @('SchemaAndJsonBenchmarks') }
    'Util'          = @{ Area = 'Util';          Globs = @('src/core/Rowles.LeanCorpus/Util/**');                  Project = 'core'; Benchmarks = @('PackedIntCodecBenchmarks') }

    'Analysers'     = @{ Area = 'Analysers';     Globs = @('src/core/Rowles.Text/Analysis/Analysers/**');          Project = 'text'; Benchmarks = @('AnalysisBenchmarks', 'AnalyserParityBenchmarks') }
    'Filters'       = @{ Area = 'Filters';       Globs = @('src/core/Rowles.Text/Analysis/Filters/**');            Project = 'text'; Benchmarks = @('TokenFilterBenchmarks', 'SynonymBenchmarks') }
    'CachingGraphEdges' = @{
        Area = 'Filters'
        Globs = @('src/core/Rowles.Text/Analysis/Filters/CachingTokenFilter.cs', 'src/devops/Rowles.Text.Benchmarks/CachingGraphEdgeBenchmarks.cs')
        Project = 'text'
        Benchmarks = @('CachingGraphEdgeBenchmarks')
    }
    'Stemmers'      = @{ Area = 'Stemmers';      Globs = @('src/core/Rowles.Text/Analysis/Stemmers/**');           Project = 'text'; Benchmarks = @('StemmerParityBenchmarks', 'HunspellBenchmarks') }
    'Tokenisers'    = @{ Area = 'Tokenisers';    Globs = @('src/core/Rowles.Text/Analysis/Tokenisers/**', 'src/devops/Rowles.Text.Benchmarks/LexiconPrefixBenchmarks.cs'); Project = 'text'; Benchmarks = @('NGramTokeniserBenchmarks', 'PatternTokeniserBenchmarks', 'LexiconPrefixBenchmarks') }
    'TextIntegration'= @{ Area = 'TextIntegration'; Globs = @('src/core/Rowles.Text/**');                         Project = 'core'; Benchmarks = @('TokenBudgetBenchmarks', 'LanguageAnalysisIndexingBenchmarks') }
}
