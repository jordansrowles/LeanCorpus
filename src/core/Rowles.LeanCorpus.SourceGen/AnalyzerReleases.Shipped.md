; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|------
LCGEN001 | LeanCorpus.Mapping | Error | Unsupported property type
LCGEN002 | LeanCorpus.Mapping | Error | Duplicate generated field name
LCGEN003 | LeanCorpus.Mapping | Error | Invalid field name
LCGEN004 | LeanCorpus.Mapping | Warning | FromStoredDocument cannot be generated
LCGEN005 | LeanCorpus.Mapping | Error | Conflicting field attributes
LCGEN006 | LeanCorpus.Mapping | Error | Missing numeric encoding
LCGEN007 | LeanCorpus.Mapping | Error | Missing vector dimension
LCGEN008 | LeanCorpus.Mapping | Error | Unsupported collection shape
LCGEN009 | LeanCorpus.Mapping | Error | Invalid geo-point mapping
LCGEN010 | LeanCorpus.Mapping | Error | Unsupported document target
LCGEN011 | LeanCorpus.Mapping | Error | Mapped property is not accessible
LCGEN012 | LeanCorpus.Mapping | Warning | FromStoredDocument construction is not available
LCGEN013 | LeanCorpus.Mapping | Error | Invalid decimal string storage