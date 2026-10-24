$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'validate-package-archive.ps1')
. (Join-Path $PSScriptRoot 'validate-compression-package-archive.ps1')

function Get-ConsumerPackageManifest {
    param([string]$PackageDirectory)

    $manifestPath = Join-Path $PackageDirectory 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Package manifest is missing: $manifestPath"
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    if ($manifest.schemaVersion -ne 1 -or [string]::IsNullOrWhiteSpace([string]$manifest.sourceCommit)) {
        throw "Package manifest has an unsupported schema or missing sourceCommit: $manifestPath"
    }

    $entries = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($package in @($manifest.packages | Where-Object { [string]$_.path -match '\.nupkg$' })) {
        $id = [string]$package.packageId
        if ([string]::IsNullOrWhiteSpace($id) -or $entries.ContainsKey($id)) {
            throw "Package manifest has an empty or duplicate package ID '$id'."
        }
        $path = Join-Path $PackageDirectory ([string]$package.path)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Manifest package archive is missing: $path" }
        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
        if ($actualHash -cne ([string]$package.sha256).ToLowerInvariant()) {
            throw "Package manifest SHA-256 does not match $id $($package.version)."
        }
        $entries.Add($id, [pscustomobject]@{
            packageId = $id
            version = [string]$package.version
            path = $path
            relativePath = [string]$package.path
            sha256 = ([string]$package.sha256).ToLowerInvariant()
        })
    }
    return [pscustomobject]@{ Manifest = $manifest; Entries = $entries }
}

function Get-ConsumerPackageEntry {
    param(
        [Parameter(Mandatory = $true)][object]$PackageIndex,
        [Parameter(Mandatory = $true)][string]$PackageId
    )
    if (-not $PackageIndex.Entries.ContainsKey($PackageId)) {
        throw "Package '$PackageId' is absent from the generated package manifest."
    }
    return $PackageIndex.Entries[$PackageId]
}

function Get-ConsumerPackageSources {
    $sources = @(& dotnet nuget list source --format short 2>&1 | ForEach-Object {
        if ([string]$_ -match '^E\s+(.+)$') { $Matches[1].Trim() }
    })
    if ($LASTEXITCODE -ne 0 -or $sources.Count -eq 0) {
        throw 'Cannot discover enabled NuGet feeds for isolated consumers.'
    }
    return $sources
}

