$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DevOpsDocs {
    param([string[]]$Arguments = @())

    $repoRoot = Get-RepoRoot
    $scriptsPath = Get-ScriptsPath
    $docsSourceDir = Join-Path $repoRoot 'docs/content'
    $siteConfigDir = Join-Path $repoRoot 'docs/site-config'
    $docfxJson = Join-Path $siteConfigDir 'docfx.json'
    $manifestPath = Join-Path $siteConfigDir 'imports.json'
    $docsArtifactRoot = Get-ArtifactKindRoot -Kind docs -RepoRoot $repoRoot
    $contentDir = Join-Path $docsArtifactRoot 'content'
    $apiDir = Get-DocsArtifactPath -Name api -RepoRoot $repoRoot
    $siteDir = Get-DocsArtifactPath -Name site -RepoRoot $repoRoot
    $diagnosticsDir = Get-DocsArtifactPath -Name diagnostics -RepoRoot $repoRoot
    $metadataLog = Join-Path $diagnosticsDir 'docfx-metadata.jsonl'
    $buildLog = Join-Path $diagnosticsDir 'docfx-build.jsonl'

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

    function Copy-CanonicalDocumentation {
        if (-not (Test-Path -LiteralPath $docsSourceDir -PathType Container)) {
            throw "Canonical documentation directory not found: $docsSourceDir"
        }
        if (Test-Path -LiteralPath $contentDir) {
            Remove-Item -LiteralPath $contentDir -Recurse -Force
        }
        [void][System.IO.Directory]::CreateDirectory($contentDir)
        Get-ChildItem -LiteralPath $docsSourceDir -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $contentDir -Recurse -Force
        }
        Write-Info 'Canonical documentation copied into the disposable staging tree.'
    }

    function Prepare-DiagramAssets {
        $diagramSource = Join-Path $siteConfigDir 'diagrams'
        $diagramScript = Join-Path $diagramSource 'build.mjs'
        $moduleRoot = Join-Path $diagramSource 'node_modules'
        $outputDir = Join-Path $contentDir 'assets/diagrams'
        [void][System.IO.Directory]::CreateDirectory($outputDir)

        $node = Get-Command node -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        $npm = Get-Command npm -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $node -or $null -eq $npm) {
            throw 'Node.js and npm are required to prepare pinned diagram assets. Install Node.js 20 or newer, then rerun pwsh ./devops.ps1 docs prepare.'
        }

        $bytefieldModule = Join-Path $moduleRoot 'bytefield-svg'
        $chartBundle = Join-Path $moduleRoot 'chart.js/dist/chart.umd.js'
        if (-not (Test-Path -LiteralPath $bytefieldModule) -or
            -not (Test-Path -LiteralPath $chartBundle)) {
            Write-Info 'Restoring pinned diagram build dependencies with npm ci...'
            & $npm.Source ci --prefix $diagramSource
            if ($LASTEXITCODE -ne 0) { throw "Pinned diagram dependency restore failed with exit code $LASTEXITCODE." }
        }

        $previousOutput = $env:LEAN_CORPUS_DOCS_DIAGRAM_OUTPUT
        try {
            $env:LEAN_CORPUS_DOCS_DIAGRAM_OUTPUT = $outputDir
            & $node.Source $diagramScript
            if ($LASTEXITCODE -ne 0) { throw "Diagram generation failed with exit code $LASTEXITCODE." }
        } finally {
            if ($null -eq $previousOutput) {
                Remove-Item Env:LEAN_CORPUS_DOCS_DIAGRAM_OUTPUT -ErrorAction SilentlyContinue
            } else {
                $env:LEAN_CORPUS_DOCS_DIAGRAM_OUTPUT = $previousOutput
            }
        }
    }

    function Prepare-BenchmarkPages {
        $benchmarkDir = Join-Path $contentDir 'benchmarks'
        $benchmarkGenerator = Join-Path $scriptsPath 'benchmarks/generate-docs.ps1'
        if (-not $skipBenchmarks) {
            Write-Heading 'Generating benchmark pages...'
            & $benchmarkGenerator -OutputDir $benchmarkDir
            if ($LASTEXITCODE -ne 0) { throw "Benchmark page generation failed with exit code $LASTEXITCODE." }
        } else {
            & $benchmarkGenerator -OutputDir $benchmarkDir -SkipRunScan
            if ($LASTEXITCODE -ne 0) { throw "Benchmark overview generation failed with exit code $LASTEXITCODE." }
        }
    }

    function Prepare-CoverageReport {
        if ($skipCoverage) { return }

        $coverageOutput = Get-DocsArtifactPath -Name coverage -RepoRoot $repoRoot
        $coverageRun = $null
        $gitContext = Get-ArtifactGitContext -RepoRoot $repoRoot
        if ($gitContext.commit -and -not $gitContext.dirty) {
            $coverageRun = Get-LatestSuccessfulArtifactRun -Kind coverage -RepoRoot $repoRoot `
                -Commit $gitContext.commit
        } elseif ($gitContext.dirty) {
            Write-Info 'Current working tree is dirty; retained coverage evidence will not be reused.'
        }

        $xmlFiles = @(
            if ($null -ne $coverageRun) {
                Find-CoverageResults (Join-Path $coverageRun.RunDirectory 'raw')
            }
        )
        if ($xmlFiles.Count -gt 0) {
            New-CoverageReport -RepoRoot $repoRoot -XmlFiles $xmlFiles -OutputDir $coverageOutput
        } else {
            Remove-OwnedArtifactPath -Path $coverageOutput -ArtifactRoot (Get-ArtifactRoot -RepoRoot $repoRoot)
            Write-Info 'Current-commit coverage evidence is unavailable; continuing without a coverage report.'
        }
    }

    function Prepare-DocumentationInputs {
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "Documentation import manifest not found: $manifestPath"
        }
        if (-not (Test-Path -LiteralPath $docfxJson -PathType Leaf)) {
            throw "DocFX configuration not found: $docfxJson"
        }

        Write-Heading 'Preparing documentation inputs...'
        Copy-CanonicalDocumentation
        Prepare-DiagramAssets
        Copy-RepositoryDocumentation -RepoRoot $repoRoot -ContentDirectory $contentDir `
            -ManifestPath $manifestPath -DocsSourceDirectory $docsSourceDir

        Copy-Changelog -RepoRoot $repoRoot -DocsDir $contentDir
        New-DocsSectionCatalog -Directory (Join-Path $contentDir 'changelog') `
            -Title 'Changelog' -Description 'Versioned LeanCorpus, Rowles.Text, Community Server and managed DevOps release notes.' `
            -SeparateServerChangelog

        $adrDirectory = Join-Path $docsSourceDir 'articles/ADRs'
        $stagedAdrDirectory = Join-Path $contentDir 'articles/ADRs'
        $adrIndex = Join-Path $stagedAdrDirectory 'index.md'
        & (Join-Path $scriptsPath 'docs/generate-adr-index.ps1') `
            -AdrDirectory $adrDirectory -OutputPath $adrIndex -StagingAdrDirectory $stagedAdrDirectory
        if ($LASTEXITCODE -ne 0) { throw "ADR index generation failed with exit code $LASTEXITCODE." }

        Prepare-BenchmarkPages
        Set-DocsContributionMetadata -RepoRoot $repoRoot -ContentDirectory $contentDir `
            -ManifestPath $manifestPath
        Prepare-CoverageReport
        Write-DocsSiteMetadata -RepoRoot $repoRoot
        Write-Success "Documentation inputs staged under $contentDir"
    }

    function Invoke-MetadataRegeneration {
        Clear-ApiMetadata $apiDir
        Write-Heading 'Generating API metadata...'
        $exitCode = Invoke-DocfxWithDiagnostics -Command metadata -ConfigPath $docfxJson -LogPath $metadataLog
        if ($exitCode -ne 0) { exit $exitCode }
        Remove-ExternalInheritedMembers $apiDir
    }

    function Invoke-DocumentationBuild {
        if (Test-Path -LiteralPath $siteDir) {
            Remove-Item -LiteralPath $siteDir -Recurse -Force
        }
        Invoke-MetadataRegeneration
        Write-Heading 'Building documentation site...'
        $exitCode = Invoke-DocfxWithDiagnostics -Command build -ConfigPath $docfxJson -LogPath $buildLog
        if ($exitCode -ne 0) { exit $exitCode }
        Write-Success "Site written to: $siteDir"
    }

    switch ($subCmd) {
        'test' {
            Write-Heading 'Validating staged documentation structure...'
            & (Join-Path $scriptsPath 'devops/validation/adr-index-smoke.ps1')
            & (Join-Path $scriptsPath 'devops/validation/docs-site-smoke.ps1') `
                -RepoRoot $repoRoot -ContentDirectory $contentDir -ApiDirectory $apiDir -SiteDirectory $siteDir
            $testArguments = @('-Suite', 'docs-site') + $docsArgs
            return Invoke-DevOpsTest -Arguments $testArguments
        }
        'prepare' {
            Prepare-DocumentationInputs
            return 0
        }
        'metadata' {
            Prepare-DocumentationInputs
            Invoke-MetadataRegeneration
            Write-Success 'API metadata written.'
            return 0
        }
        'serve' {
            Prepare-DocumentationInputs
            Invoke-DocumentationBuild
            Write-Success 'Serving on http://0.0.0.0:8080'
            $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
            & $dotnet tool run docfx -- serve $siteDir --hostname 0.0.0.0 -p 8080
            return $LASTEXITCODE
        }
        'build' {
            Prepare-DocumentationInputs
            Invoke-DocumentationBuild
            return 0
        }
        default {
            Write-Failure "Unknown docs subcommand '$subCmd'. Expected: prepare, build, metadata, serve or test."
            return 2
        }
    }
}
