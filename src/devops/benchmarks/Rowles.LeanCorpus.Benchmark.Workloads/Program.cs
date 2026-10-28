using System.Text.Json;
using Rowles.LeanCorpus.Benchmark.Workloads;

if (args.Length != 2)
{
    await Console.Error.WriteLineAsync("The internal worker requires input and terminal-output JSON paths.");
    return 2;
}
try
{
    var input = await File.ReadAllBytesAsync(args[0], CancellationToken.None);
    if (input.Length > 1_048_576)
        throw new InvalidDataException("Workload input exceeds the protocol limit.");
    var request = JsonSerializer.Deserialize<WorkloadRequest>(input, WorkloadProtocol.JsonOptions)
        ?? throw new InvalidDataException("Missing workload input.");
    var result = await new WorkloadRunner().RunAsync(request, args[1] + ".partial");
    WorkloadProtocol.WriteResult(args[1], result);
    return result.Outcome == "Completed" ? 0 : result.Outcome == "Cancelled" ? 130 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 2;
}
