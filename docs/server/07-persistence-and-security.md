# Persistence and security

The configured data root contains:

~~~text
<data-root>/
  registry.json
  indices/
    <opaque physical GUID>/
      LeanCorpus engine files
~~~

registry.json is version-one JSON containing logical names, opaque physical IDs, schema, schema hash, topology and mutable settings. Registry writes use a temporary file and atomic replacement. Startup validates names, IDs, schema hashes, topology and the expected physical directories before opening an index. Temporary install directories are recovered or removed according to their publication state.

Committed engine files and registrations are reopened after a clean stop and restart. A logical name never becomes an implicit directory name, and an unknown name does not create storage.

Community defaults are permissive for local development. Replace authentication and authorisation through dependency injection before external exposure. Keep the default loopback listeners for local use. If external binding is intentional, put the process behind authentication, TLS and network controls.

Destructive index deletion requires a server-side confirmation token. Request bodies, documents, bulk operations, queries, inspection output and retained idempotency entries are bounded by ServerCoreOptions.
