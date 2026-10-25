# LeanCorpus solutions

`Rowles.LeanCorpus.slnx` is the canonical inventory of every checked-in
`src/**/*.csproj`. The filters provide explicit build selections; building a
filter does not run its tests, benchmarks or AOT publish workflows. Project
references may bring in dependencies outside a filter's direct list.

Build from the repository root with an explicit solution path. The repository
root intentionally has no solution file:

```bash
dotnet build src/Rowles.LeanCorpus.slnx --configuration Release
dotnet build src/solutions/core.slnf --configuration Release
dotnet build src/solutions/text.slnf --configuration Release
dotnet build src/solutions/sourcegen.slnf --configuration Release
dotnet build src/solutions/server.slnf --configuration Release
dotnet build src/solutions/orchard.slnf --configuration Release
dotnet build src/solutions/devops.slnf --configuration Release
dotnet build src/solutions/dataforge.slnf --configuration Release
dotnet build src/solutions/benchmarks.slnf --configuration Release
dotnet build src/solutions/aot.slnf --configuration Release
dotnet build src/solutions/examples.slnf --configuration Release
dotnet build src/solutions/tests.slnf --configuration Release
./devops config validate --solution-inventory
```

The filter's `solution.path` is relative to that filter file. Each project path
is relative to the canonical master solution. Keep filter membership explicit;
overlap is expected where one project serves several workflows. When a project
is added or moved, update the master and each relevant filter, then run the
managed solution-inventory validation. It compares Git-tracked `src/**/*.csproj`
paths with the master and checks that every filter points back to that master
and selects only projects listed there. It reports drift without rewriting files.

| Filter | Direct projects |
| --- | --- |
| `core` | Core, the three compression packages, Core tests and shared test support |
| `text` | Rowles.Text, its tests and the tokenisation example |
| `sourcegen` | Source generator and its tests |
| `server` | Server abstractions, Core, transports, local host, Studio and non-Orchard Server tests |
| `orchard` | Orchard provider, provider tests, test host, test module and functional tests |
| `devops` | Managed DevOps host, host tests, process-test child and LeanCorpus CLI |
| `dataforge` | DataForge library, workloads, tool and tests |
| `benchmarks` | Core, Text, compression, profiling and benchmark workload projects |
| `aot` | Native AOT smoke test project; publishing and execution remain explicit DevOps commands |
| `examples` | All examples, including the Text tokeniser and Linux kernel code-search example |
| `tests` | All Core, Text, source-generator, architecture, docs, Server and Orchard test projects |
