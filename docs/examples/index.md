---
title: Examples
_description: Runnable LeanCorpus and Rowles.Text examples.
---

# Examples

These projects are small, runnable applications. Start with the first index
guide for the shortest path, then use an example when you need an end-to-end
shape.

| Example | What it demonstrates | Packages | Run |
| --- | --- | --- | --- |
| JSON search API | Minimal ASP.NET Core API with indexed JSON documents and search endpoints. | LeanCorpus | `dotnet run --project src/examples/Rowles.LeanCorpus.Example.JsonApi` |
| Linux kernel code search | End-to-end indexing and search over a large source-code corpus. | LeanCorpus | `dotnet run --project src/examples/e2e/Rowles.LeanCorpus.Example.LinuxKernelCodeSearch` |
| Newsgroups indexer | Batch indexes a real text corpus and demonstrates long-running ingestion. | LeanCorpus | `dotnet run --project src/examples/Rowles.LeanCorpus.Example.NewsgroupsIndexer` |
| Rowles.Text tokenisation | Uses the standalone analysis package without an index or searcher. | Rowles.Text | `dotnet run --project src/examples/Rowles.Text.Example.Tokenise` |
| Telemetry worker | Emits LeanCorpus metrics and tracing through OpenTelemetry. | LeanCorpus and OpenTelemetry | `dotnet run --project src/examples/Rowles.LeanCorpus.Example.Telemetry` |
