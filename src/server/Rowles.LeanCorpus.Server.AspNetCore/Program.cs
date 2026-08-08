using Rowles.LeanCorpus.Server.AspNetCore;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = builder.Configuration.GetValue<long?>("LeanCorpus:MaximumRequestBodyBytes") ?? 104_857_600);
ServerCoreOptions options = new()
{
    DataRoot = builder.Configuration["LeanCorpus:DataRoot"] ?? Path.Combine(builder.Environment.ContentRootPath, "data")
};

builder.Services.AddSingleton(LocalServerCore.OpenAsync(options).AsTask().GetAwaiter().GetResult());

WebApplication application = builder.Build();
application.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Api-Version", "1");
    await next(context);
});
application.MapLeanCorpusServerEndpoints();
application.Run();
