# Source-area mappings used by `test affected` and `benchmark affected`.
#
# Keyed by a stable area entry name. Each value maps source globs to the test
# targets they affect. A target is `suite:area` where `suite` is a key in
# test-suites.psd1 and `area` is a TestArea value. Globs are matched with
# PowerShell -like semantics (case-insensitive, `*` spans path separators).

@{
    'store'          = @{ Globs = @('src/core/Rowles.LeanCorpus/Store/**');                          Targets = @('core:Store', 'server-core:Server', 'server-integration:Server') }
    'codecs'         = @{ Globs = @('src/core/Rowles.LeanCorpus/Codecs/**');                         Targets = @('core:CodecKit') }
    'compression-packages' = @{ Globs = @('src/core/Rowles.LeanCorpus.Compression.LZ4/**', 'src/core/Rowles.LeanCorpus.Compression.Snappy/**', 'src/core/Rowles.LeanCorpus.Compression.Zstandard/**'); Targets = @('core:CodecKit') }
    'diagnostics'    = @{ Globs = @('src/core/Rowles.LeanCorpus/Diagnostics/**');                    Targets = @('core:Diagnostics') }
    'document'       = @{ Globs = @('src/core/Rowles.LeanCorpus/Document/**');                       Targets = @('core:Document') }
    'index'          = @{ Globs = @('src/core/Rowles.LeanCorpus/Index/**');                          Targets = @('core:Index') }
    'index-backup'   = @{ Globs = @('src/core/Rowles.LeanCorpus/Index/Backup/**');                   Targets = @('server-core:Server', 'server-integration:Server') }
    'index-segment'  = @{ Globs = @('src/core/Rowles.LeanCorpus/Index/Segment/**');                  Targets = @('server-core:Server', 'server-integration:Server') }
    'index-indexer'  = @{ Globs = @('src/core/Rowles.LeanCorpus/Index/Indexer/**');                  Targets = @('server-core:Server', 'server-integration:Server') }
    'linq'           = @{ Globs = @('src/core/Rowles.LeanCorpus/Linq/**');                           Targets = @('core:Linq') }
    'mapping'        = @{ Globs = @('src/core/Rowles.LeanCorpus/Mapping/**');                        Targets = @('core:Mapping') }
    'search'         = @{ Globs = @('src/core/Rowles.LeanCorpus/Search/**');                         Targets = @('core:Search') }
    'serialization'  = @{ Globs = @('src/core/Rowles.LeanCorpus/Serialization/**');                  Targets = @('core:Serialization') }
    'util'           = @{ Globs = @('src/core/Rowles.LeanCorpus/Util/**');                           Targets = @('core:Util') }
    'core-root'      = @{ Globs = @('src/core/Rowles.LeanCorpus/*.cs', 'src/core/Rowles.LeanCorpus/*.csproj'); Targets = @('core:Foundation') }
    'core-configuration' = @{ Globs = @('src/core/Rowles.LeanCorpus/Configuration/**');             Targets = @('core:Foundation', 'core:Search') }

    'analysers'      = @{ Globs = @('src/core/Rowles.Text/Analysis/Analysers/**');                   Targets = @('text:Analysers', 'core:TextIntegration') }
    'filters'        = @{ Globs = @('src/core/Rowles.Text/Analysis/Filters/**');                     Targets = @('text:Filters', 'core:TextIntegration') }
    'stemmers'       = @{ Globs = @('src/core/Rowles.Text/Analysis/Stemmers/**');                    Targets = @('text:Stemmers', 'core:TextIntegration') }
    'tokenisers'     = @{ Globs = @('src/core/Rowles.Text/Analysis/Tokenisers/**');                  Targets = @('text:Tokenisers', 'core:TextIntegration') }
    'text-root'      = @{ Globs = @('src/core/Rowles.Text/Analysis/*.cs', 'src/core/Rowles.Text/*.csproj'); Targets = @('text:Analysers', 'core:TextIntegration') }

    # Test and benchmark changes carry area intent for the production code they exercise.
    'core-foundation-tests' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.Core/Foundation/**'); Targets = @('core:Foundation') }
    'core-index-tests' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.Core/Index/**'); Targets = @('core:Index') }
    'core-codeckit-tests' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.Core/CodecKit/**'); Targets = @('core:CodecKit') }
    'core-search-tests' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.Core/Search/**'); Targets = @('core:Search') }
    'core-search-benchmarks' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Benchmarks/Search/QueryParserBenchmarks.cs', 'src/devops/Rowles.LeanCorpus.Benchmarks/Search/PhraseQueryBenchmarks.cs'); Targets = @('core:Search') }
    'core-reader-cache-benchmarks' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Benchmarks/Search/SegmentReaderCacheBenchmarks.cs', 'src/devops/Rowles.LeanCorpus.Benchmarks/Search/SegmentReaderResourceCacheBenchmarks.cs', 'src/devops/Rowles.LeanCorpus.Benchmarks/Program.cs'); Targets = @('core:Search') }
    'core-term-cache-benchmarks' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Benchmarks/Search/QualifiedTermCacheBenchmarks.cs'); Targets = @('core:Index', 'core:Search') }
    'core-search-docs' = @{ Globs = @('docs/articles/vs-lucene.md', 'docs/articles/ADRs/ADR011-lazy-segment-reader-lifetimes.md', 'docs/articles/ADRs/ADR038-bounded-query-string-compilation.md', 'docs/searching/02-boolean-queries.md', 'docs/searching/03-phrase-and-proximity.md', 'docs/searching/04-query-parser.md', 'docs/searching/12-pagination-and-rescoring.md', 'docs/advanced/06-block-join.md', 'docs/advanced/08-filtered-vector-search.md'); Targets = @('core:Search') }
    'core-segment-validation-docs' = @{ Globs = @('docs/articles/ADRs/ADR011-lazy-segment-reader-lifetimes.md', 'docs/articles/ADRs/ADR025-unified-codec-catalogue.md'); Targets = @('core:Index') }
    'release-adr-version-metadata' = @{ Globs = @('docs/articles/ADRs/ADR031-dwpt-postings-arena.md', 'docs/articles/ADRs/ADR033-spatial-geometry-packed-bkd.md', 'docs/articles/ADRs/ADR034-shape-indexing-and-spatial-relations.md', 'docs/articles/ADRs/ADR035-shape-docvalues-and-spatial-aggregations.md'); Targets = @() }
    'core-index-snapshot-docs' = @{ Globs = @('docs/concurrency/03-snapshots-and-policies.md'); Targets = @('core:Index') }
    'core-configuration-docs' = @{ Globs = @('docs/getting-started/05-configuration-reference.md'); Targets = @('core:Foundation', 'core:Search') }
    'core-compression-docs' = @{ Globs = @('docs/getting-started/01-installation.md', 'docs/getting-started/03-configuration.md', 'docs/tips/01-compression.md'); Targets = @('core:CodecKit') }
    'text-analysis-docs' = @{ Globs = @('docs/analysis/**'); Targets = @('text:Analysers') }
    'text-tokeniser-docs' = @{ Globs = @('docs/analysis/02-tokenisers.md', 'docs/analysis/index.md', 'docs/articles/features/analysis.md', 'docs/articles/vs-lucene.md', 'lexicons/README.md'); Targets = @('text:Tokenisers', 'core:TextIntegration') }
    'aot-search-smoke-test' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.AOTSmoke/IndexSmokeTests.cs'); Targets = @('aot:Search') }
    'aot-analysis-smoke-test' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.AOTSmoke/AnalysisSmokeTests.cs'); Targets = @('aot:Search') }
    'aot-tokeniser-smoke-test' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.AOTSmoke/TokeniserSmokeTests.cs'); Targets = @('aot:Search') }
    'text-analyser-tests' = @{ Globs = @('src/devops/Rowles.Text.Tests/Analysers/**'); Targets = @('text:Analysers') }
    'text-standalone-package-tests' = @{ Globs = @('src/devops/Rowles.Text.Tests/StandalonePackageTests.cs'); Targets = @('text:Tokenisers') }
    'text-filter-tests' = @{ Globs = @('src/devops/Rowles.Text.Tests/Filters/**'); Targets = @('text:Filters') }
    'text-filter-benchmarks' = @{
        Globs = @('src/devops/Rowles.Text.Benchmarks/CachingGraphEdgeBenchmarks.cs')
        Targets = @('text:Filters', 'core:TextIntegration')
    }
    'text-tokeniser-tests' = @{ Globs = @('src/devops/Rowles.Text.Tests/Tokenisers/**'); Targets = @('text:Tokenisers') }
    'text-tokeniser-benchmarks' = @{ Globs = @('src/devops/Rowles.Text.Benchmarks/LexiconPrefixBenchmarks.cs'); Targets = @('text:Tokenisers', 'core:TextIntegration') }
    'core-textintegration-tests' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.Core/TextIntegration/**'); Targets = @('core:TextIntegration') }
    'core-index-benchmarks' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Benchmarks/Index/**', 'src/devops/Rowles.LeanCorpus.Benchmarks/Search/BoundedLruCacheFailureBenchmarks.cs', 'src/devops/Rowles.LeanCorpus.Benchmarks/Search/VectorFirstTouchBenchmarks.cs', 'src/devops/Rowles.LeanCorpus.Benchmarks/Program.cs'); Targets = @('core:Index') }
    'core-docvalues-benchmarks' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Benchmarks/Search/DocValuesReadBenchmarks.cs'); Targets = @('core:Index') }
    'benchmark-runner-config' = @{ Globs = @('scripts/devops/config/benchmark-groups.psd1', 'scripts/devops/config/benchmark-suites.psd1', 'scripts/devops/config/code-areas.psd1'); Targets = @('core:CodecKit') }

    # Release notes are recognised but do not independently select tests. A
    # change consisting only of these files still fails closed below.
    'release-notes' = @{ Globs = @('changelog/**', 'docs/changelog/**'); Targets = @() }

    'server-abstractions' = @{ Globs = @('src/server/Rowles.LeanCorpus.Server.Abstractions/**'); Targets = @('server-abstractions:Server', 'server-integration:Server') }
    'server-core' = @{ Globs = @('src/server/Rowles.LeanCorpus.Server.Core/**'); Targets = @('server-core:Server', 'server-integration:Server') }
    'server-transport' = @{ Globs = @('src/server/Rowles.LeanCorpus.Server.AspNetCore/**', 'src/server/Rowles.LeanCorpus.Server.Grpc/**', 'src/server/Rowles.LeanCorpus.Server.Local/**', 'src/server/Rowles.LeanCorpus.Studio/**'); Targets = @('server-integration:Server') }
    'server-tests' = @{ Globs = @('src/server/**/*.Tests/**'); Targets = @('server-abstractions:Server', 'server-core:Server', 'server-integration:Server') }
    'server-core-docs' = @{ Globs = @('docs/server/08-operational-limits.md'); Targets = @('server-core:Server') }
}
