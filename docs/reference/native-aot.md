# Native AOT and trimming

LeanCorpus and Rowles.Text are designed for Native AOT. Publish the application with its normal runtime identifier and validate the produced binary in your deployment environment.

Avoid runtime discovery patterns that depend on reflection. Register optional compression providers explicitly at startup. Keep generated mappings and serialisation metadata rooted by normal application code.

```bash
dotnet publish -c Release -r linux-x64 -p:PublishAot=true
```

See [Installation and first index](../getting-started/01-installation.md) and the Native AOT example project for current constraints.
