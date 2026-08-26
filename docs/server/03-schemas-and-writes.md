# Schemas and writes

Indexes are explicitly registered by logical name and stored under an opaque physical ID. Community requires one shard and zero replicas. The schema is authoritative: unknown document fields, reserved _id and _raw names, invalid values, unsupported analyser references, and vector dimension mismatches are rejected.

The schema field-type values are:

| Value | Type | Notes |
| ---: | --- | --- |
| 0 | Text | Analysed full-text field |
| 1 | Keyword | Exact string field |
| 2 | Int64 | Signed 64-bit integer |
| 3 | Double | Double-precision number |
| 4 | Boolean | Boolean value |
| 5 | DateTime | UTC date/time representation |
| 6 | Binary | Binary value |
| 7 | Vector | One fixed-size floating-point array |

Text fields may name a configured analyser. Arrays require multiValued: true, except Vector fields, which are one fixed-size array. _id is generated from the operation document ID and _raw retains source JSON for bounded projection and inspection.

## Create

Use PUT /v1/indices/books with indexName, schema, topology set to one shard and zero replicas, and initial mutable settings:

~~~json
{
  "indexName": "books",
  "schema": {
    "fields": [
      { "name": "title", "type": 0, "indexed": true, "stored": true, "multiValued": false, "analyser": "standard" },
      { "name": "isbn", "type": 1, "indexed": true, "stored": true, "multiValued": false }
    ],
    "analysis": {}
  },
  "topology": { "shardCount": 1, "replicaCount": 0 },
  "settings": {
    "refreshInterval": null,
    "commitInterval": null,
    "defaultField": "title",
    "maximumQueryClauses": null
  }
}
~~~

## Bulk writes, durability and refresh

POST /v1/indices/books/documents:bulk accepts operations in caller order:

~~~json
{
  "indexName": "books",
  "operations": [
    {
      "kind": 0,
      "documentId": "one",
      "document": { "title": "Practical search", "isbn": "978-1" }
    }
  ],
  "durability": "LocalFsync",
  "refresh": false,
  "idempotencyKey": "books-write-1"
}
~~~

Memory acknowledges accepted local writer operations without requiring a commit. LocalFsync waits for a local durable commit before acknowledgement. refresh is independent: true refreshes the committed reader before the response, while false leaves visibility to the configured refresh schedule or an explicit refresh request. Neither option means replication in Community.

Successful responses contain per-operation results and a version-one local writeToken with indexId, sequenceNumber, and optional commit/content tokens. Repeating the same idempotency key and request returns the original result without applying the batch twice. Reusing the key for a different request returns idempotency_conflict.

For a read that must include the write, send the token unchanged:

~~~json
{
  "query": { "kind": "term", "field": "isbn", "value": "978-1" },
  "consistency": "ReadYourWrites",
  "readToken": {
    "version": 1,
    "indexId": "<indexId from the write response>",
    "sequenceNumber": 1
  }
}
~~~

ReadYourWrites waits for the local sequence, refreshes visibility, and is bounded by the configured consistency wait. Local reads the current local reader and Primary names the sole Community copy. Replica, Quorum and Replicated return typed unsupported failures.
