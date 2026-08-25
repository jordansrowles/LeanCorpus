# LeanCorpus examples

The examples are runnable applications, each focused on a complete job rather than an isolated API.

## Choose where to start

| Goal | Example | Typical setup |
| --- | --- | --- |
| See the smallest working text-analysis program | [Rowles.Text tokenisation](Rowles.Text.Example.Tokenise/) | No external data |
| Index and search arbitrary JSON over HTTP | [JSON API](Rowles.LeanCorpus.Example.JsonApi/) | Local data directory |
| Export traces, metrics and structured logs | [Telemetry](Rowles.LeanCorpus.Example.Telemetry/) | Optional OTLP endpoint |
| Ingest a realistic text corpus | [Newsgroups indexer](Rowles.LeanCorpus.Example.NewsgroupsIndexer/) | 20 Newsgroups data |
| Reproduce a high-segment-count workload | [Linux kernel code search](e2e/Rowles.LeanCorpus.Example.LinuxKernelCodeSearch/) | Kernel source and substantial disk space |

> [!TIP]
> New to the repository? Run the Rowles.Text example first, then the JSON API. Both give useful output without downloading a large corpus.

## Tokenise text

```bash
dotnet run --project src/examples/Rowles.Text.Example.Tokenise
```

Expected result: the console prints each token with its start and end offsets.

## Run the JSON API

```bash
dotnet run --project src/examples/Rowles.LeanCorpus.Example.JsonApi
```

Index a document:

```bash
curl -X POST http://localhost:5000/collections/books/documents \
  -H "Content-Type: application/json" \
  -d '{"id":"1","title":"The quick brown fox","content":"Local search with LeanCorpus"}'
```

Search it:

```bash
curl "http://localhost:5000/collections/books/search?q=local&field=content"
```

The application stores index data under `./data` unless `LEANCORPUS_DATA_PATH` is configured.

## Run the telemetry example

```bash
dotnet run --project src/examples/Rowles.LeanCorpus.Example.Telemetry
```

The example continuously indexes and searches a small book corpus while emitting console telemetry. Set `OTEL_EXPORTER_OTLP_ENDPOINT` to send data to an OpenTelemetry collector or Aspire dashboard.

## Index 20 Newsgroups

Prepare the corpus:

```bash
./devops data news
```

Run a bounded ingestion first:

```bash
dotnet run --project src/examples/Rowles.LeanCorpus.Example.NewsgroupsIndexer -- --limit 500
```

Expected result: the command reports the indexed document count, source path and output index path.

> [!NOTE]
> Pass `--source <path>` when the corpus is outside the repository data directory. Pass `--index <path>` to control where the index is written.

## Reproduce the Linux kernel workload

Start with the bounded smoke flow in the [Linux kernel example README](e2e/Rowles.LeanCorpus.Example.LinuxKernelCodeSearch/README.md). The full run indexes tens of millions of source lines and is intended for controlled performance investigation.

> [!WARNING]
> The large examples create persistent index data. Review their output paths and available disk space before removing bounds or running a full corpus.

## Native AOT

Native AOT is validated by the repository smoke suite rather than a usage example:

```bash
./devops aot
```

## Add or update an example

Keep an example focused on one user journey. Include its prerequisites, exact run command, expected result, output location and clean-up instructions. Update this index when adding or moving an example.
