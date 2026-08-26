# LeanCorpus Community Server

LeanCorpus Community Server `0.1.0-alpha.1` is a local, single-node HTTP and gRPC host for LeanCorpus. It has no licence, account, cluster or external-service dependency.

## Quick start

From the repository root:

```bash
./devops build
./devops server start
```

The reference host runs in the foreground on .NET 11 and binds to `127.0.0.1:5080` and `[::1]:5080`. It stores the registry and physical index data below `data/` by default. Use `LeanCorpus:DataRoot` in configuration or `--LeanCorpus:DataRoot=/path/to/data` to select another root.

For a trusted network only, use `./devops server start -External`. This binds `0.0.0.0:5080`, does not add TLS or authentication, and must be protected by the hosting environment.

Check the host before creating an index:

```bash
curl http://127.0.0.1:5080/v1/health
curl http://127.0.0.1:5080/v1/ready
```

## First index and write

Create a logical index with an authoritative schema. Field `type` uses the `IndexFieldType` values, so `0` is Text, `1` is Keyword and `2` is Int64.

```bash
curl -X PUT http://127.0.0.1:5080/v1/indices/books \
  -H 'Content-Type: application/json' \
  -d '{
    "indexName":"books",
    "schema":{"fields":[
      {"name":"title","type":0,"indexed":true,"stored":true,"multiValued":false,"analyser":"standard"},
      {"name":"isbn","type":1,"indexed":true,"stored":true,"multiValued":false}
    ],"analysis":{}},
    "topology":{"shardCount":1,"replicaCount":0},
    "settings":{"refreshInterval":null,"commitInterval":null,"defaultField":"title","maximumQueryClauses":null}
  }'
```

Bulk writes accept `Memory` or `LocalFsync` independently of `refresh`. `Memory` acknowledges an accepted local writer operation; `LocalFsync` waits for a local durable commit. `refresh: true` makes committed data visible before the response. Successful writes return a versioned `writeToken`; send that token with `consistency: "ReadYourWrites"` when a search must wait for the write.

The complete alpha guide is in [`docs/server`](../../docs/server/index.md), including schema rules, cursor paging, REST/gRPC examples, Studio and persistence details.

## Package boundaries

| Project | Purpose |
| --- | --- |
| `Rowles.LeanCorpus.Server.Abstractions` | BCL-only contracts, interception ports and Community defaults |
| `Rowles.LeanCorpus.Server.Core` | Local registry, schema mapping, writes, search, explain and inspection |
| `Rowles.LeanCorpus.Server.AspNetCore` | Reusable REST endpoint and dependency-injection integration |
| `Rowles.LeanCorpus.Server.Grpc` | Typed gRPC transport adapter over the Core service interfaces |
| `Rowles.LeanCorpus.Server.Local` | Reference executable and composition root |
| `Rowles.LeanCorpus.Studio` | Minimal embeddable Community Studio workflows |

The Community implementation deliberately has no Enterprise or distributed-server dependency.

## Alpha limitations

Community `0.1.0-alpha.1` is one local shard with no replicas. Terms facets are the supported facet subset. Highlights, range facets, postings/terms/analysis inspection, cluster administration, `Replica` consistency, `Quorum` durability and `Replicated` durability return typed unsupported failures. Alpha contracts may change before the final `0.1.0` release.
