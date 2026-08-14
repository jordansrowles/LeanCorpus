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

    Assert-DotNetTool 'docfx'

    function Invoke-GenerateIndexes {
        Write-Heading 'Generating feature comparison...'
        & (Join-Path $scriptsPath 'docs/generate-feature-comparison.ps1')

        Write-Heading 'Generating ADR index...'
        & (Join-Path $scriptsPath 'docs/generate-adr-index.ps1')

        Write-Heading 'Generating examples catalogue...'
        & (Join-Path $scriptsPath 'docs/generate-example-index.ps1')
    }

    function Invoke-MetadataRegeneration {
        Clear-ApiMetadata $docsDir
        Write-Heading 'Generating API metadata...'
        docfx metadata $docfxJson
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Remove-ExternalInheritedMembers $docsDir
    }

    if ($subCmd -eq 'metadata') {
        Invoke-MetadataRegeneration
        Write-Success 'API metadata written.'
        exit 0
    }

    if ($subCmd -eq 'serve') {
        Invoke-GenerateIndexes
        if (-not (Test-Path (Join-Path $apiDir 'toc.yml'))) {
            Invoke-MetadataRegeneration
        } else {
            Write-Info 'API metadata exists, skipping regeneration.'
        }
        Copy-Changelog -RepoRoot $repoRoot -DocsDir $docsDir

        Write-Heading 'Building documentation site...'
        docfx build $docfxJson
        if ($LASTEXITCODE -ne 0) { Write-Error "docfx build failed"; exit $LASTEXITCODE }

        Write-Success "Serving on http://0.0.0.0:8080"
        docfx serve $siteDir --hostname 0.0.0.0 -p 8080
        exit $LASTEXITCODE
    }

    # build (default)
    Invoke-GenerateIndexes

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
    docfx build $docfxJson
    if ($LASTEXITCODE -ne 0) { Write-Error "docfx build failed"; exit $LASTEXITCODE }

    Write-Success "Site written to: $siteDir"
    exit 0
}
