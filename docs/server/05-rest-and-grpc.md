# REST and gRPC APIs

REST routes are under /v1:

- health and readiness;
- index list, create, delete, schema, statistics and settings;
- bulk documents and refresh;
- search and explain;
- bounded inspection for inventory, fields, reader state, segments, storage and document samples.

The normal REST response is an envelope containing isSuccess, metadata, and either value or failure. Responses include X-Request-ID and X-API-Version: 1.

For example:

~~~bash
curl -X POST http://127.0.0.1:5080/v1/indices/books/search -H 'Content-Type: application/json' -d '{"query":{"kind":"term","field":"isbn","value":"978-1"},"size":10,"consistency":"Local"}'
~~~

Durability and consistency use their string contract values in REST JSON: Memory, LocalFsync, Quorum, Replicated, Local, Primary, Replica and ReadYourWrites. The token fields are version, indexId, sequenceNumber, commitGeneration and contentToken. Unsupported Community values return stable failures such as durability_not_supported or consistency_unavailable, with no write or read side effect.

The typed gRPC v1 services map to the same Core interfaces:

~~~csharp
GrpcContracts.SearchService.SearchServiceClient search = new(channel);
GrpcContracts.SearchResponse response = await search.SearchAsync(
    new GrpcContracts.SearchRequest
    {
        IndexName = "books",
        Query = Struct.Parser.ParseJson(
            """{"kind":"term","field":"isbn","value":"978-1"}"""),
        Consistency = "Local"
    });
~~~

Lifecycle, bulk, result, failure, health and inspection messages are typed protobuf messages. Query and schema-shaped values use protobuf Struct at the transport boundary. The gRPC adapter maps the same durability, token, consistency and typed unsupported failures as REST, and passes cancellation and deadlines into Core.

The source-controlled artefacts are Server.Abstractions/Contracts/OpenApi/lean-corpus-server.community.v1.openapi.json and Contracts/Grpc/lean-corpus-server.v1.proto. The Community OpenAPI document contains only Community routes; Enterprise routes are not registered by the local host.
