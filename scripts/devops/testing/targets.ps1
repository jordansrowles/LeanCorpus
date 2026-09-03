$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-TestSuiteRegistry {
    $registeredSuites = Get-Variable -Name TestSuites -Scope Script -ValueOnly -ErrorAction SilentlyContinue
    if ($null -ne $registeredSuites) {
        return $registeredSuites
    }

    return Import-PowerShellDataFile (Join-Path $PSScriptRoot '../config/test-suites.psd1')
}

function Get-OrderedTestSuiteKeys {
    param([hashtable]$TestSuites = (Get-TestSuiteRegistry))

    $preferredOrder = @(
        'core',
        'text',
        'sourcegen',
        'architecture',
        'server-abstractions',
        'server-core',
        'server-integration',
        'aot'
    )

    $keys = [System.Collections.Generic.List[string]]::new()
    foreach ($key in $preferredOrder) {
        if ($TestSuites.ContainsKey($key)) {
            [void]$keys.Add($key)
        }
    }

    foreach ($key in @($TestSuites.Keys | Sort-Object)) {
        if (-not $keys.Contains([string]$key)) {
            [void]$keys.Add([string]$key)
        }
    }

    return @($keys.ToArray())
}

function Get-TestSuiteFrameworks {
    param(
        [hashtable]$Suite,
        [string]$SuiteKey
    )

    if ($Suite.ContainsKey('Frameworks')) {
        $frameworks = @($Suite.Frameworks | ForEach-Object { [string]$_ })
    } elseif ($Suite.ContainsKey('Framework')) {
        $frameworks = @([string]$Suite.Framework)
    } else {
        throw "Test suite '$SuiteKey' has no supported frameworks."
    }

    if ($frameworks.Count -eq 0) {
        throw "Test suite '$SuiteKey' has no supported frameworks."
    }

    return @($frameworks | Sort-Object -Unique)
}

function Get-TestSuiteRunnerKind {
    param(
        [hashtable]$Suite,
        [string]$SuiteKey
    )

    $runner = if ($Suite.ContainsKey('Runner')) { [string]$Suite.Runner } else { 'Mtp' }
    if ($runner -notin @('Mtp', 'AotNative')) {
        throw "Test suite '$SuiteKey' declares unsupported runner kind '$runner'."
    }

    return $runner
}

