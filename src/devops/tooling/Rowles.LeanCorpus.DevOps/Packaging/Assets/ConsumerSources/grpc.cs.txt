using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Rowles.LeanCorpus.Server.Grpc;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddGrpc();
var app = builder.Build();
app.MapLeanCorpusServerGrpc();
await app.StartAsync();
await app.StopAsync();
await app.DisposeAsync();
Console.WriteLine("SERVER GRPC integration mapped and started successfully.");