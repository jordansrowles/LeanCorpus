# Source-area mappings used by `test affected` and `benchmark affected`.
#
# Keyed by a stable area entry name. Each value maps source globs to the test
# targets they affect. A target is `suite:area` where `suite` is a key in
# test-suites.psd1 and `area` is a TestArea value. Globs are matched with
# PowerShell -like semantics (case-insensitive, `*` spans path separators).

@{
    'store'          = @{ Globs = @('src/core/Rowles.LeanCorpus/Store/**');                          Targets = @('core:Store', 'server-core:Server', 'server-integration:Server') }
    'codecs'         = @{ Globs = @('src/core/Rowles.LeanCorpus/Codecs/**');                         Targets = @('core:CodecKit') }
    'compression-packages' = @{ Globs = @('src/core/Rowles.LeanCorpus.Compression.LZ4/**', 'src/core/Rowles.LeanCorpus.Compression.Snappy/**', 'src/core/Rowles.LeanCorpus.Compression.Zstandard/**', 'src/core/OptionalCompression.Pack.targets'); Targets = @('core:CodecKit') }
    'diagnostics'    = @{ Globs = @('src/core/Rowles.LeanCorpus/Diagnostics/**');                    Targets = @('core:Diagnostics') }
    'document'       = @{ Globs = @('src/core/Rowles.LeanCorpus/Document/**');                       Targets = @('core:Document') }
    'index-segment-merge' = @{ Globs = @('src/core/Rowles.LeanCorpus/Index/Segment/SegmentMerger.cs', 'src/core/Rowles.LeanCorpus/Index/Segment/Merging/**'); Targets = @('core:Index', 'core:CodecKit', 'core:Search') }
    'merge-ownership-test' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.Architecture/MergeOwnershipTests.cs'); Targets = @('architecture:Architecture') }
    'index'          = @{ Globs = @('src/core/Rowles.LeanCorpus/Index/**');                          Targets = @('core:Index') }
    'index-backup'   = @{ Globs = @('src/core/Rowles.LeanCorpus/Index/Backup/**');                   Targets = @('server-core:Server', 'server-integration:Server') }
    'index-segment'  = @{ Globs = @('src/core/Rowles.LeanCorpus/Index/Segment/**');                  Targets = @('server-core:Server', 'server-integration:Server') }
    'index-indexer'  = @{ Globs = @('src/core/Rowles.LeanCorpus/Index/Indexer/**');                  Targets = @('server-core:Server', 'server-integration:Server') }
    'linq'           = @{ Globs = @('src/core/Rowles.LeanCorpus/Linq/**');                           Targets = @('core:Linq') }
    'mapping'        = @{ Globs = @('src/core/Rowles.LeanCorpus/Mapping/**');                        Targets = @('core:Mapping') }
    'search'         = @{ Globs = @('src/core/Rowles.LeanCorpus/Search/**');                         Targets = @('core:Search') }
    'serialisation'  = @{ Globs = @('src/core/Rowles.LeanCorpus/Serialisation/**');                  Targets = @('core:Serialisation') }
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
    'core-store-tests' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.Core/Store/**'); Targets = @('core:Store') }
    'core-search-tests' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.Core/Search/**'); Targets = @('core:Search') }
    'core-search-benchmarks' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Benchmarks/Search/QueryParserBenchmarks.cs', 'src/devops/Rowles.LeanCorpus.Benchmarks/Search/PhraseQueryBenchmarks.cs'); Targets = @('core:Search') }
    'core-reader-cache-benchmarks' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Benchmarks/Search/SegmentReaderCacheBenchmarks.cs', 'src/devops/Rowles.LeanCorpus.Benchmarks/Search/SegmentReaderResourceCacheBenchmarks.cs', 'src/devops/Rowles.LeanCorpus.Benchmarks/Program.cs'); Targets = @('core:Search') }
    'core-term-cache-benchmarks' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Benchmarks/Search/QualifiedTermCacheBenchmarks.cs'); Targets = @('core:Index', 'core:Search') }
    'core-search-docs' = @{ Globs = @('docs/content/articles/vs-lucene.md', 'docs/content/articles/ADRs/ADR011-lazy-segment-reader-lifetimes.md', 'docs/content/articles/ADRs/ADR038-bounded-query-string-compilation.md', 'docs/content/searching/02-boolean-queries.md', 'docs/content/searching/03-phrase-and-proximity.md', 'docs/content/searching/04-query-parser.md', 'docs/content/searching/12-pagination-and-rescoring.md', 'docs/content/advanced/06-block-join.md', 'docs/content/advanced/08-filtered-vector-search.md'); Targets = @('core:Search') }
    'core-segment-validation-docs' = @{ Globs = @('docs/content/articles/ADRs/ADR011-lazy-segment-reader-lifetimes.md', 'docs/content/articles/ADRs/ADR025-unified-codec-catalogue.md'); Targets = @('core:Index') }
    'core-vector-presence-docs' = @{ Globs = @('docs/content/articles/ADRs/ADR039-sparse-vector-presence.md'); Targets = @('core:CodecKit', 'core:Index', 'core:Search') }
    'core-storage-migration-docs' = @{ Globs = @('docs/content/articles/ADRs/ADR040-bounded-field-length-bodies.md', 'docs/content/contributors/codeckit/03-migrations.md', 'docs/content/contributors/storage-formats.md', 'docs/content/index-management/11-codec-migration-3-0.md'); Targets = @('core:CodecKit', 'core:Index') }
    'adr-index-docs' = @{ Globs = @('docs/content/articles/ADRs/index.md'); Targets = @() }
    'release-adr-version-metadata' = @{ Globs = @('docs/content/articles/ADRs/ADR031-dwpt-postings-arena.md', 'docs/content/articles/ADRs/ADR033-spatial-geometry-packed-bkd.md', 'docs/content/articles/ADRs/ADR034-shape-indexing-and-spatial-relations.md', 'docs/content/articles/ADRs/ADR035-shape-docvalues-and-spatial-aggregations.md'); Targets = @() }
    'core-index-snapshot-docs' = @{ Globs = @('docs/content/concurrency/03-snapshots-and-policies.md'); Targets = @('core:Index') }
    'core-configuration-docs' = @{ Globs = @('docs/content/getting-started/05-configuration-reference.md'); Targets = @('core:Foundation', 'core:Search') }
    'core-compression-docs' = @{ Globs = @('docs/content/getting-started/01-installation.md', 'docs/content/getting-started/03-configuration.md', 'docs/content/tips/01-compression.md'); Targets = @('core:CodecKit') }
    'text-analysis-docs' = @{ Globs = @('docs/content/analysis/**'); Targets = @('text:Analysers') }
    'text-tokeniser-docs' = @{ Globs = @('docs/content/analysis/02-tokenisers.md', 'docs/content/analysis/index.md', 'docs/content/articles/features/analysis.md', 'docs/content/articles/vs-lucene.md', 'lexicons/README.md'); Targets = @('text:Tokenisers', 'core:TextIntegration') }
    'complex-phrase-aot' = @{
        Globs = @('src/core/Rowles.LeanCorpus/Search/Parsing/ComplexPhraseQueryParser.cs')
        Targets = @('aot:Search')
    }
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
    'benchmark-runner-config' = @{ Globs = @('.config/devops/benchmark-groups.json', '.config/devops/benchmark-projects.json', '.config/devops/benchmark-suites.json', '.config/devops/benchmark-strategies.json', '.config/devops/benchmark-datasets.json', 'scripts/devops/config/benchmark-groups.psd1', 'scripts/devops/config/benchmark-suites.psd1', 'scripts/devops/config/code-areas.psd1'); Targets = @('core:CodecKit') }

    # Release notes are recognised but do not independently select tests. A
    # change consisting only of these files still fails closed below.
    'release-notes' = @{ Globs = @('changelog/**'); Targets = @() }
    # Workflow-only changes are verified by the matching PowerShell smoke scripts.
    # As with release notes, recognising them alone must not select unrelated tests.
    'lifecycle-workflow' = @{ Globs = @('.github/workflows/build.yml', 'scripts/devops/commands/build.ps1', 'scripts/devops/commands/server.ps1', 'scripts/devops/testing/targets.ps1', 'scripts/devops/validation/artifacts-smoke.ps1', 'scripts/devops/validation/test-selection-smoke.ps1'); Targets = @() }
    'server-running-docs' = @{ Globs = @('docs/content/server/01-running.md', 'src/server/README.md'); Targets = @() }
    'architecture-docs' = @{ Globs = @('docs/content/articles/ADRs/**', 'docs/content/articles/physical-index-lifetimes.md', 'docs/content/articles/index.md'); Targets = @('architecture:Architecture') }
    'package-policy' = @{ Globs = @('scripts/devops/packaging/package-policy.json', 'scripts/devops/packaging/validate-package-consumers.ps1'); Targets = @('architecture:Architecture', 'server-abstractions:Server', 'server-core:Server') }
    'aot-physical-runtime-smoke' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.AOTSmoke/PhysicalIndexRuntimeSmokeTests.cs'); Targets = @('aot:Index') }
    'physical-runtime' = @{ Globs = @('src/core/Rowles.LeanCorpus/Index/PhysicalIndexRuntime*.cs'); Targets = @('core:Index', 'server-core:Server', 'server-orchard:Orchard', 'server-orchard-functional:Functional', 'aot:Index') }

    'server-abstractions' = @{ Globs = @('src/server/Rowles.LeanCorpus.Server.Abstractions/**', 'src/server/Shared/**'); Targets = @('server-abstractions:Server', 'server-core:Server', 'server-integration:Server') }
    'server-core' = @{ Globs = @('src/server/Rowles.LeanCorpus.Server.Core/**'); Targets = @('server-core:Server', 'server-integration:Server') }
    'server-transport' = @{ Globs = @('src/server/Rowles.LeanCorpus.Server.AspNetCore/**', 'src/server/Rowles.LeanCorpus.Server.Grpc/**', 'src/server/Rowles.LeanCorpus.Server.Local/**', 'src/server/Rowles.LeanCorpus.Studio/**'); Targets = @('server-integration:Server') }
    'server-tests' = @{ Globs = @('src/server/**/*.Tests/**'); Targets = @('server-abstractions:Server', 'server-core:Server', 'server-integration:Server') }
    'server-orchard-provider' = @{ Globs = @('src/server/Rowles.LeanCorpus.OrchardCore.Search/**'); Targets = @('server-orchard:Orchard', 'server-orchard-functional:Functional') }
    'server-orchard-tests' = @{ Globs = @('src/server/devops/Rowles.LeanCorpus.OrchardCore.Search.Tests/**'); Targets = @('server-orchard:Orchard') }
    'server-orchard-test-module' = @{ Globs = @('src/server/devops/Rowles.LeanCorpus.OrchardCore.Search.TestModule/**'); Targets = @('server-orchard:Orchard', 'server-orchard-functional:Functional') }
    'server-orchard-functional-tests' = @{ Globs = @('src/server/devops/Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests/**', 'src/server/devops/Rowles.LeanCorpus.OrchardCore.Search.TestHost/**'); Targets = @('server-orchard-functional:Functional') }
    'server-orchard-build-config' = @{ Globs = @('Directory.Packages.props', 'Rowles.LeanCorpus.slnx', 'scripts/devops/config/test-suites.psd1'); Targets = @('server-orchard:Orchard', 'server-orchard-functional:Functional') }
    'server-orchard-docs' = @{ Globs = @('docs/site-config/docfx.json', 'docs/content/server/index.md', 'docs/content/server/orchard-core-search-provider.md', 'docs/content/server/evidence/**'); Targets = @() }
    'server-core-docs' = @{ Globs = @('docs/content/server/08-operational-limits.md'); Targets = @('server-core:Server') }
}