function Write-ConsumerNuGetConfig {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$PackageDirectory,
        [Parameter(Mandatory = $true)][string[]]$ExternalSources,
        [Parameter(Mandatory = $true)][object]$Policy,
        [switch]$AllowExternalLeanCorpus
    )

    $local = [Security.SecurityElement]::Escape([IO.Path]::GetFullPath($PackageDirectory))
    $feedNodes = [Collections.Generic.List[string]]::new()
    $feedNodes.Add("<add key=`"local`" value=`"$local`" />")
    $localPatterns = [Collections.Generic.List[string]]::new()
    foreach ($entry in @($Policy.packages)) {
        $localPatterns.Add('<package pattern="' + [Security.SecurityElement]::Escape([string]$entry.packageId) + '"/>')
    }
    $mappingNodes = [Collections.Generic.List[string]]::new()
    $mappingNodes.Add('<packageSource key="local">' + ($localPatterns -join '') + '</packageSource>')
    $coreId = [Security.SecurityElement]::Escape([string]$Policy.compressionContract.corePackageId)
    for ($i = 0; $i -lt $ExternalSources.Count; $i++) {
        $key = "feed$i"
        $url = [Security.SecurityElement]::Escape([string]$ExternalSources[$i])
        $feedNodes.Add("<add key=`"$key`" value=`"$url`" />")
        $externalPatterns = '<package pattern="*"/>'
        if ($AllowExternalLeanCorpus) {
            # The negative compression probe must be able to obtain the older Core
            # baseline from a legitimate remote source while normal restores use
            # the generated local package when its exact version is available.
            $externalPatterns += '<package pattern="' + $coreId + '"/>'
        }
        $mappingNodes.Add("<packageSource key=`"$key`">$externalPatterns</packageSource>")
    }
    $mapping = $mappingNodes -join ''
    $xml = "<configuration><packageSources><clear/>$($feedNodes -join '')</packageSources><packageSourceMapping>$mapping</packageSourceMapping></configuration>"
    [IO.File]::WriteAllText($Path, $xml, [Text.UTF8Encoding]::new($false))
}

function Get-ConsumerProjectTemplate {
    return [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Consumer.csproj.template'))
}

function Get-ConsumerScenarioSource {
    param([Parameter(Mandatory = $true)][string]$Kind)

    $validKinds = @('core', 'text', 'sourcegen', 'abstractions', 'server-core', 'server-aspnetcore', 'grpc', 'studio', 'orchard', 'server-local-launcher')
    if ($Kind -notin $validKinds) { throw "Unknown package consumer source kind '$Kind'." }
    $sourcePath = Join-Path $PSScriptRoot "consumer-sources/$Kind.cs"
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Package consumer source fixture is missing: $sourcePath"
    }
    return [IO.File]::ReadAllText($sourcePath)
}

function Get-ConsumerInternalPackageClosure {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][object]$Policy,
        [Parameter(Mandatory = $true)][string[]]$PackageIds
    )

    $byId = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $byProject = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in @($Policy.packages)) {
        $byId.Add([string]$entry.packageId, $entry)
        $projectPath = [IO.Path]::GetFullPath((Join-Path $RepoRoot ([string]$entry.projectPath)))
        $byProject.Add($projectPath, $entry)
    }

    $resolved = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $visit = {
        param([string]$PackageId)
        if (-not $byId.ContainsKey($PackageId) -or -not $resolved.Add($PackageId)) { return }
        $entry = $byId[$PackageId]
        $projectPath = [IO.Path]::GetFullPath((Join-Path $RepoRoot ([string]$entry.projectPath)))
        [xml]$project = [IO.File]::ReadAllText($projectPath)
        foreach ($reference in $project.SelectNodes("//*[local-name()='PackageReference']")) {
            $privateAssets = [string]$reference.GetAttribute('PrivateAssets')
            if ([string]::IsNullOrWhiteSpace($privateAssets)) {
                $privateAssets = [string]$reference.SelectSingleNode("*[local-name()='PrivateAssets']")?.InnerText
            }
            $excludeAssets = [string]$reference.GetAttribute('ExcludeAssets')
            if ([string]::IsNullOrWhiteSpace($excludeAssets)) {
                $excludeAssets = [string]$reference.SelectSingleNode("*[local-name()='ExcludeAssets']")?.InnerText
            }
            if (@(($privateAssets -split '[,;]' | ForEach-Object { $_.Trim() }) -contains 'all') -or
                @(($excludeAssets -split '[,;]' | ForEach-Object { $_.Trim() }) -contains 'all')) {
                continue
            }
            $dependencyId = [string]$reference.GetAttribute('Include')
            if ([string]::IsNullOrWhiteSpace($dependencyId)) { $dependencyId = [string]$reference.GetAttribute('Update') }
            foreach ($id in @($dependencyId -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
                if ($byId.ContainsKey($id)) { & $visit $id }
            }
        }
        foreach ($reference in $project.SelectNodes("//*[local-name()='ProjectReference']")) {
            $include = [string]$reference.GetAttribute('Include')
            foreach ($relativePath in @($include -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
                $referencedPath = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($projectPath)) $relativePath))
                $referencedEntry = $null
                if ($byProject.TryGetValue($referencedPath, [ref]$referencedEntry)) { & $visit ([string]$referencedEntry.packageId) }
            }
        }
    }
    foreach ($id in $PackageIds) { & $visit $id }
    return @($resolved | Sort-Object -Culture invariant)
}

function Get-ConsumerScenarioDefinitions {
    param(
        [ValidateSet('all', 'compression')][string]$Scope,
        [Parameter(Mandatory = $true)][object]$Policy,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $templates = @{
        core = @{ Name = 'core'; Kind = 'core'; Sdk = 'Microsoft.NET.Sdk'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'core'; IncludeCore = $false; UseCoreFrameworks = $false; VerifyClosure = $true }
        compression = @{ Name = 'compression'; Kind = 'compression'; Sdk = 'Microsoft.NET.Sdk'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'compression'; IncludeCore = $true; UseCoreFrameworks = $false; VerifyClosure = $true }
        text = @{ Name = 'text'; Kind = 'text'; Sdk = 'Microsoft.NET.Sdk'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'single'; IncludeCore = $false; UseCoreFrameworks = $false; VerifyClosure = $true }
        sourcegen = @{ Name = 'sourcegen'; Kind = 'sourcegen'; Sdk = 'Microsoft.NET.Sdk'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'single'; IncludeCore = $true; UseCoreFrameworks = $true; VerifyClosure = $true }
        'server-abstractions' = @{ Name = 'server-abstractions'; Kind = 'abstractions'; Sdk = 'Microsoft.NET.Sdk'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'single'; IncludeCore = $false; UseCoreFrameworks = $false; VerifyClosure = $true }
        'server-core' = @{ Name = 'server-core'; Kind = 'server-core'; Sdk = 'Microsoft.NET.Sdk'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'single'; IncludeCore = $false; UseCoreFrameworks = $false; VerifyClosure = $true }
        'server-aspnetcore' = @{ Name = 'server-aspnetcore'; Kind = 'server-aspnetcore'; Sdk = 'Microsoft.NET.Sdk.Web'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'single'; IncludeCore = $false; UseCoreFrameworks = $false; VerifyClosure = $true }
        'server-grpc' = @{ Name = 'server-grpc'; Kind = 'grpc'; Sdk = 'Microsoft.NET.Sdk.Web'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'single'; IncludeCore = $false; UseCoreFrameworks = $false; VerifyClosure = $true }
        'server-local' = @{ Name = 'server-local'; Kind = 'server-local-launcher'; Sdk = 'Microsoft.NET.Sdk'; ExtraProperties = ''; ExternalReferences = @(); RuntimeKind = 'server-local'; IncludeCore = $false; UseCoreFrameworks = $false; VerifyClosure = $false }
        studio = @{ Name = 'studio'; Kind = 'studio'; Sdk = 'Microsoft.NET.Sdk.Web'; ExtraProperties = '<AddRazorSupportForMvc>true</AddRazorSupportForMvc>'; ExternalReferences = @(); RuntimeKind = 'single'; IncludeCore = $false; UseCoreFrameworks = $false; VerifyClosure = $true }
        'orchard-search' = @{ Name = 'orchard-search'; Kind = 'orchard'; Sdk = 'Microsoft.NET.Sdk.Web'; ExtraProperties = ''; ExternalReferences = @('OrchardCore.Application.Cms.Targets'); RuntimeKind = 'single'; IncludeCore = $false; UseCoreFrameworks = $false; VerifyClosure = $true }
    }

    $coreId = [string]$Policy.compressionContract.corePackageId
    $coreEntry = @($Policy.packages | Where-Object { [string]$_.packageId -ceq $coreId }) | Select-Object -First 1
    if ($null -eq $coreEntry -or [string]::IsNullOrWhiteSpace([string]$coreEntry.baselineVersion)) {
        throw "Package policy Core package '$coreId' requires a baselineVersion for the compression compatibility consumer."
    }
    $allPackageIds = @($Policy.packages | ForEach-Object { [string]$_.packageId })
    $compressionPackageIds = @($Policy.compressionContract.requiredPackageIds |
        ForEach-Object { [string]$_ } | Where-Object { $_ -cne $coreId })
    $compressionFrameworks = @($Policy.packages |
        Where-Object { [string]$_.packageId -cin $compressionPackageIds } |
        ForEach-Object { @($_.expectedFrameworks | ForEach-Object { [string]$_ }) } | Sort-Object -Unique)
    $scenarios = [Collections.Generic.List[object]]::new()
    foreach ($entry in @($Policy.packages)) {
        $role = [string]$entry.consumerScenario
        if (-not $templates.ContainsKey($role)) { throw "Package $($entry.packageId) has unknown consumerScenario role '$role'." }
        if ($Scope -eq 'compression' -and $role -cne 'compression') { continue }
        $template = $templates[$role]
        if ($role -ceq 'compression' -and [string]$entry.packageId -cnotin @($Policy.compressionContract.requiredPackageIds | Where-Object { [string]$_ -cne $coreId })) {
            throw "Compression consumer package $($entry.packageId) is not listed in the compression contract."
        }
        $frameworks = @($entry.expectedFrameworks | ForEach-Object { [string]$_ })
        if ($frameworks.Count -eq 0 -and $template.UseCoreFrameworks) {
            $frameworks = @($coreEntry.expectedFrameworks | ForEach-Object { [string]$_ })
        }
        if ($frameworks.Count -eq 0) { throw "Package $($entry.packageId) has no consumer target frameworks in its package policy." }

        $packageIds = if ($template.IncludeCore -and [string]$entry.packageId -cne $coreId) { @($coreId, [string]$entry.packageId) } else { @([string]$entry.packageId) }
        $exactInternalIds = if ($template.VerifyClosure) { @(Get-ConsumerInternalPackageClosure -RepoRoot $RepoRoot -Policy $Policy -PackageIds $packageIds) } else { @() }
        $name = if ($role -ceq 'compression') { "compression-$(([string]$entry.packageId).Split('.')[-1])" } else { [string]$template.Name }
        $kind = if ($role -ceq 'compression') { $name } else { [string]$template.Kind }
        foreach ($framework in $frameworks) {
            $scenarios.Add([pscustomobject]@{
                Name = $name; PackageId = [string]$entry.packageId; PackageIds = $packageIds; ExactInternalIds = $exactInternalIds
                Kind = $kind; Framework = $framework; Sdk = [string]$template.Sdk; ExtraProperties = [string]$template.ExtraProperties
                ExternalReferences = @($template.ExternalReferences); RuntimeKind = [string]$template.RuntimeKind
                CorePackageId = $coreId; CoreBaselineVersion = [string]$coreEntry.baselineVersion
                AnalyzerPackageId = if ([string]$entry.kind -ceq 'analyzer') { [string]$entry.packageId } else { $null }
                CompressionContract = $Policy.compressionContract; CompressionExpectedFrameworks = $compressionFrameworks
                PolicyPackageIds = $allPackageIds
            })
        }
    }
    return $scenarios
}

function Get-CentralConsumerPackageVersion {
    param([string]$RepoRoot, [string]$PackageId)
    $path = Join-Path $RepoRoot 'Directory.Packages.props'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Central package versions are missing: $path" }
    [xml]$document = Get-Content -LiteralPath $path -Raw
    $node = $document.Project.ItemGroup.PackageVersion | Where-Object { [string]$_.Include -ceq $PackageId } | Select-Object -First 1
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace([string]$node.Version)) {
        throw "No centrally managed external package version is declared for $PackageId."
    }
    return [string]$node.Version
}

function Get-ConsumerPackageReferenceXml {
    param([object]$Scenario, [object]$PackageIndex, [string]$RepoRoot)

    $nodes = [Collections.Generic.List[string]]::new()
    foreach ($id in @($Scenario.PackageIds)) {
        $entry = Get-ConsumerPackageEntry -PackageIndex $PackageIndex -PackageId $id
        if ($id -ceq [string]$Scenario.AnalyzerPackageId) {
            $nodes.Add("    <PackageReference Include=`"$id`" Version=`"[$($entry.version)]`" OutputItemType=`"Analyzer`" ReferenceOutputAssembly=`"false`" PrivateAssets=`"all`" />")
        } else {
            $nodes.Add("    <PackageReference Include=`"$id`" Version=`"[$($entry.version)]`" />")
        }
    }
    foreach ($id in @($Scenario.ExternalReferences)) {
        $version = Get-CentralConsumerPackageVersion -RepoRoot $RepoRoot -PackageId $id
        $nodes.Add("    <PackageReference Include=`"$id`" Version=`"[$version]`" />")
    }
    return $nodes -join "`n"
}

function Get-ConsumerInternalPackageId {
    param([string]$LibraryKey, [object]$PackageIndex)
    $id = $LibraryKey.Split('/')[0]
    if ($PackageIndex.Entries.ContainsKey($id)) {
        return $id
    }
    return $null
}

function Get-ConsumerCachedArchivePath {
    param([string]$CacheDirectory, [string]$PackageId, [string]$Version)
    $packageRoot = Join-Path $CacheDirectory (Join-Path $PackageId.ToLowerInvariant() $Version.ToLowerInvariant())
    if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) { throw "Isolated cache does not contain $PackageId ${Version}: $packageRoot" }
    $archive = @(Get-ChildItem -LiteralPath $packageRoot -File -Filter '*.nupkg' | Where-Object {
        $_.Name -ceq "$($PackageId.ToLowerInvariant()).$($Version.ToLowerInvariant()).nupkg"
    } | Select-Object -First 1)
    if ($archive.Count -ne 1) { throw "Isolated cache archive is missing for $PackageId ${Version}: $packageRoot" }
    return $archive[0].FullName
}

function Invoke-ConsumerCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$ScenarioDirectory,
        [TimeSpan]$Timeout = ([TimeSpan]::FromMinutes(5)),
        [scriptblock]$OnProgress
    )
    $stdout = Join-Path $ScenarioDirectory "$Name.stdout.log"
    $stderr = Join-Path $ScenarioDirectory "$Name.stderr.log"
    $parameters = @{
        FileName = 'dotnet'
        Arguments = $Arguments
        WorkingDirectory = $WorkingDirectory
        StdOutPath = $stdout
        StdErrPath = $stderr
        CaptureOutput = $true
        MirrorOutput = $false
        Timeout = $Timeout
        ProgressIntervalSeconds = 1
    }
    if ($null -ne $OnProgress) { $parameters.OnProgress = $OnProgress }
    $result = Invoke-ProcessWithLifecycle @parameters
    if ($result.TimedOut) { throw "$Name timed out; see $stdout and $stderr." }
    return $result
}

