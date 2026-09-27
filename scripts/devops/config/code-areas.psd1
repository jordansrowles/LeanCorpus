# Source-area mappings used by `test affected` and `benchmark affected`.
#
# Keyed by a stable area entry name. Each value maps source globs to the test
# targets they affect. A target is `suite:area` where `suite` is a key in
# test-suites.psd1 and `area` is a TestArea value. Globs are matched with
# PowerShell -like semantics (case-insensitive, `*` spans path separators).

@{
    'store'          = @{ Globs = @('src/core/Rowles.LeanCorpus/Store/**');                          Targets = @('core:Store', 'server-core:Server', 'server-integration:Server') }
    'codecs'         = @{ Globs = @('src/core/Rowles.LeanCorpus/Codecs/**');                         Targets = @('core:CodecKit') }
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

    'analysers'      = @{ Globs = @('src/core/Rowles.Text/Analysis/Analysers/**');                   Targets = @('text:Analysers', 'core:TextIntegration') }
    'filters'        = @{ Globs = @('src/core/Rowles.Text/Analysis/Filters/**');                     Targets = @('text:Filters', 'core:TextIntegration') }
    'stemmers'       = @{ Globs = @('src/core/Rowles.Text/Analysis/Stemmers/**');                    Targets = @('text:Stemmers', 'core:TextIntegration') }
    'tokenisers'     = @{ Globs = @('src/core/Rowles.Text/Analysis/Tokenisers/**');                  Targets = @('text:Tokenisers', 'core:TextIntegration') }
    'text-root'      = @{ Globs = @('src/core/Rowles.Text/Analysis/*.cs', 'src/core/Rowles.Text/*.csproj'); Targets = @('text:Analysers', 'core:TextIntegration') }

    # Test and benchmark changes carry area intent for the production code they exercise.
    'core-index-tests' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.Core/Index/**'); Targets = @('core:Index') }
    'core-codeckit-tests' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.Core/CodecKit/**'); Targets = @('core:CodecKit') }
    'core-search-tests' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.Core/Search/**'); Targets = @('core:Search') }
    'core-search-benchmarks' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Benchmarks/Search/QueryParserBenchmarks.cs'); Targets = @('core:Search') }
    'core-search-docs' = @{ Globs = @('docs/articles/vs-lucene.md', 'docs/articles/ADRs/ADR038-bounded-query-string-compilation.md', 'docs/searching/04-query-parser.md'); Targets = @('core:Search') }
    'aot-search-smoke-test' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.AOTSmoke/IndexSmokeTests.cs'); Targets = @('aot:Search') }
    'text-analyser-tests' = @{ Globs = @('src/devops/Rowles.Text.Tests/Analysers/**'); Targets = @('text:Analysers') }
    'text-filter-tests' = @{ Globs = @('src/devops/Rowles.Text.Tests/Filters/**'); Targets = @('text:Filters') }
    'text-filter-benchmarks' = @{
        Globs = @('src/devops/Rowles.Text.Benchmarks/CachingGraphEdgeBenchmarks.cs')
        Targets = @('text:Filters', 'core:TextIntegration')
    }
    'text-tokeniser-tests' = @{ Globs = @('src/devops/Rowles.Text.Tests/Tokenisers/**'); Targets = @('text:Tokenisers') }
    'core-textintegration-tests' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Tests.Core/TextIntegration/**'); Targets = @('core:TextIntegration') }
    'core-index-benchmarks' = @{ Globs = @('src/devops/Rowles.LeanCorpus.Benchmarks/Index/**', 'src/devops/Rowles.LeanCorpus.Benchmarks/Program.cs'); Targets = @('core:Index') }
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
