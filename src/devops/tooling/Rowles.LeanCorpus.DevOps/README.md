# LeanCorpus DevOps

LeanCorpus DevOps is the command-line tool for building, testing, documenting,
validating and packaging a LeanCorpus checkout.

## Requirements

This package is a framework-dependent .NET tool targeting .NET 10. Install a
.NET 10 runtime before running it. The package includes the managed DevOps
application and its managed dependencies; it does not include the .NET SDK,
Git, repository-pinned tools, Native AOT toolchains or the LeanCorpus source.

## Install and run

After the package is available from a configured NuGet source, install it as a
global tool:

```bash
dotnet tool install --global Rowles.LeanCorpus.DevOps
devops --help
```

Run repository commands from a compatible LeanCorpus checkout. The tool
resolves repository-owned projects, configuration and generated artefacts from
that checkout. For local development, the repository's `./devops` apphost
remains the bootstrapped entry point.

Use `devops --repo /absolute/path/to/leancorpus --help` from outside a checkout;
repository commands still require a compatible checkout and its pinned SDK.
To remove a global tool installation, run:

```bash
dotnet tool uninstall --global Rowles.LeanCorpus.DevOps
```

For an archive installation, remove the directory you extracted. Check the
archive sidecar and payload `SHA256SUMS` before running a downloaded executable.
Archive creation is local; tool/feed publication and platform qualification are
separate operations.
