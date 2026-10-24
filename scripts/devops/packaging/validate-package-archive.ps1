$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-PackageProjectMetadata {
    param([Parameter(Mandatory)][string]$ProjectPath)

    [xml]$project = [IO.File]::ReadAllText($ProjectPath)
    $properties = @{}
    foreach ($group in @($project.Project.PropertyGroup)) {
        if ($null -eq $group) { continue }
        foreach ($node in @($group.ChildNodes)) {
            if ($node.NodeType -eq [Xml.XmlNodeType]::Element) {
                $properties[$node.LocalName] = [string]$node.InnerText
            }
        }
    }

    $assemblyName = $properties['AssemblyName']
    if ([string]::IsNullOrWhiteSpace($assemblyName)) {
        $assemblyName = [IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    }

    return [ordered]@{
        packageId = [string]$properties['PackageId']
        version = [string]$properties['Version']
        assemblyName = $assemblyName
        readme = [string]$properties['PackageReadmeFile']
        licenseFile = [string]$properties['PackageLicenseFile']
        repositoryUrl = [string]$properties['RepositoryUrl']
    }
}

function Get-PackageArchiveInfo {
    param([Parameter(Mandatory)][string]$Path)

    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $nuspecs = @($archive.Entries | Where-Object { $_.FullName -match '^[^/]+\.nuspec$' })
        if ($nuspecs.Count -ne 1) { throw "Expected exactly one root nuspec in $Path; found $($nuspecs.Count)." }

        $reader = [IO.StreamReader]::new($nuspecs[0].Open())
        try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $spec.SelectSingleNode("//*[local-name()='metadata']")
        if ($null -eq $metadata) { throw "The nuspec has no metadata element: $Path" }

        $groups = [Collections.Generic.List[object]]::new()
        $dependenciesNode = $metadata.SelectSingleNode("./*[local-name()='dependencies']")
        if ($null -ne $dependenciesNode) {
            foreach ($group in $dependenciesNode.SelectNodes("./*[local-name()='group']")) {
                $dependencies = [Collections.Generic.List[object]]::new()
                foreach ($dependency in $group.SelectNodes("./*[local-name()='dependency']")) {
                    $dependencies.Add([ordered]@{
                        id = $dependency.GetAttribute('id')
                        version = $dependency.GetAttribute('version')
                    })
                }
                $groups.Add([ordered]@{
                    targetFramework = $group.GetAttribute('targetFramework')
                    dependencies = @($dependencies)
                })
            }
        }

        $repository = $metadata.SelectSingleNode("./*[local-name()='repository']")
        $license = $metadata.SelectSingleNode("./*[local-name()='license']")
        return [ordered]@{
            id = [string]$metadata.SelectSingleNode("./*[local-name()='id']").InnerText
            version = [string]$metadata.SelectSingleNode("./*[local-name()='version']").InnerText
            repositoryUrl = if ($null -eq $repository) { '' } else { $repository.GetAttribute('url') }
            licenseValue = if ($null -eq $license) { '' } else { $license.InnerText }
            licenseType = if ($null -eq $license) { '' } else { $license.GetAttribute('type') }
            dependencies = @($groups)
            dependencyIds = @($groups | ForEach-Object { $_.dependencies } | ForEach-Object { $_.id })
            entries = @($archive.Entries | ForEach-Object { $_.FullName })
        }
    } finally { $archive.Dispose() }
}

function ConvertTo-PackageFrameworkName {
    param([string]$Value)

    $framework = ($Value -replace ',Version=v', '').Trim().TrimStart('.')
    if ($framework -match '^NETCoreApp(?<version>\d+(?:\.\d+)*)$') { return "net$($Matches.version)" }
    if ($framework -match '^NETStandard(?<version>\d+(?:\.\d+)*)$') { return "netstandard$($Matches.version)" }
    if ($framework -match '^NETFramework(?<version>\d+(?:\.\d+)*)$') { return "net$($Matches.version)" }
    return $framework.ToLowerInvariant()
}

