# Schemas and writes

Indexes are explicitly registered by logical name and are stored under an opaque physical ID. Community Server requires one shard and no replicas. The schema is authoritative: unknown document fields, reserved `_id` and `_raw` names, invalid values, unsupported analyser references and vector dimension mismatches are rejected.

Supported fields are Text, Keyword, Int64, Double, Boolean, DateTime, Binary and Vector. Arrays require `MultiValued`, except vectors which are one fixed-size array. `_id` is generated from the document operation ID and `_raw` retains the source JSON for bounded projection and inspection.

Bulk writes return per-operation results. `Refresh = true` commits and refreshes before the response; ordinary writes are committed by the configured interval. Idempotency keys are bounded per physical index and replay the original result for an identical request.
