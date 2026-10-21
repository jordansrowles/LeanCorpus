namespace Rowles.LeanCorpus.DevOps.Commands;

internal interface ICommandHandler
{
    int Build(BuildCommandOptions options);

    int Clean(CleanCommandOptions options);

    int StartServer(ServerStartCommandOptions options);

    int Report(ReportCommandOptions options);

    int Pack(PackCommandOptions options);

    int Data(DataCommandOptions options);

    int DataForge(DataForgeCommandOptions options);

    int Diagnostics(DiagnosticsCommandOptions options);

    int Benchmark(BenchmarkCommandOptions options);

    int BenchmarkRemote(BenchmarkRemoteCommandOptions options);
}
