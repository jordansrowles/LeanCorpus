# Orchard Core search provider

`Rowles.LeanCorpus.OrchardCore.Search` adds an embedded LeanCorpus indexing provider, Orchard's generic search service, and a native LeanCorpus query source. It stores each tenant's indexes locally and does not call an external service.

The package is an optional Orchard Core module. It targets .NET 10 and .NET 11 and is built against Orchard Core 3.0.1. Building from source requires the .NET 11 SDK and preview C# compiler. The provider is released as `0.1.0-alpha.1`.

The module uses Orchard's Indexing, Search, and Queries packages and depends on `Rowles.LeanCorpus` Core. Enabling the feature is optional; it does not replace or alter Orchard's Lucene provider. The Core change needed by the provider is the additive public `IndexWriter.UpdateDocuments` batch API, which applies deletes before replacements in an Orchard document batch.

## Install and enable

Add the package to an Orchard Core application and enable the feature with ID `Rowles.LeanCorpus.OrchardCore.Search` (display name **LeanCorpus Search Provider**). The module registers a content indexing source named `LeanCorpus`, an `ISearchService` named `LeanCorpus`, and an Orchard query source named `LeanCorpus`.

In the Orchard admin, create a **Content** index profile and choose **LeanCorpus** as its provider. Select the content types to index and configure their field indexing settings. Build the profile after creating or changing its indexed content types and fields.

Orchard **Reset** clears indexed documents and rewinds the task cursor so Orchard can replay content into the existing physical index. **Rebuild** replaces the physical index, clears its schema and cursor state, and lets Orchard repopulate it. Rebuild after changing an indexed field contract or the default analyser; reset is for replaying content against the current schema.

The provider maps Orchard entries as follows:

| Orchard entry | LeanCorpus representation | Notes |
| --- | --- | --- |
| Text | Analysed text or exact keyword | Orchard's `Keyword` option selects exact terms. HTML content is rendered to text. |
| Boolean | Boolean keyword | Supports exact term and range queries. |
| Integer | Signed 64-bit integer | Supports exact and range queries. |
| Number | Double | Non-finite values are rejected. |
| Date/time | UTC ticks | Supports exact and range queries. Unspecified `DateTime` values are treated as UTC. |
| Geo point | Indexed geo point | Coordinates are range-checked and available to native `geoDistance` queries. |
| Vector | Stored vector with fixed dimensions | Available to native `vectorKnn` queries; dimensions and finite values are checked. |
| Complex | Unsupported schema entry | Recorded in the schema but not indexed. |

Fields can be multi-valued. Null values use companion marker fields so native `exists` and `isNull` queries can distinguish null from missing. A field's type and indexing representation must remain consistent within an index; incompatible later values fail indexing rather than changing the persisted schema. Changing the default analyser after indexing requires a physical profile rebuild.

Unpublishing removes indexed content through Orchard's ordinary indexing-task flow. Hard removal through `IContentManager.RemoveAsync` removes indexed content through Orchard's deferred content-index handler. Orchard Core 3.0.1 also queues a `Delete` indexing task for hard removal, but its background `ContentIndexingService` does not build a `BuildDocumentIndexContext` for that task. Hard-delete propagation therefore relies on Orchard's public content-removal lifecycle; direct task insertion is not a substitute for `IContentManager.RemoveAsync`.

Index data, schema manifests, and indexing cursors are kept below:

```text
<content-root>/App_Data/Sites/<tenant-key>/LeanCorpus/
    indexes/<index-key>/
    schemas/<index-key>.json
    state/<index-key>.json
```

Tenant and index keys are derived from the Orchard names using a readable prefix and hash, so raw names are not used as filesystem paths. Keep this tenant data with the rest of Orchard's persistent `App_Data` when backing up or moving a site.

Replacement shells belonging to the same root host and tenant directory share
one physical runtime per index. During ordinary shell turnover, the last cache
owner closes that runtime. Application shutdown closes admission across
historical and replacement shells, drains admitted operations and search leases,
then waits for physical close. A retained historical shell scope cannot reopen
an index after host shutdown. An independent root host in the same process cannot
attach to an already active writable tenant path; the provider returns a clear
ownership error. After the original root closes successfully, a new root can
open the path with a new runtime. Other tenants and different paths have
independent lifetimes. Close failures remain visible in ownership diagnostics
and prevent a new owner from treating the path as available.

The provider uses the same internal physical owner as Community Server. The
owner controls the directory, writer and searcher generations; Orchard controls
tenant paths, schema, task cursors and visibility after indexing. A task cursor
advances only after both commit and the required refresh succeed.

Each active provider instance assumes exclusive ownership of its tenant's index and cursor files. Do not point multiple active Orchard processes at the same `App_Data` directory. The provider has no cross-process writer lease or distributed cursor coordination. A multi-node deployment needs independent per-node indexes and an explicit rebuild/replay plan after failover; otherwise use a search service that provides shared distributed ownership.

## Configure generic site search

Edit the LeanCorpus index profile's provider settings:

- **Default analyser** is `standard` by default. `keyword` is also available.
- **Site-search fields** selects which indexed analysed-text fields participate in Orchard's generic search. Leaving the selection empty searches every indexed analysed-text field.

Changing the analyser after fields have been indexed requires a physical profile rebuild. Search fields must refer to fields present in the index schema and represented as analysed text. Keyword text fields remain available to native term queries, but are not part of generic analysed-text search.