function Get-PackageProjectDependencyIds {
    param([Parameter(Mandatory)][string]$ProjectPath)

    [xml]$project = [IO.File]::ReadAllText($ProjectPath)
    $dependencies = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($reference in $project.SelectNodes("//*[local-name()='PackageReference']")) {
        $include = $reference.GetAttribute('Include')
        if ([string]::IsNullOrWhiteSpace($include)) { $include = $reference.GetAttribute('Update') }
        $privateAssets = $reference.GetAttribute('PrivateAssets')
        $excludeAssets = $reference.GetAttribute('ExcludeAssets')
        $privateNode = $reference.SelectSingleNode("./*[local-name()='PrivateAssets']")
        $excludeNode = $reference.SelectSingleNode("./*[local-name()='ExcludeAssets']")
        if ([string]::IsNullOrWhiteSpace($privateAssets) -and $null -ne $privateNode) { $privateAssets = $privateNode.InnerText }
        if ([string]::IsNullOrWhiteSpace($excludeAssets) -and $null -ne $excludeNode) { $excludeAssets = $excludeNode.InnerText }
        if ([string]::IsNullOrWhiteSpace($include) -or $privateAssets -match '(?i)(^|;)all(;|$)' -or
            $excludeAssets -match '(?i)(^|;)all(;|$)') { continue }
        foreach ($id in $include.Split(';', [StringSplitOptions]::RemoveEmptyEntries)) { [void]$dependencies.Add($id.Trim()) }
    }

    $projectDirectory = Split-Path -Parent $ProjectPath
    foreach ($reference in $project.SelectNodes("//*[local-name()='ProjectReference']")) {
        $include = $reference.GetAttribute('Include')
        if ([string]::IsNullOrWhiteSpace($include)) { continue }
        $referencedPath = [IO.Path]::GetFullPath((Join-Path $projectDirectory $include.Replace('\', [IO.Path]::DirectorySeparatorChar)))
        if (-not (Test-Path -LiteralPath $referencedPath -PathType Leaf)) { throw "Project reference is missing while validating dependencies: $referencedPath" }
        $referencedMetadata = Get-PackageProjectMetadata -ProjectPath $referencedPath
        $id = $referencedMetadata.packageId
        if ([string]::IsNullOrWhiteSpace($id)) { $id = [IO.Path]::GetFileNameWithoutExtension($referencedPath) }
        [void]$dependencies.Add($id)
    }
    return @($dependencies | Sort-Object)
}

function Assert-PackageRepositoryMetadata {
    param([Collections.IDictionary]$ProjectMetadata, [Collections.IDictionary]$ArchiveInfo, [string]$PackagePath)

    if ([string]::IsNullOrWhiteSpace($ProjectMetadata.repositoryUrl)) {
        throw "Project does not declare RepositoryUrl for $PackagePath."
    }
    if ($ArchiveInfo.repositoryUrl -cne $ProjectMetadata.repositoryUrl) {
        throw "Repository metadata does not match the project in $PackagePath."
    }
}

function Assert-PackageDependencies {
    param(
        [Collections.IDictionary]$ArchiveInfo,
        [Collections.IDictionary]$PolicyEntry,
        [string]$ProjectPath,
        [string]$PackagePath
    )

    $groups = @($ArchiveInfo.dependencies)
    $expectedDependencyIds = @(Get-PackageProjectDependencyIds -ProjectPath $ProjectPath)
    if ($groups.Count -eq 0) {
        if ($expectedDependencyIds.Count -gt 0) {
            throw "Project declares dependencies but its nuspec has no dependency groups in $PackagePath."
        }
        return
    }

    $expectedDependencyFrameworks = if ($PolicyEntry.Contains('expectedDependencyFrameworks')) {
        @($PolicyEntry['expectedDependencyFrameworks'])
    } else {
        @($PolicyEntry.expectedFrameworks)
    }
    $expected = @($expectedDependencyFrameworks | ForEach-Object { ([string]$_).ToLowerInvariant() } | Sort-Object -Unique)
    $actual = @($groups | ForEach-Object { ConvertTo-PackageFrameworkName ([string]$_.targetFramework) } | Sort-Object -Unique)
    if (@($groups | Where-Object { [string]::IsNullOrWhiteSpace($_.targetFramework) }).Count -gt 0 -or
        ($actual -join ',') -cne ($expected -join ',')) {
        throw "Unexpected dependency framework groups in $PackagePath. Expected '$($expected -join ',')', found '$($actual -join ',')'."
    }

    foreach ($dependency in @($groups | ForEach-Object { $_.dependencies })) {
        if ([string]::IsNullOrWhiteSpace($dependency.id) -or [string]::IsNullOrWhiteSpace($dependency.version)) {
            throw "Dependency is missing an ID or version in $PackagePath."
        }
    }
    foreach ($group in $groups) {
        $actualDependencyIds = @($group.dependencies | ForEach-Object { [string]$_.id } | Sort-Object -Unique)
        if (($actualDependencyIds -join ',') -cne ($expectedDependencyIds -join ',')) {
            throw "Unexpected dependency IDs for '$($group.targetFramework)' in $PackagePath. Expected '$($expectedDependencyIds -join ',')', found '$($actualDependencyIds -join ',')'."
        }
    }
}

function Test-PackageArchive {
    param(
        [Parameter(Mandatory)][string]$PackagePath,
        [Parameter(Mandatory)][hashtable]$PolicyEntry,
        [Parameter(Mandatory)][string]$RepoRoot,
        [string]$SymbolPath
    )

    $projectPath = Join-Path $RepoRoot ([string]$PolicyEntry.projectPath)
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Package policy project does not exist: $($PolicyEntry.projectPath)"
    }
    $project = Get-PackageProjectMetadata -ProjectPath $projectPath
    $archiveInfo = Get-PackageArchiveInfo -Path $PackagePath
    if ($archiveInfo.id -cne $PolicyEntry.packageId -or $archiveInfo.version -cne $project.version) {
        throw "Unexpected package identity/version in $PackagePath; expected $($PolicyEntry.packageId) $($project.version)."
    }
    if ($project.packageId -cne $PolicyEntry.packageId) {
        throw "Package policy ID '$($PolicyEntry.packageId)' does not match project ID '$($project.packageId)' in $projectPath."
    }

    Assert-PackageDependencies -ArchiveInfo $archiveInfo -PolicyEntry $PolicyEntry -ProjectPath $projectPath -PackagePath $PackagePath

    $kind = [string]$PolicyEntry.kind
    $frameworks = @($PolicyEntry.expectedFrameworks | ForEach-Object { [string]$_ })
    $entries = @($archiveInfo.entries)
    $allowedCategories = if ($PolicyEntry.Contains('allowedAssetCategories')) {
        @($PolicyEntry['allowedAssetCategories'])
    } else { @() }
    $allowedRootFiles = @('[Content_Types].xml', 'README.md', 'LICENCE', 'LICENSE')
    $applicationAssets = @()
    if (-not [string]::IsNullOrWhiteSpace($project.readme)) {
        $readmeEntry = $project.readme.Replace('\', '/').TrimStart('/')
        if ($readmeEntry -notin $entries) { throw "Declared package README '$readmeEntry' is missing from $PackagePath." }
        $allowedRootFiles += $readmeEntry
    }
    if (-not [string]::IsNullOrWhiteSpace($project.licenseFile)) {
        $licenseEntry = $project.licenseFile.Replace('\', '/').TrimStart('/')
        if ($licenseEntry -notin $entries) { throw "Declared package licence '$licenseEntry' is missing from $PackagePath." }
        $allowedRootFiles += $licenseEntry
    }

    $nuspecPath = "$($PolicyEntry.packageId).nuspec"
    if ($nuspecPath -notin $entries) { throw "Expected package nuspec '$nuspecPath' is missing from $PackagePath." }
    if ($kind -eq 'library' -or $kind -eq 'razor-library') {
        foreach ($framework in $frameworks) {
            $dll = "lib/$framework/$($project.assemblyName).dll"
            if ($dll -notin $entries) { throw "Missing expected implementation assembly '$dll' in $PackagePath." }
            if ([bool]$PolicyEntry.requireXmlDocs) {
                $xml = "lib/$framework/$($project.assemblyName).xml"
                if ($xml -notin $entries) { throw "Missing required XML documentation '$xml' in $PackagePath." }
            }
        }
        $libraryFrameworks = @($entries | ForEach-Object {
            if ($_ -match '^lib/([^/]+)/') { $Matches[1] }
        } | Sort-Object -Unique)
        if (($libraryFrameworks -join ',') -cne (@($frameworks | Sort-Object -Unique) -join ',')) {
            throw "Unexpected library TFM folders in ${PackagePath}: '$($libraryFrameworks -join ',')'."
        }
        foreach ($entry in $entries | Where-Object { $_ -match '^lib/' }) {
            $parts = $entry -split '/'
            $framework = $parts[1]
            $expectedAssets = @("lib/$framework/$($project.assemblyName).dll")
            if ([bool]$PolicyEntry.requireXmlDocs) { $expectedAssets += "lib/$framework/$($project.assemblyName).xml" }
            if ($entry -notin $expectedAssets) { throw "Unexpected library asset '$entry' in $PackagePath." }
        }
    }
    elseif ($kind -eq 'analyzer') {
        $analyzerDll = "analyzers/dotnet/cs/$($project.assemblyName).dll"
        if ($analyzerDll -notin $entries) { throw "Missing SourceGen analyser assembly '$analyzerDll' in $PackagePath." }
        if (@($entries | Where-Object { $_ -match '^lib/' }).Count -gt 0) {
            throw "Analyzer package must not contain a lib implementation assembly: $PackagePath."
        }
        if (@($archiveInfo.dependencyIds | Where-Object { $_ -like 'Microsoft.CodeAnalysis*' }).Count -gt 0) {
            throw "Roslyn implementation dependencies leaked into $PackagePath."
        }
        if (@($entries | Where-Object { $_ -match '^analyzers/dotnet/cs/.+\.dll$' }).Count -ne 1) {
            throw "Expected exactly one C# analyser assembly in $PackagePath."
        }
        if ($archiveInfo.licenseType -cne 'file' -or
            [string]::IsNullOrWhiteSpace($project.licenseFile) -or
            $archiveInfo.licenseValue -cne $project.licenseFile.Replace('\', '/').TrimStart('/')) {
            throw "SourceGen package must declare its licence file in $PackagePath."
        }
    }
    elseif ($kind -eq 'application') {
        $applicationAssets = @(
            foreach ($framework in $frameworks) {
                "lib/$framework/$($project.assemblyName).dll"
                "lib/$framework/$($project.assemblyName).runtimeconfig.json"
            }
        )
        foreach ($asset in $applicationAssets) {
            if ($asset -notin $entries) { throw "Missing expected application asset '$asset' in $PackagePath." }
        }
        $applicationFrameworks = @($entries | ForEach-Object {
            if ($_ -match '^lib/([^/]+)/') { $Matches[1] }
        } | Sort-Object -Unique)
        if (($applicationFrameworks -join ',') -cne (($frameworks | Sort-Object) -join ',')) {
            throw "Unexpected application TFM folders in ${PackagePath}: '$($applicationFrameworks -join ',')'."
        }
        foreach ($entry in $entries | Where-Object { $_ -match '^lib/' }) {
            if ($entry -notin $applicationAssets) { throw "Unexpected application asset '$entry' in $PackagePath." }
        }
    }

    Assert-PackageRepositoryMetadata -ProjectMetadata $project -ArchiveInfo $archiveInfo -PackagePath $PackagePath
    foreach ($entry in $entries) {
        if ($entry -match '(^|/)(bin|obj)(/|$)') { throw "Build output '$entry' leaked into $PackagePath." }

        $category = ($entry -split '/', 2)[0]
        if ($entry -in $allowedRootFiles -or $entry -ceq $nuspecPath -or
            $entry -match '^_rels/' -or $entry -match '^package/services/metadata/core-properties/[^/]+\.psmdcp$') {
            continue
        }
        if ($category -eq 'lib' -and ($kind -eq 'library' -or $kind -eq 'razor-library')) { continue }
        if ($kind -eq 'application' -and $entry -in $applicationAssets) { continue }
        if ($category -eq 'analyzers' -and $kind -eq 'analyzer') { continue }
        if ($category -in $allowedCategories) { continue }
        throw "Unexpected package asset category/content '$entry' in $PackagePath. Add an explicit policy category only when this asset is required."
    }

    foreach ($entry in $entries | Where-Object { $_ -match '\.(dll|exe|pdb|so|dylib|a|lib)$' }) {
        if ($entry -match '(?i)(test|benchmark)' ) { throw "Test or benchmark binary '$entry' leaked into $PackagePath." }
        if (($kind -eq 'library' -or $kind -eq 'razor-library') -and $entry -match '^lib/') {
            $framework = ($entry -split '/')[1]
            $expectedBinary = "lib/$framework/$($project.assemblyName).dll"
            if ($entry -cne $expectedBinary) { throw "Unexpected package binary '$entry' in $PackagePath." }
        }
        elseif ($kind -eq 'analyzer' -and $entry -ceq "analyzers/dotnet/cs/$($project.assemblyName).dll") { continue }
        elseif ($kind -eq 'application' -and $entry -cin $applicationAssets) { continue }
        else { throw "Unexpected package binary '$entry' in $PackagePath." }
    }

    $needsSymbols = [bool]$PolicyEntry.requireSymbols
    if ($needsSymbols) {
        if ([string]::IsNullOrWhiteSpace($SymbolPath) -or -not (Test-Path -LiteralPath $SymbolPath -PathType Leaf)) {
            throw "Policy requires a symbol package for $PackagePath."
        }
        $symbolInfo = Get-PackageArchiveInfo -Path $SymbolPath
        if ($symbolInfo.id -cne $archiveInfo.id -or $symbolInfo.version -cne $archiveInfo.version) {
            throw "Symbol package identity/version does not match $PackagePath."
        }
        $symbolEntries = @($symbolInfo.entries)
        foreach ($framework in $frameworks) {
            $pdbPath = "lib/$framework/$($project.assemblyName).pdb"
            if ($pdbPath -notin $symbolEntries) { throw "Missing symbol PDB '$pdbPath' in $SymbolPath." }
            $zip = [IO.Compression.ZipFile]::OpenRead($SymbolPath)
            try {
                $pdb = [IO.BinaryReader]::new($zip.GetEntry($pdbPath).Open())
                try {
                    if ([Text.Encoding]::ASCII.GetString($pdb.ReadBytes(4)) -cne 'BSJB') {
                        throw "Expected a portable PDB at '$pdbPath' in $SymbolPath."
                    }
                } finally { $pdb.Dispose() }
            } finally { $zip.Dispose() }
        }
        foreach ($entry in $symbolEntries) {
            if ($entry -match '(^|/)(bin|obj)(/|$)') { throw "Build output '$entry' leaked into $SymbolPath." }
            if ($entry -match '(?i)(test|benchmark)' -and $entry -match '\.pdb$') {
                throw "Test or benchmark symbols '$entry' leaked into $SymbolPath."
            }
            if ($entry -match '\.pdb$' -and $entry -notmatch '^lib/') {
                throw "Unexpected PDB location '$entry' in $SymbolPath."
            }
            if ($entry -notin @('[Content_Types].xml', "$($PolicyEntry.packageId).nuspec") -and
                $entry -notmatch '^_rels/' -and
                $entry -notmatch '^package/services/metadata/core-properties/[^/]+\.psmdcp$' -and
                $entry -notmatch '^lib/[^/]+/[^/]+\.pdb$') {
                throw "Unexpected symbol package content '$entry' in $SymbolPath."
            }
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($SymbolPath) -and (Test-Path -LiteralPath $SymbolPath -PathType Leaf)) {
        throw "Policy does not require symbols, but a symbol package was produced for $PackagePath."
    }

    Write-Success "Verified package archive $($archiveInfo.id) $($archiveInfo.version) ($kind)"
    return $archiveInfo
}