function Invoke-PackageConsumerScenario {
    param(
        [Parameter(Mandatory = $true)][object]$Scenario,
        [Parameter(Mandatory = $true)][object]$PackageIndex,
        [Parameter(Mandatory = $true)][string]$PackageDirectory,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string[]]$ExternalSources,
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)][string]$CacheRoot,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][Collections.Generic.List[object]]$Results
    )

    $primary = Get-ConsumerPackageEntry -PackageIndex $PackageIndex -PackageId $Scenario.PackageId
    $directoryName = "$($Scenario.Name)-$($Scenario.Framework)"
    $scenarioDirectory = Join-Path $ConsumerRoot $directoryName
    [void][IO.Directory]::CreateDirectory($scenarioDirectory)
    $cache = Join-Path $CacheRoot $directoryName
    [void][IO.Directory]::CreateDirectory($cache)
    $row = [ordered]@{
        scenario = [string]$Scenario.Name
        packageId = [string]$primary.packageId
        packageVersion = [string]$primary.version
        packageSha256 = [string]$primary.sha256
        targetFramework = [string]$Scenario.Framework
        restore = 'not-run'
        build = 'not-run'
        runtime = 'not-run'
        verifiedPackages = @()
        workingDirectory = $scenarioDirectory
        cacheDirectory = $cache
        error = $null
    }
    try {
        if ($Scenario.Name -like 'compression-*') {
            Assert-CompressionPackageDependencyRange -PackageId $Scenario.PackageId `
                -PackagePath $primary.path -ExpectedFrameworks @($Scenario.CompressionExpectedFrameworks) `
                -Contract $Scenario.CompressionContract
        }
        $project = Join-Path $scenarioDirectory 'Consumer.csproj'
        $template = Get-ConsumerProjectTemplate
        $references = Get-ConsumerPackageReferenceXml -Scenario $Scenario -PackageIndex $PackageIndex -RepoRoot $RepoRoot
        $projectXml = $template.Replace('SDK_NAME', [string]$Scenario.Sdk)
        $projectXml = $projectXml.Replace('FRAMEWORK', [string]$Scenario.Framework)
        $projectXml = $projectXml.Replace('EXTRA_PROPERTIES', [string]$Scenario.ExtraProperties)
        $projectXml = $projectXml.Replace('PACKAGE_REFERENCES', $references)
        [IO.File]::WriteAllText($project, $projectXml, [Text.UTF8Encoding]::new($false))
        if ($projectXml -match '<ProjectReference\b') { throw 'Generated consumer must not use ProjectReference.' }
        $source = if ($Scenario.RuntimeKind -eq 'compression') {
            $compressionPrefix = [string]$Scenario.CorePackageId + '.Compression.'
            if (-not ([string]$Scenario.PackageId).StartsWith($compressionPrefix, [StringComparison]::Ordinal)) {
                throw "Compression package $($Scenario.PackageId) does not use the Core package compression naming convention '$compressionPrefix'."
            }
            $codec = [string]$Scenario.PackageId.Substring($compressionPrefix.Length)
            $registration = if ($codec -eq 'LZ4') { 'Lz4Compression' } else { "$codec`Compression" }
            $policy = if ($codec -eq 'LZ4') { 'Lz4' } else { $codec }
            $core = Get-ConsumerPackageEntry -PackageIndex $PackageIndex -PackageId ([string]$Scenario.CorePackageId)
            $programTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Program.cs.template'))
            $coreAssembly = ([version]$core.version).ToString() + '.0'
            $codecAssembly = ([version]$primary.version).ToString() + '.0'
            $program = $programTemplate.Replace('CORE_ASSEMBLY_VERSION', $coreAssembly)
            $program = $program.Replace('OPTIONAL_ASSEMBLY_VERSION', $codecAssembly)
            $program = $program.Replace('CODEC', $codec).Replace('REGISTRATION', $registration).Replace('POLICY', $policy)
            $program
        } else { Get-ConsumerScenarioSource -Kind ([string]$Scenario.Kind) }
        [IO.File]::WriteAllText((Join-Path $scenarioDirectory 'Program.cs'), $source, [Text.UTF8Encoding]::new($false))
        if ($Scenario.Kind -eq 'sourcegen') {
            if ($projectXml -notmatch 'OutputItemType="Analyzer"' -or $projectXml -notmatch 'ReferenceOutputAssembly="false"') {
                throw 'SourceGen consumer must reference the generator as a private analyser dependency.'
            }
        }
        if ($Scenario.Kind -eq 'studio') {
            $pages = Join-Path $scenarioDirectory 'Pages'
            [void][IO.Directory]::CreateDirectory($pages)
            [IO.File]::WriteAllText((Join-Path $pages 'Consumer.cshtml'), "@page `"/razor-smoke`"`n<h1>Razor package consumer</h1>`n", [Text.UTF8Encoding]::new($false))
        }

        $config = Join-Path $scenarioDirectory 'NuGet.Config'
        Write-ConsumerNuGetConfig -Path $config -PackageDirectory $PackageDirectory -ExternalSources $ExternalSources -Policy $PackageIndex.Policy
        $restore = Invoke-ConsumerCommand -Name 'restore' -Arguments @(
            'restore', $project, '--packages', $cache, '--configfile', $config,
            '--disable-build-servers', '-p:NuGetAudit=false'
        ) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
        if ($restore.ExitCode -ne 0) { throw "Consumer restore failed with exit code $($restore.ExitCode); see $($restore.StdOutPath) and $($restore.StdErrPath)." }
        $row.restore = 'passed'

        $assetsPath = Join-Path $scenarioDirectory 'obj/project.assets.json'
        if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) { throw "Restore did not create $assetsPath" }
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
        if ([string]$assets.project.restore.projectStyle -cne 'PackageReference') {
            throw "Consumer restore style was not PackageReference in $assetsPath."
        }
        $libraryKeys = @($assets.libraries.Keys)
        $verified = [Collections.Generic.List[object]]::new()
        $internalResolved = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($key in $libraryKeys) {
            $id = Get-ConsumerInternalPackageId -LibraryKey ([string]$key) -PackageIndex $PackageIndex
            if ($null -eq $id) { continue }
            $version = [string]$key.Split('/')[1]
            if ($internalResolved.ContainsKey($id) -and $internalResolved[$id] -cne $version) {
                throw "Consumer resolved multiple versions for internal package $id."
            }
            $internalResolved[$id] = $version
            $manifestEntry = Get-ConsumerPackageEntry -PackageIndex $PackageIndex -PackageId $id
            if ($manifestEntry.version -cne $version) {
                throw "Consumer resolved $id $version, but the package manifest records $($manifestEntry.version)."
            }
            $cachedArchive = Get-ConsumerCachedArchivePath -CacheDirectory $cache -PackageId $id -Version $version
            $cachedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $cachedArchive).Hash.ToLowerInvariant()
            if ($cachedHash -cne $manifestEntry.sha256) {
                throw "Consumer cache archive hash does not match the manifest for $id $version."
            }
            $verified.Add([ordered]@{ packageId = $id; version = $version; sha256 = $cachedHash })
        }
        foreach ($id in @($Scenario.PackageIds)) {
            if (-not $internalResolved.ContainsKey([string]$id)) { throw "Consumer assets do not contain required package $id." }
        }
        if (@($Scenario.ExactInternalIds).Count -gt 0) {
            $actualIds = @($internalResolved.Keys | Sort-Object -Culture invariant)
            $expectedIds = @($Scenario.ExactInternalIds | Sort-Object -Culture invariant)
            if (Compare-Object -ReferenceObject $expectedIds -DifferenceObject $actualIds -CaseSensitive) {
                throw "Unexpected internal package graph for $($Scenario.Name): expected [$($expectedIds -join ', ')], found [$($actualIds -join ', ')]."
            }
        }
        $row.verifiedPackages = @($verified)

        $build = Invoke-ConsumerCommand -Name 'build' -Arguments @(
            'build', $project, '-c', 'Release', '--no-restore', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false'
        ) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
        if ($build.ExitCode -ne 0) { throw "Consumer build failed with exit code $($build.ExitCode); see $($build.StdOutPath) and $($build.StdErrPath)." }
        $row.build = 'passed'
        $consumerDll = Join-Path $scenarioDirectory "bin/Release/$($Scenario.Framework)/Consumer.dll"

        switch ($Scenario.RuntimeKind) {
            'core' {
                $index = Join-Path $scenarioDirectory 'index'
                $write = Invoke-ConsumerCommand -Name 'runtime-write' -Arguments @($consumerDll, 'write', $index) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
                if ($write.ExitCode -ne 0) { throw "Core writer process failed with exit code $($write.ExitCode); see $($write.StdOutPath) and $($write.StdErrPath)." }
                $read = Invoke-ConsumerCommand -Name 'runtime-read' -Arguments @($consumerDll, 'read', $index) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
                if ($read.ExitCode -ne 0) { throw "Core reader process failed with exit code $($read.ExitCode); see $($read.StdOutPath) and $($read.StdErrPath)." }
                if (Test-Path -LiteralPath $index) {
                    [IO.Directory]::Delete($index, $true)
                    if (Test-Path -LiteralPath $index) { throw "Core index cleanup failed for $index." }
                }
                $row.runtime = 'passed-write-read-cleanup'
            }
            'compression' {
                $index = Join-Path $scenarioDirectory 'index'
                foreach ($mode in @('write', 'read')) {
                    $process = Invoke-ConsumerCommand -Name "runtime-$mode" -Arguments @($consumerDll, $mode, $index) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
                    if ($process.ExitCode -ne 0) { throw "Compression $mode process failed with exit code $($process.ExitCode); see $($process.StdOutPath) and $($process.StdErrPath)." }
                }
                [IO.Directory]::Delete($index, $true)
                if (Test-Path -LiteralPath $index) { throw "Compression index cleanup failed for $index." }

                $coreEntry = Get-ConsumerPackageEntry -PackageIndex $PackageIndex -PackageId ([string]$Scenario.CorePackageId)
                $negativeConfig = Join-Path $scenarioDirectory 'Negative.NuGet.Config'
                Write-ConsumerNuGetConfig -Path $negativeConfig -PackageDirectory $PackageDirectory -ExternalSources $ExternalSources -Policy $PackageIndex.Policy -AllowExternalLeanCorpus
                $projectXml = Get-Content -LiteralPath $project -Raw
                $baselineVersion = [string]$Scenario.CoreBaselineVersion
                if ([string]::IsNullOrWhiteSpace($baselineVersion) -or $baselineVersion -ceq [string]$coreEntry.version) {
                    throw "Compression package policy requires a distinct published baseline for Core package $($Scenario.CorePackageId)."
                }
                $negativeReference = 'Include="' + [string]$Scenario.CorePackageId + '" Version="[' + [string]$coreEntry.version + ']"'
                $negativeReplacement = 'Include="' + [string]$Scenario.CorePackageId + '" Version="[' + $baselineVersion + ']"'
                $negativeXml = $projectXml.Replace($negativeReference, $negativeReplacement)
                [IO.File]::WriteAllText($project, $negativeXml, [Text.UTF8Encoding]::new($false))
                $negative = Invoke-ConsumerCommand -Name 'negative-restore' -Arguments @(
                    'restore', $project, '--packages', $cache, '--configfile', $negativeConfig,
                    '--disable-build-servers', '-p:NuGetAudit=false'
                ) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
                $negativeText = ''
                foreach ($log in @($negative.StdOutPath, $negative.StdErrPath)) {
                    if (Test-Path -LiteralPath $log) { $negativeText += [IO.File]::ReadAllText($log) + "`n" }
                }
                if ($negative.ExitCode -eq 0 -or $negativeText -notmatch 'NU1605|NU1107') {
                    throw "Expected the incompatible Core $baselineVersion restore to fail with NU1605/NU1107; see $($negative.StdOutPath) and $($negative.StdErrPath)."
                }
                $row.runtime = "passed-round-trip-marker-and-rejects-core-$baselineVersion"
            }
            'server-local' {
                $localId = [string]$Scenario.PackageId
                $local = Get-ConsumerPackageEntry -PackageIndex $PackageIndex -PackageId $localId
                $localDirectory = Join-Path $cache (Join-Path $local.packageId.ToLowerInvariant() $local.version.ToLowerInvariant())
                $applicationAssets = Join-Path $localDirectory "lib/$($Scenario.Framework)"
                $localDll = Join-Path $applicationAssets "$localId.dll"
                if (-not (Test-Path -LiteralPath $localDll -PathType Leaf)) { throw "Server.Local package assembly is missing from the isolated cache: $localDll" }
                $deps = Join-Path $scenarioDirectory "bin/Release/$($Scenario.Framework)/Consumer.deps.json"
                $runtimeConfig = Join-Path $scenarioDirectory "bin/Release/$($Scenario.Framework)/Consumer.runtimeconfig.json"
                $dataRoot = Join-Path $scenarioDirectory 'server-local-data'
                $arguments = @('exec', '--depsfile', $deps, '--runtimeconfig', $runtimeConfig, '--additionalprobingpath', $cache, $localDll,
                    '--urls', 'http://127.0.0.1:0', '--LeanCorpus:DataRoot', $dataRoot)
                $ready = [pscustomobject]@{ value = $false }
                $stdoutPath = Join-Path $scenarioDirectory 'runtime.stdout.log'
                $stderrPath = Join-Path $scenarioDirectory 'runtime.stderr.log'
                $onProgress = {
                    param($process, $elapsed)
                    $output = ''
                    foreach ($path in @($stdoutPath, $stderrPath)) {
                        if (Test-Path -LiteralPath $path) { $output += [IO.File]::ReadAllText($path) }
                    }
                    if ($output -match 'Now listening on: http://127\.0\.0\.1:\d+') {
                        $ready.value = $true
                        try {
                            $process.Kill($true)
                        } catch [System.Management.Automation.MethodException] {
                            $process.Kill()
                        } catch [System.NotSupportedException] {
                            $process.Kill()
                        }
                        [void]$process.WaitForExit(5000)
                    }
                }.GetNewClosure()
                $launch = Invoke-ConsumerCommand -Name 'runtime' -Arguments $arguments -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory -Timeout ([TimeSpan]::FromSeconds(45)) -OnProgress $onProgress
                $output = ''
                foreach ($path in @($launch.StdOutPath, $launch.StdErrPath)) {
                    if (Test-Path -LiteralPath $path) { $output += [IO.File]::ReadAllText($path) }
                }
                if (-not $ready.value -and $output -notmatch 'Now listening on: http://127\.0\.0\.1:\d+') {
                    throw "Server.Local did not start and bind its ephemeral endpoint; see $($launch.StdOutPath) and $($launch.StdErrPath)."
                }
                if ($launch.TimedOut) { throw "Server.Local startup exceeded its short smoke bound; see $($launch.StdOutPath) and $($launch.StdErrPath)." }
                $row.runtime = 'passed-started-and-bound'
            }
            default {
                $runtime = Invoke-ConsumerCommand -Name 'runtime' -Arguments @($consumerDll) -WorkingDirectory $scenarioDirectory -ScenarioDirectory $scenarioDirectory
                if ($runtime.ExitCode -ne 0) { throw "Consumer runtime failed with exit code $($runtime.ExitCode); see $($runtime.StdOutPath) and $($runtime.StdErrPath)." }
                $runtimeOutput = if (Test-Path -LiteralPath $runtime.StdOutPath) { [IO.File]::ReadAllText($runtime.StdOutPath) } else { '' }
                if ($Scenario.Kind -eq 'text' -and $runtimeOutput -notmatch 'TEXT quick\|fox') { throw 'Text consumer output was not deterministic.' }
                if ($Scenario.Kind -eq 'sourcegen' -and $runtimeOutput -notmatch 'SOURCEGEN Product') { throw 'SourceGen output did not prove generated mapper use.' }
                if ($Scenario.Kind -eq 'orchard' -and $runtimeOutput -notmatch 'ORCHARD module bootstrap passed') { throw 'Orchard consumer did not finish its module bootstrap.' }
                $row.runtime = 'passed'
            }
        }
        $row.status = 'passed'
        $Results.Add($row)
        return $scenarioDirectory
    } catch {
        $row.status = 'failed'
        $row.error = $_.Exception.Message
        [IO.File]::WriteAllText(
            (Join-Path $scenarioDirectory 'exception.txt'),
            ($_ | Out-String) + "`n" + [string]$_.ScriptStackTrace,
            [Text.UTF8Encoding]::new($false))
        $Results.Add($row)
        throw
    }
}

