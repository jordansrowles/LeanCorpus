$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-PackageBaselinePolicy {
    param([System.Collections.IDictionary]$Entry)

    if ($Entry.Contains('strictBaseline')) {
        if ($Entry.strictBaseline -isnot [bool]) {
            throw "Package $($Entry.packageId) requires a Boolean 'strictBaseline' policy value."
        }
        if ($Entry.strictBaseline) {
            if (-not $Entry.enableApiCompatibility) {
                throw "Package $($Entry.packageId) requires enableApiCompatibility for strictBaseline."
            }
            if ([string]::IsNullOrWhiteSpace([string]$Entry.baselineVersion)) {
                throw "Package $($Entry.packageId) requires baselineVersion for strictBaseline."
            }
        }
    }
}

function New-PackageValidationProperties {
    param([System.Collections.IDictionary]$Entry, [string]$ProjectName)

    $baselineProperty = if ([string]::IsNullOrWhiteSpace([string]$Entry.baselineVersion)) {
        ''
    } else {
        "<PackageValidationBaselineVersion>$($Entry.baselineVersion)</PackageValidationBaselineVersion>"
    }
    $strictProperty = if ($Entry.Contains('strictBaseline') -and $Entry.strictBaseline) {
        '<EnableStrictModeForBaselineValidation>true</EnableStrictModeForBaselineValidation>'
    } else { '' }
    return "<Project><PropertyGroup Condition=`"'`$(MSBuildProjectName)' == '$ProjectName'`"><EnablePackageValidation>true</EnablePackageValidation>$strictProperty$baselineProperty</PropertyGroup></Project>"
}

function Invoke-DevOpsPack {
    param([string[]]$Arguments = @())

    try {
        $parsed = ConvertFrom-DevOpsArguments $Arguments
        $repoRoot = Get-RepoRoot
        $scriptsRoot = Get-ScriptsPath
        $configuration = [string]$parsed.Get('Configuration', 'Release')
        $outputDirectory = Join-Path (Get-ArtifactKindRoot -Kind package -RepoRoot $repoRoot) 'release'
        $packagingDirectory = Join-Path $scriptsRoot 'devops/packaging'
        $policyPath = Join-Path $packagingDirectory 'package-policy.json'
        . (Join-Path $packagingDirectory 'validate-package-archive.ps1')
        . (Join-Path $packagingDirectory 'validate-compression-package-archive.ps1')

        if (-not (Test-Path -LiteralPath $policyPath -PathType Leaf)) { throw "Package policy is missing: $policyPath" }
        $policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json -AsHashtable
        if ($policy.schemaVersion -ne 2 -or @($policy.packages).Count -eq 0 -or
            $null -eq $policy.releaseContract -or $policy.releaseContract.version -ne 1 -or
            $null -eq $policy.compressionContract -or $policy.compressionContract.version -ne 1) {
            throw 'Package policy must use schemaVersion 2 and contain packages and both version 1 release contracts.'
        }

        $validKinds = @('library', 'analyzer', 'razor-library', 'application')
        $allEntries = @($policy.packages)
        $ids = @($allEntries | ForEach-Object { [string]$_.packageId })
        if (@($ids | Sort-Object -Unique).Count -ne $ids.Count) { throw 'Package policy contains duplicate package IDs.' }
        $requiredReleaseIds = @($policy.releaseContract.requiredPackageIds | ForEach-Object { [string]$_ })
        if ($requiredReleaseIds.Count -eq 0 -or @($requiredReleaseIds | Sort-Object -Unique).Count -ne $requiredReleaseIds.Count) {
            throw 'Release contract requires a non-empty unique requiredPackageIds list.'
        }
        $missingReleasePackages = @($requiredReleaseIds | Where-Object { $_ -cnotin $ids })
        $unexpectedReleasePackages = @($ids | Where-Object { $_ -cnotin $requiredReleaseIds })
        if ($missingReleasePackages.Count -gt 0 -or $unexpectedReleasePackages.Count -gt 0) {
            throw "Package policy package set differs from the release contract. Missing [$($missingReleasePackages -join ', ')], unexpected [$($unexpectedReleasePackages -join ', ')]."
        }

        $compressionContract = Get-CompressionPackageDependencyContract -Policy $policy
        $compressionIds = @($compressionContract.requiredPackageIds | ForEach-Object { [string]$_ })
        if ($compressionIds -notcontains [string]$compressionContract.corePackageId -or
            @($compressionIds | Sort-Object -Unique).Count -ne $compressionIds.Count) {
            throw 'Compression contract requires unique package IDs including its Core package.'
        }
        $compressionGroupIds = @($allEntries | Where-Object { @($_.packageGroups) -ccontains [string]$compressionContract.packageGroup } |
            ForEach-Object { [string]$_.packageId } | Sort-Object -CaseSensitive)
        $expectedCompressionIds = @($compressionIds | Sort-Object -CaseSensitive)
        if (Compare-Object -ReferenceObject $expectedCompressionIds -DifferenceObject $compressionGroupIds -CaseSensitive) {
            throw 'Package compression group membership does not match its contract.'
        }
        $projectMetadataById = @{}
        foreach ($entry in $allEntries) {
            if ([string]::IsNullOrWhiteSpace($entry.packageId) -or [string]::IsNullOrWhiteSpace($entry.projectPath)) {
                throw 'Every package policy entry requires packageId and projectPath.'
            }
            if ($entry.kind -notin $validKinds) { throw "Unsupported package kind '$($entry.kind)' for $($entry.packageId)." }
            if ($null -eq $entry.expectedFrameworks) { throw "Package $($entry.packageId) requires expectedFrameworks." }
            $frameworks = @($entry.expectedFrameworks | ForEach-Object { [string]$_ })
            if ($entry.kind -eq 'analyzer' -and $frameworks.Count -ne 0) {
                throw "Analyzer package $($entry.packageId) must declare no library TFMs."
            }
            if ($entry.kind -ne 'analyzer' -and $frameworks.Count -eq 0) {
                throw "Package $($entry.packageId) must declare at least one target framework."
            }
            foreach ($property in @('enableApiCompatibility', 'requireXmlDocs', 'requireSymbols')) {
                if ($entry[$property] -isnot [bool]) { throw "Package $($entry.packageId) requires a Boolean '$property' policy value." }
            }
            Assert-PackageBaselinePolicy -Entry $entry
            $project = Join-Path $repoRoot ([string]$entry.projectPath)
            if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Package policy project does not exist: $($entry.projectPath)" }
            $projectMetadata = Get-PackageProjectMetadata -ProjectPath $project
            if ($projectMetadata.packageId -cne $entry.packageId) {
                throw "Policy package ID '$($entry.packageId)' does not match project package ID '$($projectMetadata.packageId)' in $project."
            }
            if ([string]::IsNullOrWhiteSpace($projectMetadata.version)) { throw "Project has no explicit Version for $($entry.packageId)." }
            $projectMetadataById[[string]$entry.packageId] = $projectMetadata
            if ($entry.enableApiCompatibility -and $entry.kind -notin @('analyzer', 'application')) {
                if ($null -eq $entry.baselineVersion -and [string]$projectMetadata.version -notmatch '-') {
                    Write-Info "$($entry.packageId) has no published baseline; SDK package validation will run without baseline comparison."
                }
            }
        }

        $coreEntry = @($allEntries | Where-Object { [string]$_.packageId -ceq [string]$compressionContract.corePackageId }) | Select-Object -First 1
        if ($null -eq $coreEntry) { throw "Compression contract Core package '$($compressionContract.corePackageId)' is not declared." }
        Assert-CompressionCoreVersionContract -Contract $compressionContract -CoreVersion ([string]$projectMetadataById[[string]$coreEntry.packageId].version)

        $selectedEntries = if ($parsed.Has('CompressionOnly')) {
            @($allEntries | Where-Object { @($_.packageGroups) -ccontains [string]$compressionContract.packageGroup })
        } else { $allEntries }
        if ($selectedEntries.Count -eq 0) { throw 'Package policy selection produced no projects.' }
        if ($parsed.Has('ValidatePackages') -and $parsed.Has('CompressionOnly')) {
            throw '-ValidatePackages requires the complete shipping package set and cannot be combined with -CompressionOnly.'
        }
        $selectedPackageIds = @($selectedEntries | ForEach-Object { [string]$_.packageId })
        $missingCompressionPackages = @($compressionContract.requiredPackageIds | Where-Object { [string]$_ -cnotin $selectedPackageIds })
        if ($missingCompressionPackages.Count -gt 0) {
            throw "Package policy selection omitted required compression package(s): $($missingCompressionPackages -join ', ')."
        }

        if ($parsed.Has('Clean') -and (Test-Path -LiteralPath $outputDirectory)) {
            Remove-OwnedArtifactPath -Path $outputDirectory -ArtifactRoot (Get-ArtifactRoot -RepoRoot $repoRoot)
        }
        [void][System.IO.Directory]::CreateDirectory($outputDirectory)

        Write-Heading "Packing $($selectedEntries.Count) policy packages"
        $expectedVersions = @{}
        foreach ($entry in $selectedEntries) {
            $project = Join-Path $repoRoot ([string]$entry.projectPath)
            $projectMetadata = Get-PackageProjectMetadata -ProjectPath $project
            $expectedVersions[[string]$entry.packageId] = [string]$projectMetadata.version
            $propsPath = $null

            $packArguments = @(
                'pack', $project, '-c', $configuration, '--output', $outputDirectory,
                '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '--tl:off',
                "-p:IncludeSymbols=$([string]([bool]$entry.requireSymbols).ToString().ToLowerInvariant())",
                '-p:SymbolPackageFormat=snupkg'
            )
            if ($entry.enableApiCompatibility -and $entry.kind -notin @('analyzer', 'application')) {
                $propsDirectory = Join-Path (Get-ArtifactKindRoot -Kind temp -RepoRoot $repoRoot) 'package-validation'
                [void][IO.Directory]::CreateDirectory($propsDirectory)
                $propsPath = Join-Path $propsDirectory ("baseline-" + [Guid]::NewGuid().ToString('N') + '.props')
                $projectName = [IO.Path]::GetFileNameWithoutExtension($project)
                $props = New-PackageValidationProperties -Entry $entry -ProjectName $projectName
                [IO.File]::WriteAllText($propsPath, $props, [Text.UTF8Encoding]::new($false))
                $packArguments += "-p:CustomAfterMicrosoftCommonProps=$propsPath"
            }
            if ($parsed.Has('NoBuild')) { $packArguments += '--no-build' }
            try {
                Invoke-DotNet $packArguments | ForEach-Object { Write-Host $_ }
            } finally {
                if ($null -ne $propsPath -and (Test-Path -LiteralPath $propsPath)) {
                    Remove-Item -LiteralPath $propsPath -Force
                }
            }

            $packagePath = Join-Path $outputDirectory "$($entry.packageId).$($projectMetadata.version).nupkg"
            if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
                throw "Packing $($entry.projectPath) did not produce the expected $($entry.packageId) $($projectMetadata.version) package."
            }
        }

        $actualPackages = @(Get-ChildItem -LiteralPath $outputDirectory -File -Filter '*.nupkg' | Sort-Object Name)
        $selectedIds = @($selectedEntries | ForEach-Object { [string]$_.packageId })
        $seenIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($package in $actualPackages) {
            $archiveInfo = Get-PackageArchiveInfo -Path $package.FullName
            if ($archiveInfo.id -notin $selectedIds) {
                throw "Unrepresented shipping package '$($archiveInfo.id)' was produced in $($package.FullName)."
            }
            if (-not $seenIds.Add([string]$archiveInfo.id)) {
                throw "More than one package version was produced for '$($archiveInfo.id)'; clean the package output and retry."
            }
            if ($archiveInfo.version -cne $expectedVersions[[string]$archiveInfo.id]) {
                throw "Unexpected version $($archiveInfo.version) for policy package $($archiveInfo.id); expected $($expectedVersions[[string]$archiveInfo.id])."
            }
        }
        foreach ($entry in $selectedEntries) {
            if (-not $seenIds.Contains([string]$entry.packageId)) { throw "Policy package $($entry.packageId) was not emitted." }
        }

        foreach ($entry in $selectedEntries) {
            $version = $expectedVersions[[string]$entry.packageId]
            $packagePath = Join-Path $outputDirectory "$($entry.packageId).$version.nupkg"
            $symbolPath = Join-Path $outputDirectory "$($entry.packageId).$version.snupkg"
            if (-not (Test-Path -LiteralPath $symbolPath -PathType Leaf)) { $symbolPath = '' }
            [void](Test-PackageArchive -PackagePath $packagePath -SymbolPath $symbolPath -PolicyEntry $entry -RepoRoot $repoRoot)

            if ($entry.packageId -in @($compressionContract.requiredPackageIds | Where-Object { [string]$_ -cne [string]$compressionContract.corePackageId })) {
                $expectedDependencyFrameworks = if ($entry.Contains('expectedDependencyFrameworks')) {
                    @($entry.expectedDependencyFrameworks | ForEach-Object { [string]$_ })
                } else {
                    @($entry.expectedFrameworks | ForEach-Object { [string]$_ })
                }
                Assert-CompressionPackageDependencyRange -PackageId ([string]$entry.packageId) `
                    -PackagePath $packagePath -ExpectedFrameworks $expectedDependencyFrameworks -Contract $compressionContract
            }
        }

        $allSymbols = @(Get-ChildItem -LiteralPath $outputDirectory -File -Filter '*.snupkg' | Sort-Object Name)
        foreach ($symbol in $allSymbols) {
            $expectedName = [IO.Path]::GetFileNameWithoutExtension($symbol.Name)
            if ($expectedName -notmatch '^(?<id>.+)\.(?<version>\d+\.\d+\.\d+(?:[-+].+)?)$') {
                throw "Unexpected symbol package name '$($symbol.Name)'."
            }
            $symbolId = $Matches.id
            $symbolVersion = $Matches.version
            $symbolPolicy = @($selectedEntries | Where-Object { $_.packageId -ceq $symbolId })
            if ($symbolPolicy.Count -ne 1 -or -not [bool]$symbolPolicy[0].requireSymbols -or
                $symbolVersion -cne $expectedVersions[$symbolId]) {
                throw "Unrepresented symbol package '$($symbol.Name)' was produced."
            }
        }

        $git = Get-ArtifactGitContext -RepoRoot $repoRoot
        $packages = @(Get-ChildItem -LiteralPath $outputDirectory -File | Where-Object { $_.Extension -in @('.nupkg', '.snupkg') } | Sort-Object Name)
        $entries = foreach ($package in $packages) {
            $name = [System.IO.Path]::GetFileNameWithoutExtension($package.Name)
            $match = [regex]::Match($name, '^(?<id>.+)\.(?<version>\d+\.\d+\.\d+(?:[-+].+)?)$')
            [ordered]@{
                packageId = if ($match.Success) { $match.Groups['id'].Value } else { $name }
                version = if ($match.Success) { $match.Groups['version'].Value } else { '' }
                path = $package.Name
                size = $package.Length
                sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $package.FullName).Hash.ToLowerInvariant()
            }
        }
        Write-AtomicJsonFile -Path (Join-Path $outputDirectory 'manifest.json') -Value ([ordered]@{
            schemaVersion = 1
            generatedAtUtc = [DateTime]::UtcNow.ToString('O')
            sourceCommit = $git.commit
            configuration = $configuration
            packages = @($entries)
        })

        if ($parsed.Has('ValidatePackages')) {
            . (Join-Path $packagingDirectory 'validate-package-consumers.ps1')
            Test-PackageConsumerSet -PackageDirectory $outputDirectory -RepoRoot $repoRoot -Scope all
        } elseif ($parsed.Has('ValidateCompression')) {
            . (Join-Path $packagingDirectory 'validate-compression.ps1')
            Test-CompressionPackages -PackageDirectory $outputDirectory -RepoRoot $repoRoot
        }
        Write-Success "Packages written to: $outputDirectory"
        return 0
    } catch {
        Write-Failure "Pack command failed: $($_.Exception.Message)"
        return 1
    }
}
