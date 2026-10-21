# Running the Community Server

Community Server 0.1.0-alpha.1 targets .NET 10 and .NET 11. Building from source requires the .NET 11 SDK and preview C# compiler. The reference host runs in the foreground:

~~~
./devops server start
~~~

Select .NET 10 with `./devops server start -Framework net10.0`; the default is `net11.0`.

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

## Shutdown

Shutdown rejects new operations and waits for admitted requests before closing
physical indexes. If `ShutdownTimeout` expires, disposal throws a timeout and
retains the physical owners in the draining state. Finish or cancel the admitted
requests, then retry disposal. A timeout does not mean the index files are free
to rename or remove.

Search leases and committed snapshot leases must be disposed before their
physical runtime can close. Executing commit-timer callbacks are joined, and
remaining durability waiters fail when their coordinator closes. A failed close
is reported to the caller rather than treated as successful shutdown. Store
shutdown attempts every physical owner even when one close fails; repeated
callers observe the same close result, and a closed store rejects new owners.

See [physical index lifetimes](../articles/physical-index-lifetimes.md) for the
shared Core ownership contract.
