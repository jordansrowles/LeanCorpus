$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($IsWindows) {
    throw 'The fake-command bootstrap contract smoke currently runs on Linux only.'
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../'))
$scriptsRoot = Join-Path $repoRoot 'scripts/devops'
$retiredApplicationNames = @(
    'DevOps.psm1',
    'artifacts',
    'commands',
    'common',
    'config',
    'diagnostics',
    'support',
    'ui'
)
$retiredApplicationPaths = @($retiredApplicationNames |
    ForEach-Object { Join-Path $scriptsRoot $_ } |
    Where-Object { Test-Path -LiteralPath $_ })
if ($retiredApplicationPaths.Count -gt 0) {
    throw "The retired PowerShell DevOps application is still present: $($retiredApplicationPaths -join ', ')"
}
$legacyConfiguration = @(Get-ChildItem -LiteralPath $scriptsRoot -Filter '*.psd1' -File -Recurse -ErrorAction SilentlyContinue)
if ($legacyConfiguration.Count -gt 0) {
    throw "Legacy PowerShell DevOps configuration remains under scripts/devops: $($legacyConfiguration.FullName -join ', ')"
}
$legacyTestScripts = @(Get-ChildItem -LiteralPath (Join-Path $scriptsRoot 'testing') -Filter '*.ps1' -File -ErrorAction SilentlyContinue)
if ($legacyTestScripts.Count -gt 0) {
    throw "Legacy PowerShell test runner scripts remain: $($legacyTestScripts.FullName -join ', ')"
}
$managedHostScript = Join-Path $PSScriptRoot '../managed-host.ps1'
. $managedHostScript
$gitCommand = Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1
$powerShell = Get-Command pwsh -CommandType Application -ErrorAction Stop | Select-Object -First 1
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('LeanCorpus managed bootstrap smoke ' + [Guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($tempRoot)

$fakeDotnet = @'
#!/bin/sh
if [ "$1" = "--list-sdks" ]; then
    if [ "$BOOTSTRAP_FAKE_FAIL_STAGE" = "missing-sdk" ]; then
        printf '%s\n' '10.0.400 [/fake/sdk]'
    else
        printf '%s\n' '11.0.100-preview.7.26381.103 [/fake/sdk]'
    fi
    exit 0
fi
if [ "$1" = "--version" ]; then printf '%s\n' '11.0.100-preview.7.26381.103'; exit 0; fi
if [ "$1" = "tool" ] && [ "$BOOTSTRAP_FAKE_FAIL_STAGE" = "tool-restore" ]; then exit 31; fi
if [ "$1" = "restore" ]; then
    if [ -n "$BOOTSTRAP_RESTORE_CAPTURE_PATH" ]; then printf '%s\n' "$*" >> "$BOOTSTRAP_RESTORE_CAPTURE_PATH"; fi
    if [ "$BOOTSTRAP_FAKE_FAIL_STAGE" = "restore" ]; then exit 32; fi
fi
if [ "$1" = "build" ]; then
    if [ -n "$BOOTSTRAP_BUILD_CAPTURE_PATH" ]; then printf '%s\n' "$*" >> "$BOOTSTRAP_BUILD_CAPTURE_PATH"; fi
    if [ "$BOOTSTRAP_FAKE_FAIL_STAGE" = "build" ]; then exit 33; fi
    test_app='artifacts/devops/bin/Rowles.LeanCorpus.DevOps.Tests/release/Rowles.LeanCorpus.DevOps.Tests'
    mkdir -p "$(dirname "$test_app")"
    cat > "$test_app" <<'HOST'
#!/bin/sh
exit 0
HOST
    chmod 755 "$test_app"
    exit 0
fi
if [ "$1" = "publish" ]; then
    if [ -n "$BOOTSTRAP_PUBLISH_CAPTURE_PATH" ]; then printf '%s\n' "$*" >> "$BOOTSTRAP_PUBLISH_CAPTURE_PATH"; fi
    if [ "$BOOTSTRAP_FAKE_FAIL_STAGE" = "publish" ]; then exit 34; fi
    output=''
    previous=''
    for argument in "$@"; do
        if [ "$previous" = '--output' ]; then output="$argument"; fi
        previous="$argument"
    done
    mkdir -p "$output"
    cat > "$output/leancorpus-devops" <<'HOST'
#!/bin/sh
staged=0
case "$0" in */bootstrap-staging/*) staged=1 ;; esac
if [ "$1" = "--help" ]; then
    if [ "$BOOTSTRAP_FAKE_FAIL_STAGE" = "staged-help" ] && [ "$staged" = "1" ]; then exit 17; fi
    if [ "$BOOTSTRAP_FAKE_FAIL_STAGE" = "promoted-help" ] && [ "$staged" = "0" ]; then exit 18; fi
    printf '%s\n' 'LeanCorpus managed DevOps fixture'
    exit 0
fi
if [ "$1" = "--version" ]; then printf '%s\n' '1.0.0-fixture'; exit 0; fi
if [ -n "$DEVOPS_CAPTURE_PATH" ]; then printf '%s\n' "$@" > "$DEVOPS_CAPTURE_PATH"; fi
exit 0
HOST
    chmod 755 "$output/leancorpus-devops"
    if [ "$BOOTSTRAP_FAKE_SIDECAR" = "1" ]; then printf '%s\n' 'unexpected' > "$output/leancorpus-devops.deps.json"; fi
    exit 0
fi
exit 0
'@

$script:Checks = 0
function Assert-BootstrapContract {
    param([Parameter(Mandatory = $true)][bool]$Condition, [Parameter(Mandatory = $true)][string]$Message)
    if (-not $Condition) { throw "Managed bootstrap contract regression: $Message" }
    $script:Checks++
}

function New-BootstrapFixture {
    param([Parameter(Mandatory = $true)][string]$Name, [switch]$WithoutGit)

    $root = Join-Path $tempRoot $Name
    $fakeBin = Join-Path $root 'fake bin'
    $fakeHome = Join-Path $root 'fake home'
    [void][System.IO.Directory]::CreateDirectory($fakeBin)
    [void][System.IO.Directory]::CreateDirectory($fakeHome)
    foreach ($relative in @(
        '.config', 'src/devops', 'src/devops/Rowles.LeanCorpus.DevOps', 'src/devops/Rowles.LeanCorpus.DevOps.Tests', '.config/devops',
        'scripts/devops')) {
        [void][System.IO.Directory]::CreateDirectory((Join-Path $root $relative))
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'setup.ps1') -Destination $root
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/devops/managed-host.ps1') -Destination (Join-Path $root 'scripts/devops')

    $files = @{
        'Rowles.LeanCorpus.slnx' = '<Solution />'
        'global.json' = '{"sdk":{"version":"11.0.100-preview.7.26381.103","allowPrerelease":true}}'
        '.config/dotnet-tools.json' = '{"version":1,"isRoot":true,"tools":{}}'
        'Directory.Build.props' = '<Project />'
        'Directory.Packages.props' = '<Project />'
        'src/devops/Directory.Build.props' = '<Project />'
        'src/devops/Directory.Packages.props' = '<Project />'
        'src/devops/Rowles.LeanCorpus.DevOps.Tests/Rowles.LeanCorpus.DevOps.Tests.csproj' = '<Project />'
        'src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj' = '<Project />'
        'src/devops/Rowles.LeanCorpus.DevOps/Program.cs' = 'fixture source'
        '.config/devops/benchmark-datasets.json' = '{}'
        '.config/devops/benchmark-groups.json' = '{}'
        '.config/devops/benchmark-strategies.json' = '{}'
        '.config/devops/benchmark-suites.json' = '{}'
        '.config/devops/code-areas.json' = '{}'
        '.config/devops/test-suites.json' = '{"version":1}'
    }
    foreach ($entry in $files.GetEnumerator()) {
        $path = Join-Path $root $entry.Key
        [System.IO.File]::WriteAllText($path, $entry.Value, [System.Text.UTF8Encoding]::new($false))
    }

    if (-not $WithoutGit) {
        & $gitCommand.Source -C $root init --quiet
        if ($LASTEXITCODE -ne 0) { throw "Could not initialise temporary Git checkout '$root'." }
    }

    $dotnetPath = Join-Path $fakeBin 'dotnet'
    [System.IO.File]::WriteAllText($dotnetPath, $fakeDotnet.TrimStart([char[]]"`r`n"), [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::SetUnixFileMode($dotnetPath, [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite -bor [System.IO.UnixFileMode]::UserExecute -bor
        [System.IO.UnixFileMode]::GroupRead -bor [System.IO.UnixFileMode]::GroupExecute -bor [System.IO.UnixFileMode]::OtherRead -bor [System.IO.UnixFileMode]::OtherExecute)

    return [pscustomobject]@{ Root = $root; FakeBin = $fakeBin; FakeHome = $fakeHome; Setup = Join-Path $root 'setup.ps1' }
}

function Set-BootstrapSentinel {
    param([Parameter(Mandatory = $true)][object]$Fixture)
    $hostPath = Join-Path $Fixture.Root 'devops'
    $manifestPath = Join-Path $Fixture.Root 'artifacts/devops/managed-host.json'
    [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($manifestPath))
    [System.IO.File]::WriteAllText($hostPath, 'previous apphost sentinel')
    [System.IO.File]::WriteAllText($manifestPath, 'previous manifest sentinel')
    return [pscustomobject]@{ Host = $hostPath; Manifest = $manifestPath }
}

function Invoke-BootstrapFixture {
    param(
        [Parameter(Mandatory = $true)][object]$Fixture,
        [string]$FailureStage = '',
        [switch]$UnexpectedSidecar,
        [string]$RestoreCapturePath = '',
        [string]$BuildCapturePath = '',
        [string]$PublishCapturePath = ''
    )

    $previousPath = $env:PATH
    $previousHome = $env:HOME
    $previousFailure = $env:BOOTSTRAP_FAKE_FAIL_STAGE
    $previousSidecar = $env:BOOTSTRAP_FAKE_SIDECAR
    $previousCapturePath = $env:DEVOPS_CAPTURE_PATH
    $previousRestoreCapturePath = $env:BOOTSTRAP_RESTORE_CAPTURE_PATH
    $previousBuildCapturePath = $env:BOOTSTRAP_BUILD_CAPTURE_PATH
    $previousPublishCapturePath = $env:BOOTSTRAP_PUBLISH_CAPTURE_PATH
    try {
        $env:PATH = $Fixture.FakeBin + [System.IO.Path]::PathSeparator + $previousPath
        $env:HOME = $Fixture.FakeHome
        $env:BOOTSTRAP_FAKE_FAIL_STAGE = $FailureStage
        $env:BOOTSTRAP_FAKE_SIDECAR = if ($UnexpectedSidecar) { '1' } else { '0' }
        $env:DEVOPS_CAPTURE_PATH = ''
        $env:BOOTSTRAP_RESTORE_CAPTURE_PATH = $RestoreCapturePath
        $env:BOOTSTRAP_BUILD_CAPTURE_PATH = $BuildCapturePath
        $env:BOOTSTRAP_PUBLISH_CAPTURE_PATH = $PublishCapturePath
        $output = (& $powerShell.Source -NoProfile -File $Fixture.Setup 2>&1 | Out-String)
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    } finally {
        $env:PATH = $previousPath
        $env:HOME = $previousHome
        $env:BOOTSTRAP_FAKE_FAIL_STAGE = $previousFailure
        $env:BOOTSTRAP_FAKE_SIDECAR = $previousSidecar
        $env:DEVOPS_CAPTURE_PATH = $previousCapturePath
        $env:BOOTSTRAP_RESTORE_CAPTURE_PATH = $previousRestoreCapturePath
        $env:BOOTSTRAP_BUILD_CAPTURE_PATH = $previousBuildCapturePath
        $env:BOOTSTRAP_PUBLISH_CAPTURE_PATH = $previousPublishCapturePath
    }
}

function Test-BootstrapFailurePreservesHost {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$FailureStage = '',
        [string]$ExpectedText,
        [string]$MissingInput = '',
        [switch]$WithoutGit,
        [switch]$UnexpectedSidecar
    )

    $fixture = New-BootstrapFixture -Name $Name -WithoutGit:$WithoutGit
    try {
        if ($MissingInput) {
            Remove-Item -LiteralPath (Join-Path $fixture.Root $MissingInput) -Force
        }
        $sentinel = Set-BootstrapSentinel -Fixture $fixture
        $restoreCapturePath = ''
        $buildCapturePath = ''
        $publishCapturePath = ''
        if ($MissingInput) {
            $restoreCapturePath = Join-Path $fixture.Root 'restore-calls.txt'
            $buildCapturePath = Join-Path $fixture.Root 'build-calls.txt'
            $publishCapturePath = Join-Path $fixture.Root 'publish-calls.txt'
        }
        $result = Invoke-BootstrapFixture -Fixture $fixture -FailureStage $FailureStage -UnexpectedSidecar:$UnexpectedSidecar -RestoreCapturePath $restoreCapturePath -BuildCapturePath $buildCapturePath -PublishCapturePath $publishCapturePath
        Assert-BootstrapContract ($result.ExitCode -ne 0) "$Name should fail"
        $outputForAssertion = [regex]::Replace($result.Output, '[\r\n|]+', ' ')
        $outputForAssertion = [regex]::Replace($outputForAssertion, '\s+', ' ')
        if ($ExpectedText -and $outputForAssertion -notlike "*$ExpectedText*") {
            throw "Managed bootstrap contract regression: $Name should report '$ExpectedText'. Actual output: $($result.Output)"
        }
        if ($MissingInput) {
            $capturedCommands = @(@($restoreCapturePath, $buildCapturePath, $publishCapturePath) | Where-Object { Test-Path -LiteralPath $_ })
            Assert-BootstrapContract ($capturedCommands.Count -eq 0) "$Name should fail before restore, build or publish"
        }
        Assert-BootstrapContract ([System.IO.File]::ReadAllText($sentinel.Host) -ceq 'previous apphost sentinel') "$Name must preserve the previous root apphost"
        Assert-BootstrapContract ([System.IO.File]::ReadAllText($sentinel.Manifest) -ceq 'previous manifest sentinel') "$Name must preserve the previous promotion manifest"
    } finally {
        Remove-Item -LiteralPath $fixture.Root -Recurse -Force
    }
}

try {
    Test-BootstrapFailurePreservesHost -Name 'wrong checkout' -WithoutGit -ExpectedText 'not a LeanCorpus checkout'
    Test-BootstrapFailurePreservesHost -Name 'missing managed test project' -MissingInput 'src/devops/Rowles.LeanCorpus.DevOps.Tests/Rowles.LeanCorpus.DevOps.Tests.csproj' -ExpectedText 'required bootstrap input'
    Test-BootstrapFailurePreservesHost -Name 'missing managed host project' -MissingInput 'src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj' -ExpectedText 'required bootstrap input'
    foreach ($configuration in @('benchmark-datasets.json', 'benchmark-groups.json', 'benchmark-strategies.json', 'benchmark-suites.json', 'code-areas.json', 'test-suites.json')) {
        Test-BootstrapFailurePreservesHost -Name "missing $configuration" -MissingInput ".config/devops/$configuration" -ExpectedText 'required bootstrap input'
    }
    Test-BootstrapFailurePreservesHost -Name 'missing SDK' -FailureStage 'missing-sdk' -ExpectedText 'SDK required by global.json was not found'
    Test-BootstrapFailurePreservesHost -Name 'tool restore failure' -FailureStage 'tool-restore' -ExpectedText 'Repository tool restore failed'
    Test-BootstrapFailurePreservesHost -Name 'locked restore failure' -FailureStage 'restore' -ExpectedText 'failed with exit code 32'
    Test-BootstrapFailurePreservesHost -Name 'build failure' -FailureStage 'build' -ExpectedText 'failed with exit code 33'
    Test-BootstrapFailurePreservesHost -Name 'publish failure' -FailureStage 'publish' -ExpectedText 'failed with exit code 34'
    Test-BootstrapFailurePreservesHost -Name 'unexpected sidecar' -UnexpectedSidecar -ExpectedText 'unexpected sidecars'
    Test-BootstrapFailurePreservesHost -Name 'staged probe failure' -FailureStage 'staged-help' -ExpectedText 'Staged managed host --help probe failed'
    Test-BootstrapFailurePreservesHost -Name 'promoted probe rollback' -FailureStage 'promoted-help' -ExpectedText 'Promoted managed host --help probe failed'

    $fixture = New-BootstrapFixture -Name 'successful staged promotion'
    try {
        $sentinel = Set-BootstrapSentinel -Fixture $fixture
        $restoreCapturePath = Join-Path $fixture.Root 'restore-calls.txt'
        $buildCapturePath = Join-Path $fixture.Root 'build-calls.txt'
        $publishCapturePath = Join-Path $fixture.Root 'publish-calls.txt'
        $result = Invoke-BootstrapFixture -Fixture $fixture -RestoreCapturePath $restoreCapturePath -BuildCapturePath $buildCapturePath -PublishCapturePath $publishCapturePath
        Assert-BootstrapContract ($result.ExitCode -eq 0) 'successful bootstrap should complete'
        $restoreCalls = [System.IO.File]::ReadAllLines($restoreCapturePath)
        Assert-BootstrapContract ($restoreCalls.Count -eq 2) 'setup should perform both locked restores'
        Assert-BootstrapContract (@($restoreCalls | Where-Object { $_ -notmatch '(^| )--locked-mode( |$)' -or $_ -notmatch '(^| )--disable-build-servers( |$)' -or $_ -notmatch '(^| )-m:1( |$)' }).Count -eq 0) 'both locked restore calls should disable build servers and use a single MSBuild node'
        $testProject = 'src/devops/Rowles.LeanCorpus.DevOps.Tests/Rowles.LeanCorpus.DevOps.Tests.csproj'
        $hostProject = 'src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj'
        Assert-BootstrapContract (@($restoreCalls | Where-Object { $_ -like "*$testProject*" }).Count -eq 1 -and @($restoreCalls | Where-Object { $_ -like "*$hostProject*" }).Count -eq 1) 'setup should restore the test graph and host project independently'
        $buildCalls = [System.IO.File]::ReadAllLines($buildCapturePath)
        Assert-BootstrapContract ('build src/devops/Rowles.LeanCorpus.DevOps.slnx --configuration Release' -match '\.slnx(?:\s|$)') 'solution target detector should recognise a .slnx command'
        Assert-BootstrapContract ($buildCalls.Count -eq 1 -and $buildCalls[0] -like "*$testProject*" -and $buildCalls[0] -notmatch '\.slnx(?:\s|$)') 'setup should build the test project graph without a solution file'
        Assert-BootstrapContract ($buildCalls[0] -notmatch '\\n') 'build command capture should write a real line terminator, not a literal backslash-n'
        $publishCalls = [System.IO.File]::ReadAllLines($publishCapturePath)
        Assert-BootstrapContract ($publishCalls.Count -eq 1 -and $publishCalls[0] -match '(^| )-p:IncludeNativeLibrariesForSelfExtract=true( |$)') 'single-file publish should bundle native libraries without root sidecars'
        if ($result.Output -notmatch 'Staged managed host --help probe' -or $result.Output -notmatch 'Staged managed host --version probe') {
            throw "Managed bootstrap contract regression: staged help and version probes should run. Actual output: $($result.Output)"
        }
        Assert-BootstrapContract ($result.Output -match 'Staged managed build smoke') 'staged build smoke should run'
        Assert-BootstrapContract ([System.IO.File]::ReadAllText($sentinel.Host) -ne 'previous apphost sentinel') 'successful promotion should replace the root apphost'
        $status = Test-ManagedHost -RepositoryRoot $fixture.Root -ExecutablePath $sentinel.Host
        Assert-BootstrapContract $status.IsCurrent 'promoted fixture host should pass its source and executable fingerprint'
        Assert-BootstrapContract (-not (Test-Path -LiteralPath ($sentinel.Host + '.deps.json'))) 'root promotion should leave no sidecar'

        $dispatcher = Join-Path $fixture.Root 'devops.ps1'
        Copy-Item -LiteralPath (Join-Path $repoRoot 'devops.ps1') -Destination $dispatcher
        $capturePath = Join-Path $fixture.Root 'forwarded-arguments.txt'
        $previousCapturePath = $env:DEVOPS_CAPTURE_PATH
        try {
            $env:DEVOPS_CAPTURE_PATH = $capturePath
            & $powerShell.Source -NoProfile -File $dispatcher report git -Json -Top 3
            Assert-BootstrapContract ($LASTEXITCODE -eq 0) 'PowerShell report invocation should reach the promoted managed host'
            $forwarded = [System.IO.File]::ReadAllLines($capturePath)
            Assert-BootstrapContract (($forwarded -join '|') -ceq 'report|git|-Json|-Top|3') 'PowerShell report invocation should preserve argument token boundaries and ordering'

            & $powerShell.Source -NoProfile -File $dispatcher coverage -Suite sourcegen -Framework net11.0 -GenerateReport
            Assert-BootstrapContract ($LASTEXITCODE -eq 0) 'PowerShell coverage invocation should reach the promoted managed host'
            $forwarded = [System.IO.File]::ReadAllLines($capturePath)
            Assert-BootstrapContract (($forwarded -join '|') -ceq 'coverage|-Suite|sourcegen|-Framework|net11.0|-GenerateReport') 'PowerShell coverage invocation should preserve argument token boundaries and ordering'

            & $powerShell.Source -NoProfile -File $dispatcher diagnostics trace --pid 42 -- --providers Microsoft-DotNETRuntime
            Assert-BootstrapContract ($LASTEXITCODE -eq 0) 'PowerShell diagnostics invocation should reach the promoted managed host'
            $forwarded = [System.IO.File]::ReadAllLines($capturePath)
            Assert-BootstrapContract (($forwarded -join '|') -ceq 'diagnostics|trace|--pid|42|--|--providers|Microsoft-DotNETRuntime') 'PowerShell diagnostics invocation should preserve argument token boundaries and ordering'

            & $powerShell.Source -NoProfile -File $dispatcher benchmark remote start --host jordan@bench --path '/srv/LeanCorpus checkout' --branch issue-97 -- --suite query --filter '*Term Query*'
            Assert-BootstrapContract ($LASTEXITCODE -eq 0) 'PowerShell benchmark invocation should reach the promoted managed host'
            $forwarded = [System.IO.File]::ReadAllLines($capturePath)
            Assert-BootstrapContract (($forwarded -join '|') -ceq 'benchmark|remote|start|--host|jordan@bench|--path|/srv/LeanCorpus checkout|--branch|issue-97|--|--suite|query|--filter|*Term Query*') 'PowerShell benchmark invocation should preserve exact remote and benchmark argument tokens'

            & $powerShell.Source -NoProfile -File $dispatcher test core --ci --filter 'FullyQualifiedName~Writer' --timeout 2m
            Assert-BootstrapContract ($LASTEXITCODE -eq 0) 'PowerShell test invocation should reach the promoted managed host'
            $forwarded = [System.IO.File]::ReadAllLines($capturePath)
            Assert-BootstrapContract (($forwarded -join '|') -ceq 'test|core|--ci|--filter|FullyQualifiedName~Writer|--timeout|2m') 'PowerShell test invocation should preserve selection, CI and timeout tokens'

            & $powerShell.Source -NoProfile -File $dispatcher aot --framework net10.0 --rid linux-x64
            Assert-BootstrapContract ($LASTEXITCODE -eq 0) 'PowerShell AOT invocation should reach the promoted managed host'
            $forwarded = [System.IO.File]::ReadAllLines($capturePath)
            Assert-BootstrapContract (($forwarded -join '|') -ceq 'aot|--framework|net10.0|--rid|linux-x64') 'PowerShell AOT invocation should preserve framework and RID tokens'

            & $powerShell.Source -NoProfile -File $dispatcher ui
            Assert-BootstrapContract ($LASTEXITCODE -eq 0) 'PowerShell workbench invocation should reach the promoted managed host'
            $forwarded = [System.IO.File]::ReadAllLines($capturePath)
            Assert-BootstrapContract (($forwarded -join '|') -ceq 'ui') 'PowerShell workbench invocation should preserve the managed command token'

            $managedArgumentCases = @(
                ,@('docs', 'build', '--skip-benchmarks')
                ,@('dataforge', 'profiles')
                ,@('data', 'gutenberg', '--book-count', '12')
                ,@('config', 'validate')
                ,@('benchmark', '--list')
                ,@('pack', '--help')
                ,@('distribute', '--help')
            )
            foreach ($managedArguments in $managedArgumentCases) {
                $description = $managedArguments -join ' '
                & $powerShell.Source -NoProfile -File $dispatcher @managedArguments
                Assert-BootstrapContract ($LASTEXITCODE -eq 0) "Managed compatibility invocation '$description' should reach the promoted host"
                $forwarded = [System.IO.File]::ReadAllLines($capturePath)
                Assert-BootstrapContract (($forwarded -join '|') -ceq ($managedArguments -join '|')) "Managed compatibility invocation '$description' should preserve every argument token"
            }

            $helpOutput = & $powerShell.Source -NoProfile -File $dispatcher
            Assert-BootstrapContract ($LASTEXITCODE -eq 0) 'An empty compatibility invocation should display managed help'
            Assert-BootstrapContract (($helpOutput -join "`n") -match 'LeanCorpus managed DevOps fixture') 'An empty compatibility invocation should forward managed help without loading the legacy application'
        } finally {
            $env:DEVOPS_CAPTURE_PATH = $previousCapturePath
        }
    } finally {
        Remove-Item -LiteralPath $fixture.Root -Recurse -Force
    }

    Write-Host "Managed bootstrap contract smoke passed ($script:Checks checks)."
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
