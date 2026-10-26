using Rowles.LeanCorpus.Server.Abstractions.Services;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Services;

string dataRoot = Path.Combine(Environment.CurrentDirectory, "server-core-data");
using (LocalServerCore server = await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = dataRoot }))
{
    if (server is not IHealthService) throw new InvalidDataException("Server Core did not expose the health contract.");
}
Directory.Delete(dataRoot, recursive: true);
if (Directory.Exists(dataRoot)) throw new IOException("Server Core consumer data cleanup failed.");
Console.WriteLine("SERVER CORE local service graph passed.");