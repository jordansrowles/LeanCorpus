namespace Rowles.LeanCorpus.DevOps.Commands;

internal interface ICommandHandler
{
    int Build(BuildCommandOptions options);

    int Clean(CleanCommandOptions options);

    int Docs(DocsCommandOptions options);

    int StartServer(ServerStartCommandOptions options);

    int Report(ReportCommandOptions options);

    int Coverage(CoverageCommandOptions options);

    int Pack(PackCommandOptions options);

    int Distribute(DistributeCommandOptions options);

    int Data(DataCommandOptions options);

    int DataForge(DataForgeCommandOptions options);

    int Diagnostics(DiagnosticsCommandOptions options);

    int Benchmark(BenchmarkCommandOptions options);

    int BenchmarkRemote(BenchmarkRemoteCommandOptions options);

    int Ui(UiCommandOptions options);

    int Test(TestCommandOptions options);

    int BackupRestoreMatrix(BackupRestoreMatrixCommandOptions options);
}
