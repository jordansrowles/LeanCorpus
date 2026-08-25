# REST and gRPC APIs

REST routes are under `/v1`:

- health and readiness;
- index list, create, delete, schema, statistics and settings;
- bulk documents and refresh;
- search and explain;
- bounded inspection (inventory, fields, segments, reader state, storage and document samples).

The versioned OpenAPI and protobuf artefacts are shipped from `Server.Abstractions/Contracts`. The Community OpenAPI document contains only routes registered by the Community host.

gRPC uses typed v1 protobuf messages mapped to the same Core services as REST. Search queries and schema-shaped values use protobuf `Struct` values at the transport boundary, while lifecycle, write, result, failure, health and inspection messages remain strongly typed. Deadlines and cancellation are passed to Core, and generated protobuf types never enter Core.
