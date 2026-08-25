# Persistence and security

The registry is atomically persisted as version-one JSON. Startup validates its format, index names, opaque IDs, schema hashes and storage directories. Unknown names never create an index directory. Committed documents and registrations are reopened after a clean restart.

Community defaults are permissive for local development but authentication and authorisation are replaceable through DI. Destructive index deletion requires a server-side confirmation token. Request body, document, bulk, uncommitted-operation, query, inspection and idempotency retention limits are bounded by `ServerCoreOptions`.

Keep the default loopback listeners for local use. If external binding is intentional, put the server behind the host's authentication and network controls and review the warning emitted at startup.