function Test-PackageConsumerSet {
    param(
        [Parameter(Mandatory = $true)][string]$PackageDirectory,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [ValidateSet('all', 'compression')][string]$Scope = 'all'
    )

    $target = if ($Scope -eq 'compression') { 'compression-package-consumer' } else { 'package-consumer' }
    $commandLine = if ($Scope -eq 'compression') { 'pwsh ./devops.ps1 pack -CompressionOnly -ValidateCompression' } else { 'pwsh ./devops.ps1 pack -Clean -ValidatePackages' }
    $run = New-ArtifactRun -Kind test -Target $target -CommandLine $commandLine -RepoRoot $RepoRoot
    $status = 'Failed'
    $consumerRoot = Join-Path $run.RunDirectory 'consumer-projects'
    $cacheRoot = Join-Path $run.RunDirectory 'isolated-caches'
    $acceptancePath = Join-Path $run.RunDirectory 'acceptance.json'
    $results = [Collections.Generic.List[object]]::new()
    $successDirectories = [Collections.Generic.List[string]]::new()
    $successCaches = [Collections.Generic.List[string]]::new()
    $manifestInfo = $null
    $failure = $null
    try {
        [void][IO.Directory]::CreateDirectory($consumerRoot)
        [void][IO.Directory]::CreateDirectory($cacheRoot)
        $manifestInfo = Get-ConsumerPackageManifest -PackageDirectory $PackageDirectory
        $policyPath = Join-Path $RepoRoot 'scripts/devops/packaging/package-policy.json'
        if (-not (Test-Path -LiteralPath $policyPath -PathType Leaf)) { throw "Package policy is missing: $policyPath" }
        $policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json -AsHashtable
        if ($policy.schemaVersion -ne 2 -or $null -eq $policy.releaseContract -or $null -eq $policy.compressionContract) {
            throw 'Package consumer validation requires package policy schemaVersion 2 with release and compression contracts.'
        }
        $manifestInfo | Add-Member -NotePropertyName Policy -NotePropertyValue $policy
        $expectedIds = if ($Scope -eq 'compression') {
            @($policy.compressionContract.requiredPackageIds | ForEach-Object { [string]$_ })
        } else {
            @($policy.releaseContract.requiredPackageIds | ForEach-Object { [string]$_ })
        }
        $actualIds = @($manifestInfo.Entries.Keys | Sort-Object -Culture invariant)
        $expectedIds = @($expectedIds | Sort-Object -Culture invariant)
        if (Compare-Object -ReferenceObject $expectedIds -DifferenceObject $actualIds -CaseSensitive) {
            throw "Package manifest IDs do not match the $Scope contract: expected [$($expectedIds -join ', ')], found [$($actualIds -join ', ')]."
        }
        $externalSources = Get-ConsumerPackageSources
        foreach ($scenario in @(Get-ConsumerScenarioDefinitions -Scope $Scope -Policy $policy -RepoRoot $RepoRoot)) {
            $directory = Invoke-PackageConsumerScenario -Scenario $scenario -PackageIndex $manifestInfo -PackageDirectory $PackageDirectory -RepoRoot $RepoRoot -ExternalSources $externalSources -ConsumerRoot $consumerRoot -CacheRoot $cacheRoot -Results $results
            $successDirectories.Add($directory)
            $successCaches.Add((Join-Path $cacheRoot "$($scenario.Name)-$($scenario.Framework)"))
        }
        $status = 'Passed'
    } catch {
        $failure = $_.Exception.Message
        throw
    } finally {
        $git = Get-ArtifactGitContext -RepoRoot $RepoRoot
        $sourceCommit = if ($null -ne $manifestInfo) { [string]$manifestInfo.Manifest.sourceCommit } else { [string]$git.commit }
        $acceptance = [ordered]@{
            schemaVersion = 1
            sourceCommit = $sourceCommit
            packageManifest = Join-Path $PackageDirectory 'manifest.json'
            generatedAtUtc = [DateTime]::UtcNow.ToString('O')
            status = $status.ToLowerInvariant()
            failure = $failure
            results = @($results)
        }
        Write-AtomicJsonFile -Path $acceptancePath -Value $acceptance
        if ($status -eq 'Passed') {
            try {
                foreach ($directory in $successDirectories) {
                    if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
                    if (Test-Path -LiteralPath $directory) { throw "Successful consumer working directory cleanup failed: $directory" }
                }
                foreach ($cache in $successCaches) {
                    if (Test-Path -LiteralPath $cache) { Remove-Item -LiteralPath $cache -Recurse -Force }
                    if (Test-Path -LiteralPath $cache) { throw "Successful consumer cache cleanup failed: $cache" }
                }
                if (Test-Path -LiteralPath $consumerRoot) { Remove-Item -LiteralPath $consumerRoot -Recurse -Force }
                if (Test-Path -LiteralPath $cacheRoot) { Remove-Item -LiteralPath $cacheRoot -Recurse -Force }
            } catch {
                $status = 'Failed'
                $failure = $_.Exception.Message
                $acceptance.status = 'failed'
                $acceptance.failure = $failure
                Write-AtomicJsonFile -Path $acceptancePath -Value $acceptance
            }
        }
        Complete-ArtifactRun -RunDirectory $run.RunDirectory -Status $status -AdditionalValues @{
            consumerDirectory = $consumerRoot
            cacheDirectory = $cacheRoot
            acceptance = $acceptancePath
            scenarioCount = $results.Count
        }
    }
    Write-Success "Package consumer acceptance evidence: $acceptancePath"
}
