# Running the Community Server

From a checkout, run:

```bash
./devops server start
```

The reference host targets .NET 11, runs in the foreground, binds to `127.0.0.1:5080` and `[::1]:5080` by default, and stores indexes below `data/`. Press Ctrl+C to stop it. Set `LeanCorpus:DataRoot` or provide an alternate `appsettings.json` to change the data root.

For access from another machine on a trusted network, run `./devops server start -External`. This listens on all IPv4 interfaces at port 5080 and permits remote host headers. It does not add authentication or TLS, so do not expose it directly to the internet. Opening the firewall alone is insufficient while the process is bound only to loopback.

Use `./devops server start -- --urls http://127.0.0.1:5081` to pass application arguments through to the host.

Liveness is available at `/v1/health`; readiness is available at `/v1/ready`. Both return the normal response metadata envelope.
