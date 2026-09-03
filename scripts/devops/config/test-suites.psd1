@{
    core = @{
        Name = 'Core'
        Project = 'src/devops/Rowles.LeanCorpus.Tests.Core/Rowles.LeanCorpus.Tests.Core.csproj'
        Runner = 'Mtp'
        Frameworks = @('net10.0', 'net11.0')
        Coverage = $true
        Capabilities = @('Trx', 'HangDump', 'CrashDump')
    }
    text = @{
        Name = 'Text'
        Project = 'src/devops/Rowles.Text.Tests/Rowles.Text.Tests.csproj'
        Runner = 'Mtp'
        Frameworks = @('net10.0', 'net11.0')
        Coverage = $false
        Capabilities = @('Trx', 'HangDump', 'CrashDump')
    }
    sourcegen = @{
        Name = 'SourceGen'
        Project = 'src/devops/Rowles.LeanCorpus.Tests.SourceGen/Rowles.LeanCorpus.Tests.SourceGen.csproj'
        Runner = 'Mtp'
        Frameworks = @('net10.0', 'net11.0')
        Coverage = $true
        Capabilities = @('Trx', 'HangDump', 'CrashDump')
    }
    architecture = @{
        Name = 'Architecture'
        Project = 'src/devops/Rowles.LeanCorpus.Tests.Architecture/Rowles.LeanCorpus.Tests.Architecture.csproj'
        Runner = 'Mtp'
        Frameworks = @('net10.0', 'net11.0')
        Coverage = $false
        Capabilities = @('Trx')
    }
    'server-abstractions' = @{
        Name = 'Server Abstractions'
        Project = 'src/server/devops/Rowles.LeanCorpus.Server.Abstractions.Tests/Rowles.LeanCorpus.Server.Abstractions.Tests.csproj'
        Runner = 'Mtp'
        Frameworks = @('net11.0')
        DefaultFramework = 'net11.0'
        Coverage = $false
        Capabilities = @('Trx', 'HangDump', 'CrashDump')
    }
    'server-core' = @{
        Name = 'Server Core'
        Project = 'src/server/devops/Rowles.LeanCorpus.Server.Core.Tests/Rowles.LeanCorpus.Server.Core.Tests.csproj'
        Runner = 'Mtp'
        Frameworks = @('net11.0')
        DefaultFramework = 'net11.0'
        Coverage = $false
        Capabilities = @('Trx', 'HangDump', 'CrashDump')
    }
    'server-integration' = @{
        Name = 'Server Integration'
        Project = 'src/server/devops/Rowles.LeanCorpus.Server.Integration.Tests/Rowles.LeanCorpus.Server.Integration.Tests.csproj'
        Runner = 'Mtp'
        Frameworks = @('net11.0')
        DefaultFramework = 'net11.0'
        Coverage = $false
        Capabilities = @('Trx', 'HangDump', 'CrashDump')
    }
    aot = @{
        Name = 'AOT'
        Project = 'src/devops/Rowles.LeanCorpus.Tests.AOTSmoke/Rowles.LeanCorpus.Tests.AOTSmoke.csproj'
        Runner = 'AotNative'
        Frameworks = @('net10.0', 'net11.0')
        ExpandFrameworksByDefault = $true
        Coverage = $false
        Capabilities = @()
    }
}