Orchard's generic search service returns content item IDs, ordered by descending score with a stable ID tie-break. It caps each page at 100 results. Search text is limited to 4,096 characters and at most 64 distinct analysed tokens. The provider does not currently return highlights.

## Create a native LeanCorpus query

Create an Orchard query and select **LeanCorpus** as its source. Choose a LeanCorpus index profile and enter a bounded native query object. The editor validates the query against that index's persisted schema and adds the selected index name when it saves the query.

This query source uses LeanCorpus's native JSON format; it does not parse Orchard Lucene query syntax or provide a drop-in Lucene query migration. Switching providers does not migrate an existing Lucene profile or index: create a LeanCorpus profile and build it from Orchard content. Scores and result order differ, Lucene's tested DateTime lower-bound behaviour differs, and Lucene omits the explicit null field used by the LeanCorpus null marker.

For example, this matches analysed text in one field:

```json
{
  "query": {
    "type": "match",
    "field": "Article.Title",
    "value": "orchard search"
  },
  "skip": 0,
  "take": 20
}
```

Supported clause types are `match`, `term`, `range`, `exists`, `isNull`, `and`, `or`, `not`, `geoDistance`, and `vectorKnn`. Field names and supported operations are checked against the index schema. Boolean clauses accept 1 to 32 child clauses. Queries are limited to 16,384 JSON characters, nesting depth 16, and a `take` value of at most 100; `skip` is limited to 1,000,000. Vector queries must match the indexed dimensions, and their `topK` is limited to 100.

## Functional and performance evidence

The paired provider run used Orchard Core 3.0.1 on Debian GNU/Linux 13 with .NET 11 preview 7, an Intel Xeon E3-1220 V2 at 3.10 GHz, and 2,000 published content items. It compared the Orchard Lucene and LeanCorpus profiles in one host. Search and single-update measurements used one cold sample, three warm-ups, and ten measured samples per provider.

| Operation | Lucene | LeanCorpus |
| --- | ---: | ---: |
| Term search median / p95 | 3.20 / 11.09 ms | 54.97 / 131.52 ms |
| Multi-term search median / p95 | 5.56 / 5.60 ms | 38.88 / 45.01 ms |
| Single-document update plus task processing, median / p95 | 25.48 / 37.64 ms | 112.96 / 187.28 ms |
| Initial build, median of three | 1,457.46 ms | 2,438.29 ms |
| Rebuild, median of three | 853.95 ms | 1,529.74 ms |
| Index reopen after host restart | 0.012 ms | 174.345 ms |

Search timing is measured inside `ISearchService.SearchAsync` and excludes loopback transport. Update timing sums Orchard content mutation and the target provider's indexing-task processing; a separate search checks visibility but is not included in the duration. Both providers receive each content task independently.

Builds and rebuilds replay the same 2,000 Orchard indexing tasks through each provider's regular task-processing and content-field refresh path. Each provider had three initial-build runs and three rebuild runs. Three 100-item mutation and delete batches also passed, as did cursor-drain and host-restart/index-open checks. The raw report contains sample values, allocations, working set, index sizes, and startup timings. Operating-system caches were not flushed. These are results from one local run and are not a cross-machine performance guarantee.

### Recommendation

Keep the provider opt-in and experimental. It passes Orchard lifecycle, task replay, cursor recovery, field mapping, native geo/vector query, and restart checks, and it supplies local embedded storage without a remote search service. In this 2,000-item run it was slower than Lucene for both search queries, single-item updates, initial build, rebuild, and index reopen. It is therefore not yet a recommended default or a Lucene replacement for search-heavy sites. Consider it where local storage or native LeanCorpus capabilities matter and the measured workload fits; qualify it with a representative production corpus and deployment topology before adoption.

The complete measurement record, including sample data and host provenance, is in [the raw run report](evidence/orchard-core-search-provider-runs.json). Run the phases in order; each command is a separate DevOps invocation and keeps the configured 100 second hang deadline:

```bash
./devops test -Suite server-orchard-performance -Framework net11.0 -Filter 'FullyQualifiedName~Phase00_SeedSharedOrchardCorpus' -ExplicitOnly -HangTimeout 100s
./devops test -Suite server-orchard-performance -Framework net11.0 -Filter 'FullyQualifiedName~Phase10_BuildAndRebuildLucene' -ExplicitOnly -HangTimeout 100s
./devops test -Suite server-orchard-performance -Framework net11.0 -Filter 'FullyQualifiedName~Phase20_BuildAndRebuildLeanCorpus' -ExplicitOnly -HangTimeout 100s
./devops test -Suite server-orchard-performance -Framework net11.0 -Filter 'FullyQualifiedName~Phase30_SearchAndSingleDocumentUpdates' -ExplicitOnly -HangTimeout 100s
./devops test -Suite server-orchard-performance -Framework net11.0 -Filter 'FullyQualifiedName~Phase40_BatchMutationsAndIndexRestart' -ExplicitOnly -HangTimeout 100s
```

The functional browser test installs a test Orchard site with paired Lucene and LeanCorpus profiles, then checks search results through publish, update, unpublish, hard removal, reset, and index-queue processing. The provider unit and integration suite covers schema mapping, persistence, replacement, cursor safety, and failure recovery.
