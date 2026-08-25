$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsDocs {
    param([string[]]$Arguments = @())

    $repoRoot = Get-RepoRoot
    $scriptsPath = Get-ScriptsPath

    $hasExplicitSubCommand = $Arguments.Count -gt 0 -and $Arguments[0] -notlike '-*'
    $subCmd = if ($hasExplicitSubCommand) { $Arguments[0] } else { 'build' }
    $docsArgs = if ($hasExplicitSubCommand -and $Arguments.Count -gt 1) {
        $Arguments[1..($Arguments.Count - 1)]
    } elseif ($hasExplicitSubCommand) {
        @()
    } else {
        $Arguments
    }
    $skipBenchmarks = $docsArgs -contains '-SkipBenchmarks'
    $skipCoverage = $docsArgs -contains '-SkipCoverage'

    $docsDir  = Join-Path $repoRoot 'docs'
    $docfxJson = Join-Path $docsDir 'docfx.json'
    $apiDir   = Join-Path $docsDir 'api'
    $siteDir  = Join-Path $docsDir 'site'
    $diagnosticsDir = Join-Path $repoRoot 'artifacts/docs'
    $metadataLog = Join-Path $diagnosticsDir 'docfx-metadata.jsonl'
    $buildLog = Join-Path $diagnosticsDir 'docfx-build.jsonl'

    Assert-DotNetTool 'docfx'

    function Invoke-GenerateIndexes {
        Write-Heading 'Generating ADR index...'
        & (Join-Path $scriptsPath 'docs/generate-adr-index.ps1')
    }

    function Invoke-CopyRepositoryDocumentation {
        Write-Heading 'Copying repository documentation...'
        Copy-RepositoryDocumentation -RepoRoot $repoRoot -DocsDir $docsDir
    }

    function Invoke-MetadataRegeneration {
        Clear-ApiMetadata $docsDir
        Write-Heading 'Generating API metadata...'
        $exitCode = Invoke-DocfxWithDiagnostics -Command metadata -ConfigPath $docfxJson -LogPath $metadataLog
        if ($exitCode -ne 0) { exit $exitCode }
        Remove-ExternalInheritedMembers $docsDir
    }

    if ($subCmd -eq 'metadata') {
        Invoke-MetadataRegeneration
        Write-Success 'API metadata written.'
        exit 0
    }

    if ($subCmd -eq 'serve') {
        Invoke-GenerateIndexes
        Invoke-CopyRepositoryDocumentation
        if (-not (Test-Path (Join-Path $apiDir 'toc.yml'))) {
            Invoke-MetadataRegeneration
        } else {
            Write-Info 'API metadata exists, skipping regeneration.'
        }
        Copy-Changelog -RepoRoot $repoRoot -DocsDir $docsDir

        Write-Heading 'Building documentation site...'
        $exitCode = Invoke-DocfxWithDiagnostics -Command build -ConfigPath $docfxJson -LogPath $buildLog
        if ($exitCode -ne 0) { exit $exitCode }

        Write-Success "Serving on http://0.0.0.0:8080"
        docfx serve $siteDir --hostname 0.0.0.0 -p 8080
        exit $LASTEXITCODE
    }

    # build (default)
    Invoke-GenerateIndexes
    Invoke-CopyRepositoryDocumentation

    if (-not $skipBenchmarks) {
        Write-Heading 'Generating benchmark pages...'
        & (Join-Path $scriptsPath 'benchmarks/generate-docs.ps1')
    }

    if (-not $skipCoverage) {
        $xmlFiles = @(Find-CoverageResults (Join-Path $repoRoot 'coverage-results'))
        if ($xmlFiles.Count -gt 0) {
            New-CoverageReport -XmlFiles $xmlFiles -OutputDir (Join-Path $docsDir 'coverage')
        }
    }

    Invoke-MetadataRegeneration
    Copy-Changelog -RepoRoot $repoRoot -DocsDir $docsDir

    Write-Heading 'Building documentation site...'
    $exitCode = Invoke-DocfxWithDiagnostics -Command build -ConfigPath $docfxJson -LogPath $buildLog
    if ($exitCode -ne 0) { exit $exitCode }

    Write-Success "Site written to: $siteDir"
    exit 0
}
