# Browser catalogue

A small, single-threaded Blazor WebAssembly consumer of LeanCorpus. It indexes three titles, commits and searches locally. Reloading the page rebuilds the volatile index. CPU work runs on the browser thread.

Build through the managed entry point:

```bash
./devops build --project src/examples/e2e/Rowles.LeanCorpus.Example.BlazorWasm/Rowles.LeanCorpus.Example.BlazorWasm.csproj --framework net10.0
```

The DevOps CLI has no application-serving route. For local interactive use, run the standard Blazor development server:

```bash
dotnet run --project src/examples/e2e/Rowles.LeanCorpus.Example.BlazorWasm --framework net10.0 --configuration Release --no-launch-profile
```

Use `net11.0` for the other supported framework. Interpreted publishing can disable native relinking with `-p:WasmBuildNative=false`; WASM AOT requires the matching `wasm-tools` workload. See the canonical [browser guide](../../../../docs/content/getting-started/browser-wasm.md) for capabilities, lifetimes and deployment limits.
