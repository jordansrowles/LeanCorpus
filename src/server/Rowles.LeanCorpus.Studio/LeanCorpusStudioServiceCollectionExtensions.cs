using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Rowles.LeanCorpus.Studio;

/// <summary>Registers the embeddable Community Studio components.</summary>
public static class LeanCorpusStudioServiceCollectionExtensions
{
    /// <summary>Registers Studio services without creating an HTTP client or owning host routing.</summary>
    public static IServiceCollection AddLeanCorpusStudio(this IServiceCollection services) => services;

    /// <summary>Maps the embeddable Community Studio at <c>/studio</c>.</summary>
    public static IEndpointRouteBuilder MapLeanCorpusStudio(this IEndpointRouteBuilder endpoints)
    {
        static IResult Page() => Results.Content(StudioPage, "text/html; charset=utf-8");
        endpoints.MapGet("/studio", Page);
        endpoints.MapGet("/studio/assets/studio.css", () => Asset("studio.css", "text/css; charset=utf-8"));
        endpoints.MapGet("/studio/assets/studio.js", () => Asset("studio.js", "text/javascript; charset=utf-8"));
        return endpoints;
    }

    private static IResult Asset(string name, string contentType)
    {
        string resource = $"Rowles.LeanCorpus.Studio.wwwroot.{name}";
        Stream stream = typeof(LeanCorpusStudioServiceCollectionExtensions).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Studio resource '{resource}' is missing.");
        return Results.Stream(stream, contentType);
    }

    private const string StudioPage = """
        <!doctype html>
        <html lang="en-GB">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>LeanCorpus Studio</title>
          <link rel="stylesheet" href="/studio/assets/studio.css">
          <script src="/studio/assets/studio.js" defer></script>
        </head>
        <body>
          <header><strong>LeanCorpus Studio</strong><span>Community Server 0.1.0-alpha</span></header>
          <div class="shell">
            <nav aria-label="Studio navigation">
              <button data-view="overview">Overview</button>
              <button data-view="indices">Indices</button>
              <div id="indexNavigation" hidden>
                <strong id="selectedIndexName"></strong>
                <button data-view="index-overview">Overview</button>
                <button data-view="schema">Schema</button>
                <button data-view="documents">Documents</button>
                <button data-view="segments">Segments</button>
                <button data-view="query">Query</button>
                <button data-view="settings">Settings</button>
              </div>
            </nav>
            <main>
              <div id="notice" role="status" aria-live="polite"></div>
              <section id="overview" class="view"><h1>Server overview</h1><div id="serverOverview" class="cards"></div></section>
              <section id="indices" class="view" hidden>
                <h1>Indices</h1>
                <form id="createIndexForm">
                  <label>Index name<input id="createIndexName" required pattern="[A-Za-z0-9_-]+"></label>
                  <label>Schema<textarea id="createIndexSchema" rows="12" spellcheck="false"></textarea></label>
                  <button type="submit">Create index</button>
                </form>
                <div id="indexList"></div>
              </section>
              <section id="index-overview" class="view" hidden><h1>Index overview</h1><pre id="indexOverview"></pre><button id="deleteIndex" class="danger">Delete index</button></section>
              <section id="schema" class="view" hidden><h1>Schema</h1><pre id="schemaOutput"></pre></section>
              <section id="documents" class="view" hidden>
                <h1>Documents</h1>
                <form id="indexDocumentForm"><label>Document ID<input id="documentId" required></label><label>Document JSON<textarea id="documentJson" rows="8" spellcheck="false">{}</textarea></label><button type="submit">Index document</button></form>
                <pre id="documentsOutput"></pre>
              </section>
              <section id="segments" class="view" hidden><h1>Segments</h1><pre id="segmentsOutput"></pre></section>
              <section id="query" class="view" hidden>
                <h1>Query test bench</h1>
                <label>Query JSON<textarea id="queryJson" rows="9" spellcheck="false">{"kind":"queryString","query":"test"}</textarea></label>
                <label>Document ID for explain<input id="explainDocumentId"></label>
                <p><button id="runSearch">Search</button> <button id="runExplain">Explain</button></p>
                <pre id="queryOutput"></pre>
              </section>
              <section id="settings" class="view" hidden>
                <h1>Settings</h1>
                <form id="settingsForm"><label>Mutable settings JSON<textarea id="settingsJson" rows="9" spellcheck="false"></textarea></label><button type="submit">Save settings</button></form>
              </section>
            </main>
          </div>
        </body>
        </html>
        """;
}
