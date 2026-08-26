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

    'server-abstractions' = @{ Globs = @('src/server/Rowles.LeanCorpus.Server.Abstractions/**'); Targets = @('server-abstractions:Server', 'server-integration:Server') }
    'server-core' = @{ Globs = @('src/server/Rowles.LeanCorpus.Server.Core/**'); Targets = @('server-core:Server', 'server-integration:Server') }
    'server-transport' = @{ Globs = @('src/server/Rowles.LeanCorpus.Server.AspNetCore/**', 'src/server/Rowles.LeanCorpus.Server.Grpc/**', 'src/server/Rowles.LeanCorpus.Server.Local/**', 'src/server/Rowles.LeanCorpus.Studio/**'); Targets = @('server-integration:Server') }
    'server-tests' = @{ Globs = @('src/server/**/*.Tests/**'); Targets = @('server-abstractions:Server', 'server-core:Server', 'server-integration:Server') }
}
