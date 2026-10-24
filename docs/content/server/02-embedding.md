# Embedding

Rowles.LeanCorpus.Server.Core is transport-neutral. Keep one LocalServerCore lifetime per host and replace routing, authentication, authorisation, entitlement, acknowledgement, lifecycle, audit, consistency and inspection policies through ServerPortSet.

The reusable ASP.NET Core composition is:

~~~
builder.Services
    .AddLeanCorpusServerCore(options => options.DataRoot = dataRoot)
    .AddLeanCorpusServerAspNetCore()
    .AddLeanCorpusStudio();
builder.Services.AddGrpc();

app.UseStaticFiles();
app.MapLeanCorpusServerEndpoints();
app.MapLeanCorpusServerGrpc();
app.MapLeanCorpusStudio();
~~~

AddLeanCorpusServerAspNetCore registers the Core service interfaces used by the REST adapter. MapLeanCorpusServerGrpc maps typed v1 protobuf services over those same interfaces. AddLeanCorpusStudio and MapLeanCorpusStudio expose the embeddable Studio at /studio.

The host owns the Core instance and its physical index handles. Do not pass IndexWriter, SearcherManager, directory or merge objects through the embedding boundary. Configure authentication and authorisation before using an external listener.
