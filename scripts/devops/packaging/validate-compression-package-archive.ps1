$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-CompressionPackageDependencyContract {
    param([Parameter(Mandatory = $true)][System.Collections.IDictionary]$Policy)

    $contract = $Policy.compressionContract
    if ($null -eq $contract -or $contract.version -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$contract.corePackageId) -or
        [string]::IsNullOrWhiteSpace([string]$contract.packageGroup) -or
        @($contract.requiredPackageIds).Count -lt 2 -or
        [string]::IsNullOrWhiteSpace([string]$contract.coreDependencyVersionRange)) {
        throw 'Package policy requires a version 1 compression contract.'
    }
    return $contract
}

function Assert-CompressionPackageDependencyRange {
    param(
        [Parameter(Mandatory = $true)][string]$PackageId,
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string[]]$ExpectedFrameworks,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Contract
    )

    $codecPackageIds = @($Contract.requiredPackageIds | Where-Object { [string]$_ -cne [string]$Contract.corePackageId })
    if ($PackageId -notin $codecPackageIds) {
        throw "Package $PackageId is not covered by the compression dependency contract."
    }

    $archiveInfo = Get-PackageArchiveInfo -Path $PackagePath
    if ($archiveInfo.id -cne $PackageId) {
        throw "Compression package archive ID '$($archiveInfo.id)' does not match $PackageId in $PackagePath."
    }

    $expected = @($ExpectedFrameworks | ForEach-Object { ([string]$_).ToLowerInvariant() } | Sort-Object -Unique)
    if ($expected.Count -eq 0) {
        throw "Compression package $PackageId has no expected dependency frameworks."
    }

    $groups = @($archiveInfo.dependencies)
    if ($groups.Count -ne $expected.Count) {
        throw "Compression package $PackageId must declare dependency groups for '$($expected -join ',')'; found $($groups.Count) groups in $PackagePath."
    }

    foreach ($framework in $expected) {
        $frameworkGroups = @($groups | Where-Object {
            (ConvertTo-PackageFrameworkName ([string]$_.targetFramework)) -ceq $framework
        })
        if ($frameworkGroups.Count -ne 1) {
            throw "Compression package $PackageId must declare exactly one '$framework' dependency group in $PackagePath."
        }

        $coreDependencies = @($frameworkGroups[0].dependencies | Where-Object { $_.id -ceq [string]$Contract.corePackageId })
        if ($coreDependencies.Count -ne 1 -or
            ($coreDependencies[0].version -replace '\s', '') -cne [string]$Contract.coreDependencyVersionRange) {
            throw "Compression package $PackageId must declare $($Contract.corePackageId) $($Contract.coreDependencyVersionRange) in its '$framework' dependency group."
        }
    }
}

function Assert-CompressionCoreVersionContract {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Contract,
        [Parameter(Mandatory = $true)][string]$CoreVersion
    )

    $range = [regex]::Match([string]$Contract.coreDependencyVersionRange, '^\[(?<minimum>\d+\.\d+\.\d+),(?<maximum>\d+\.\d+\.\d+)\)$')
    if (-not $range.Success) { throw "Compression Core dependency range '$($Contract.coreDependencyVersionRange)' must be a closed lower bound and open next-major upper bound." }
    $minimum = [version]$range.Groups['minimum'].Value
    $maximum = [version]$range.Groups['maximum'].Value
    $current = [version]$CoreVersion
    if ($minimum.Minor -ne 0 -or $minimum.Build -ne 0 -or $maximum.Minor -ne 0 -or $maximum.Build -ne 0 -or
        $maximum.Major -ne ($minimum.Major + 1) -or $current.Major -ne $minimum.Major -or $current -lt $minimum -or $current -ge $maximum) {
        throw "Compression contract range '$($Contract.coreDependencyVersionRange)' does not cover the current Core package version '$CoreVersion'. Update the versioned compression contract when Core changes major version."
    }
}
