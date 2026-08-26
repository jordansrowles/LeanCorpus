# Running the Community Server

Community Server 0.1.0-alpha.1 targets .NET 11 and runs the reference host in the foreground:

~~~
./devops server start
~~~

The default listeners are http://127.0.0.1:5080 and http://[::1]:5080. Indexes are stored below data/ in the host content root. Press Ctrl+C to stop the process. Set LeanCorpus:DataRoot in configuration, or pass an application argument, to select another data root.

For a trusted network only:

~~~
./devops server start -External
~~~

This binds http://0.0.0.0:5080 and permits remote host headers. It does not add authentication or TLS. Put it behind the host's network and authentication controls before allowing untrusted clients.

Arguments after -- are passed to the reference application. For example:

~~~
./devops server start -- --urls http://127.0.0.1:5081
~~~

The reference host also accepts LeanCorpus:Listeners and the bounded LeanCorpus:Maximum* settings from appsettings.json.

## Health and readiness

Liveness is GET /v1/health; readiness is GET /v1/ready. Both return the normal metadata envelope and include request ID and API version headers.

Health reports healthy, degraded, unhealthy or draining. It includes each index's mode, visible and durable generations, pending operations, commit failures and installation state. A failed commit or installation that leaves the last committed reader usable is degraded. An unrecoverable rollback is unhealthy.

Readiness means that startup and the registry completed, the process is not stopping, and registered indexes still have usable committed runtimes. A degraded index remains ready because committed reads remain available, although writes may be unavailable. An unusable index or a draining/stopped process is not ready.

## Create an index

Create a logical index with PUT /v1/indices/{name}. The request schema is authoritative and the physical directory uses an opaque ID. The schemas and writes page contains the full example.