function ConvertTo-TestArgumentList {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @()
    }

    return @($Value -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Build-TraitFilter {
    param(
        [string]$Area,
        [string]$Category
    )

    $parts = [System.Collections.Generic.List[string]]::new()
    $areas = @(ConvertTo-TestArgumentList $Area)
    if ($areas.Count -gt 0) {
        [void]$parts.Add('(' + (($areas | ForEach-Object { "Area=$($_)" }) -join '|') + ')')
    }

    $categories = @(ConvertTo-TestArgumentList $Category)
    if ($categories.Count -gt 0) {
        [void]$parts.Add('(' + (($categories | ForEach-Object { "Category=$($_)" }) -join '|') + ')')
    }

    return ($parts -join '&')
}

function Build-TestFilter {
    param(
        [string[]]$AffectedAreas = @(),
        [string]$Area = '',
        [string]$Category = '',
        [string]$Filter = ''
    )

    $parts = [System.Collections.Generic.List[string]]::new()
    $affected = @($AffectedAreas | ForEach-Object { [string]$_ } | Where-Object { $_ } | Sort-Object -Unique)
    if ($affected.Count -gt 0) {
        [void]$parts.Add('(' + (($affected | ForEach-Object { "Area=$($_)" }) -join '|') + ')')
    }

    $traitFilter = Build-TraitFilter -Area $Area -Category $Category
    if ($traitFilter) {
        [void]$parts.Add($traitFilter)
    }

    if ($Filter) {
        [void]$parts.Add($Filter)
    }

    return ($parts -join '&')
}

function Get-DefaultRuntimeIdentifier {
    if ($IsLinux) {
        return 'linux-x64'
    }
    if ($IsMacOS) {
        return 'osx-x64'
    }

    return 'win-x64'
}

function New-TestTarget {
    param(
        [string]$Key,
        [string]$SuiteKey,
        [hashtable]$Suite,
        [string]$Framework,
        [string]$Configuration,
        [string]$RuntimeIdentifier,
        [string]$Filter,
        [string[]]$Areas,
        [string]$Area,
        [string]$Category,
        [string[]]$AdditionalArguments = @()
    )

    $runner = Get-TestSuiteRunnerKind -Suite $Suite -SuiteKey $SuiteKey
    $capabilities = if ($Suite.ContainsKey('Capabilities')) {
        @($Suite.Capabilities | ForEach-Object { [string]$_ })
    } else {
        @()
    }
    $coverageEligible = $false
    if ($Suite.ContainsKey('Coverage')) {
        $coverageEligible = [bool]$Suite.Coverage
    }

    $project = if ($Suite.ContainsKey('Project')) { [string]$Suite.Project } else { '' }
    $displayName = [string]$Suite.Name
    $additionalArguments = @($AdditionalArguments | Where-Object {
        $null -ne $_ -and -not [string]::IsNullOrEmpty([string]$_)
    })
    $artifactName = "$SuiteKey-$Framework"
    if ($RuntimeIdentifier) {
        $artifactName = "$artifactName-$RuntimeIdentifier"
    }

    return [pscustomobject]@{
        Key = $Key
        Name = $displayName
        Suite = $SuiteKey
        RunnerKind = $runner
        Project = $project
        Framework = $Framework
        Configuration = $Configuration
        RuntimeIdentifier = $RuntimeIdentifier
        Filter = $Filter
        Areas = @($Areas | Sort-Object -Unique)
        Categories = ConvertTo-TestArgumentList $Category
        CoverageEligible = $coverageEligible
        Capabilities = @($capabilities | Sort-Object -Unique)
        AdditionalArguments = $additionalArguments
        ArtifactName = $artifactName
    }
}

function Get-AffectedTestIntent {
    param(
        [string]$RepoRoot,
        [hashtable]$TestSuites = (Get-TestSuiteRegistry),
        [hashtable]$CodeAreas = $null
    )

    if ($null -eq $CodeAreas) {
        $CodeAreas = Import-PowerShellDataFile (Join-Path $PSScriptRoot '../config/code-areas.psd1')
    }

    $dirty = @(Get-DirtyFiles -RepoRoot $RepoRoot)
    $areasBySuite = @{}
    $matchedFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($file in $dirty) {
        $normalised = ([string]$file).Replace('\', '/')
        foreach ($entryName in $CodeAreas.Keys) {
            $entry = $CodeAreas[$entryName]
            foreach ($glob in @($entry.Globs)) {
                if (-not (Test-GlobMatch -Path $normalised -Glob ([string]$glob))) {
                    continue
                }

                [void]$matchedFiles.Add($normalised)
                foreach ($target in @($entry.Targets)) {
                    $suiteKey, $areaName = ([string]$target -split ':', 2)
                    if (-not $TestSuites.ContainsKey($suiteKey)) {
                        throw "Code-area mapping '$entryName' references unknown test suite '$suiteKey'."
                    }
                    if (-not $areasBySuite.ContainsKey($suiteKey)) {
                        $areasBySuite[$suiteKey] = [System.Collections.Generic.HashSet[string]]::new()
                    }
                    [void]$areasBySuite[$suiteKey].Add($areaName)
                }
            }
        }
    }

    if ($matchedFiles.Count -ne $dirty.Count) {
        $unmapped = @($dirty | Where-Object {
            -not $matchedFiles.Contains(([string]$_).Replace('\', '/'))
        })
        if ($unmapped.Count -gt 0) {
            throw "Dirty files have no code-area mapping. Refusing to run zero tests: $($unmapped -join ', ')"
        }
    }

    if ($areasBySuite.Count -eq 0) {
        throw 'No code-area mapping matched the dirty files. Refusing to run zero tests.'
    }

    $normalisedAreas = @{}
    foreach ($suiteKey in @($areasBySuite.Keys | Sort-Object)) {
        $normalisedAreas[$suiteKey] = @($areasBySuite[$suiteKey].ToArray() | Sort-Object)
    }

    return [pscustomobject]@{
        DirtyFiles = $dirty
        AreasBySuite = $normalisedAreas
    }
}

function Resolve-TestTargets {
    param(
        [string]$Suite = 'all',
        [string]$Framework = '',
        [bool]$FrameworkExplicit = $false,
        [string]$Configuration = 'Release',
        [string]$RuntimeIdentifier = '',
        [string]$Area = '',
        [string]$Category = '',
        [string]$Filter = '',
        [bool]$Ci = $false,
        [bool]$CollectCoverage = $false,
        [string[]]$AdditionalArguments = @(),
        [string[]]$AffectedAreas = @(),
        [string]$RepoRoot = (Get-RepoRoot),
        [hashtable]$TestSuites = (Get-TestSuiteRegistry)
    )

    $requestedSuite = if ($Suite) { $Suite.ToLowerInvariant() } else { 'all' }
    if ($requestedSuite -eq 'affected') {
        $affected = Get-AffectedTestIntent -RepoRoot $RepoRoot -TestSuites $TestSuites
        $resolved = [System.Collections.Generic.List[object]]::new()
        foreach ($suiteKey in @($affected.AreasBySuite.Keys | Sort-Object)) {
            $suiteTargets = Resolve-TestTargets -Suite $suiteKey -Framework $Framework `
                -FrameworkExplicit $FrameworkExplicit -Configuration $Configuration `
                -RuntimeIdentifier $RuntimeIdentifier -Area $Area -Category $Category `
                -Filter $Filter -Ci $Ci -CollectCoverage $CollectCoverage `
                -AdditionalArguments $AdditionalArguments -RepoRoot $RepoRoot `
                -TestSuites $TestSuites -AffectedAreas @($affected.AreasBySuite[$suiteKey])
            foreach ($target in @($suiteTargets)) {
                [void]$resolved.Add($target)
            }
        }
        return @($resolved.ToArray())
    }

    $affectedAreas = @($AffectedAreas)
    $hasAffectedAreas = $PSBoundParameters.ContainsKey('AffectedAreas') -and $null -ne $AffectedAreas

    $suiteKeys = if ($requestedSuite -eq 'all') {
        Get-OrderedTestSuiteKeys -TestSuites $TestSuites
    } else {
        @($requestedSuite)
    }

    $targets = [System.Collections.Generic.List[object]]::new()
    $isExplicitSuite = $requestedSuite -ne 'all'
    foreach ($suiteKey in $suiteKeys) {
        if (-not $TestSuites.ContainsKey($suiteKey)) {
            throw "Unknown test suite '$suiteKey'."
        }

        $suiteConfig = $TestSuites[$suiteKey]
        $frameworks = @(Get-TestSuiteFrameworks -Suite $suiteConfig -SuiteKey $suiteKey)
        $runner = Get-TestSuiteRunnerKind -Suite $suiteConfig -SuiteKey $suiteKey
        if ($FrameworkExplicit) {
            if ($Framework -notin $frameworks) {
                if ($isExplicitSuite) {
                    throw "Test suite '$suiteKey' does not support framework '$Framework'. Supported frameworks: $($frameworks -join ', ')."
                }
                continue
            }
            $selectedFrameworks = @($Framework)
        } elseif ($Ci -or ($suiteConfig.ContainsKey('ExpandFrameworksByDefault') -and [bool]$suiteConfig.ExpandFrameworksByDefault)) {
            $selectedFrameworks = $frameworks
        } elseif ($suiteConfig.ContainsKey('DefaultFramework')) {
            $selectedFrameworks = @([string]$suiteConfig.DefaultFramework)
        } else {
            if (-not $Framework) {
                throw "A default framework is required for test suite '$suiteKey'."
            }
            if ($Framework -notin $frameworks) {
                if ($isExplicitSuite) {
                    throw "Test suite '$suiteKey' does not support framework '$Framework'. Supported frameworks: $($frameworks -join ', ')."
                }
                continue
            }
            $selectedFrameworks = @($Framework)
        }

        if ($CollectCoverage -and -not [bool]$suiteConfig.Coverage) {
            if ($isExplicitSuite) {
                throw "Test suite '$suiteKey' is not eligible for coverage."
            }
            continue
        }

        foreach ($selectedFramework in $selectedFrameworks) {
            $targetAreas = if ($hasAffectedAreas) { $affectedAreas } else { @() }
            $targetFilter = Build-TestFilter -AffectedAreas $targetAreas -Area $Area -Category $Category -Filter $Filter
            $targetRuntimeIdentifier = if ($runner -eq 'AotNative') {
                if ($RuntimeIdentifier) { $RuntimeIdentifier } else { Get-DefaultRuntimeIdentifier }
            } else {
                ''
            }
            $targetKey = "$suiteKey/$selectedFramework"
            if ($targetRuntimeIdentifier) {
                $targetKey = "$targetKey/$targetRuntimeIdentifier"
            }
            $suiteArguments = if ($suiteConfig.ContainsKey('AdditionalArguments')) {
                @($suiteConfig.AdditionalArguments) + @($AdditionalArguments)
            } else {
                @($AdditionalArguments)
            }
            $target = New-TestTarget -Key $targetKey -SuiteKey $suiteKey -Suite $suiteConfig `
                -Framework $selectedFramework -Configuration $Configuration `
                -RuntimeIdentifier $targetRuntimeIdentifier -Filter $targetFilter `
                -Areas $targetAreas -Area $Area -Category $Category `
                -AdditionalArguments $suiteArguments
            [void]$targets.Add($target)
        }
    }

    if ($targets.Count -eq 0) {
        if ($isExplicitSuite) {
            throw "No executable target was resolved for suite '$requestedSuite'."
        }
        throw 'No executable test targets were resolved.'
    }

    return @($targets.ToArray())
}

function Get-CoverageSuiteKeys {
    param([hashtable]$TestSuites = (Get-TestSuiteRegistry))

    return @((Get-OrderedTestSuiteKeys -TestSuites $TestSuites) | Where-Object {
        $TestSuites[$_].ContainsKey('Coverage') -and [bool]$TestSuites[$_].Coverage
    })
}

function Get-DirtyFiles {
    param([string]$RepoRoot)

    $files = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @(& git -C $RepoRoot diff --name-only 2>$null)) {
        if ($line) { [void]$files.Add([string]$line) }
    }
    foreach ($line in @(& git -C $RepoRoot diff --cached --name-only 2>$null)) {
        if ($line) { [void]$files.Add([string]$line) }
    }
    foreach ($line in @(& git -C $RepoRoot ls-files --others --exclude-standard 2>$null)) {
        if ($line) { [void]$files.Add([string]$line) }
    }

    return @($files | Sort-Object -Unique)
}

function Test-GlobMatch {
    param(
        [string]$Path,
        [string]$Glob
    )

    $pattern = [System.Text.StringBuilder]::new()
    $i = 0
    while ($i -lt $Glob.Length) {
        $character = $Glob[$i]
        if ($character -eq '*') {
            if (($i + 1) -lt $Glob.Length -and $Glob[$i + 1] -eq '*') {
                [void]$pattern.Append('.*')
                $i++
            } else {
                [void]$pattern.Append('[^/]*')
            }
        } elseif ($character -eq '?') {
            [void]$pattern.Append('[^/]')
        } else {
            [void]$pattern.Append([regex]::Escape([string]$character))
        }
        $i++
    }

    return $Path -match ("^$pattern$")
}
