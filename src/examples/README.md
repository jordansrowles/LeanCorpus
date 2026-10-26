# LeanCorpus workload example

The repository retains one runnable application: the Linux kernel code-search
workload. It indexes Linux kernel source line by line and records search and
indexing measurements. Start with its bounded smoke flow in the
[workload README](e2e/Rowles.LeanCorpus.Example.LinuxKernelCodeSearch/README.md).

The smoke flow requires a local Linux kernel checkout, a dedicated index path
and sufficient disk space. Build it with the repository's examples filter:

```bash
dotnet build src/solutions/examples.slnf --configuration Release
```

Telemetry and text-analysis usage are documented in their respective guides;
their former demonstration applications are retired.
