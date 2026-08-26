@{
    core          = @{ Name = 'Core';          Project = 'src/devops/Rowles.LeanCorpus.Tests.Core/Rowles.LeanCorpus.Tests.Core.csproj' }
    text          = @{ Name = 'Text';          Project = 'src/devops/Rowles.Text.Tests/Rowles.Text.Tests.csproj' }
    sourcegen     = @{ Name = 'SourceGen';     Project = 'src/devops/Rowles.LeanCorpus.Tests.SourceGen/Rowles.LeanCorpus.Tests.SourceGen.csproj' }
    architecture  = @{ Name = 'Architecture';  Project = 'src/devops/Rowles.LeanCorpus.Tests.Architecture/Rowles.LeanCorpus.Tests.Architecture.csproj' }
    'server-abstractions' = @{ Name = 'Server Abstractions'; Project = 'src/server/devops/Rowles.LeanCorpus.Server.Abstractions.Tests/Rowles.LeanCorpus.Server.Abstractions.Tests.csproj'; Framework = 'net11.0' }
    'server-core'   = @{ Name = 'Server Core';   Project = 'src/server/devops/Rowles.LeanCorpus.Server.Core.Tests/Rowles.LeanCorpus.Server.Core.Tests.csproj'; Framework = 'net11.0' }
    'server-integration' = @{ Name = 'Server Integration'; Project = 'src/server/devops/Rowles.LeanCorpus.Server.Integration.Tests/Rowles.LeanCorpus.Server.Integration.Tests.csproj'; Framework = 'net11.0' }
    aot           = @{ Name = 'AOT';           Command = 'aot' }
}
