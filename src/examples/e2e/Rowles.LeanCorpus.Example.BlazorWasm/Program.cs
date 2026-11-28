using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Rowles.LeanCorpus.Example.BlazorWasm;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
await builder.Build().RunAsync();
