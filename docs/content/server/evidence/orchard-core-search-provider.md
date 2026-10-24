# Orchard Core LeanCorpus Provider Evidence

## Environment

The paired performance run used the local Debian GNU/Linux 13 (trixie) x64 host, an Intel Xeon E3-1220 V2 at 3.10 GHz, four reported processors, .NET 11.0.0-preview.7.26381.103, and SDK 11.0.100-preview.7.26381.103. It used 2,000 deterministic published `SearchTestArticle` items generated with seed 3901. The content included short titles, multi-paragraph bodies, categories, keywords, dates, integers, floating values, and Unicode.

The run completed on 2026-10-09 at 03:18:35 UTC at commit `0a0fac1a713464a546e7f595e72f1894a7bc1a4c` on branch `alpha/sever-orchard`, with the intended implementation changes in the dirty worktree. All five explicit performance phases passed. Exact samples and captured status are in [the raw performance record](orchard-core-search-provider-runs.json).

Final post-cleanup validation ran on 2026-10-09 from commit `78d8ffa3343c1766574501c6431c6c5fb62f328c`, before this evidence entry was refreshed. `./devops build` passed across the solution with 0 errors and 486 warnings, including NuGet audit warnings because the configured sources were unreachable. `./devops test -Suite server-orchard` passed 29/29 tests. `./devops test -Suite affected` passed both target executions: 4/4 functional tests passed and the five explicit performance phases were skipped by default. The architecture suite passed 54/54 on both net10.0 and net11.0. The Core Index suite passed 968/968 on both net10.0 and net11.0. `./devops docs build -SkipBenchmarks -SkipCoverage` completed with 44 metadata warnings and 3,997 site warnings. The evidence refresh changed no implementation or test sources; the docs workflow was rerun against the refreshed document.

## Orchard version

Orchard Core `3.0.1`; the test tenant uses SQLite and the checked-in `LeanCorpusSearchTest` recipe.

## LeanCorpus version / commit

The provider package is `Rowles.LeanCorpus.OrchardCore.Search` `0.1.0-alpha.1`, targeting `net11.0`. It references the Core project at version `4.0.0`. The paired run used the baseline commit above plus the intended dirty worktree changes.

## Provider registration

The installed tenant exposes `LeanCorpus` as a distinct indexing provider alongside `Lucene`. The admin Indexing page showed both `Search-LeanCorpus` and `Search-Lucene`; the recipe created both profiles over `SearchTestArticle` and `VectorProbeArticle`. The production provider registers the index manager, document index manager, search service, stored-query source, and index-profile editor through its Orchard module startup.

## Field mapping results

The Orchard task pipeline indexed and searched the deterministic article corpus. Mapper tests checked the concrete LeanCorpus field classes, persisted schema values, UTC conversion, reserved names, schema conflicts, null markers, multiple values, and unsupported Complex values. Query integration tests ran integer, number, date, geo, vector, keyword, null, and stored-field queries against real temporary LeanCorpus indexes.

The compatibility table records the mapping contract and the observed provider-level query behavior. `Parity` means the tested mapping/query result agrees with the stated field semantics. It does not imply matching Lucene scores, ordering, or every Lucene query dialect. Pairwise Lucene result-set comparisons are listed separately below.

| Field type | Orchard representation | LeanCorpus representation | Indexing result | Search/query result | Lucene behaviour | LeanCorpus behaviour | Parity status | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Text | Orchard `TextField` emitted by the content field handler | Analysed `TextField`, configured analyser defaults to Standard | Indexed by the real task pipeline; direct mapper test verified type and analyser metadata | Generic, Unicode (`東京`), and punctuation (`co-op`) searches returned the expected content IDs in both profiles | Standard-analyser free-text query through `ISearchService` | Standard-analyser free-text query through `ISearchService` | Parity | Tokenisation edge cases beyond the tested corpus can differ. |
| Text + Keyword | Orchard test index handler emits repeated keyword text entries | `StringField`, exact keyword representation | The same Orchard content task emitted the field to both provider profiles | Paired exact term queries returned matching IDs for one member and for a shared member | Repeated keyword fields match the exact requested member | Native `term` query matches any exact member value | Parity | Pairwise keyword equality and multiple values passed through the real Orchard task pipeline. |
| Text + Store | Orchard test index handler emits a stored keyword text entry | Stored `StringField` | Both providers persisted the same stored value from the Orchard task | Paired query-source results returned the expected stored value in both providers | Stored value was returned by Lucene query results | Stored value was returned by LeanCorpus query results | Parity | The stored-field comparison uses an explicit stored test field; the recipe keeps `StoreSourceData=false`. |
| Boolean | Orchard `BooleanField` | `StringField` with `true` or `false` keyword values | Real article profile indexed the field; mapper test verified conversion | Paired exact term query returned the same two content IDs in both providers | Lucene indexes lowercase boolean tokens | LeanCorpus indexes canonical lowercase boolean tokens | Parity | Mapper and paired Orchard predicate passed. |
| Integer | Orchard `NumericField` with scale 0 | `Int64Field` | Real article profile indexed deterministic integer values | Paired inclusive numeric range returned the same two content IDs | Lucene numeric range query | LeanCorpus `range` query over `int64` points | Parity | Mapper, provider range integration, and paired Orchard range passed. |
| Number | Orchard `NumericField` with decimal scale | finite `double` `NumericField` | Real article profile indexed deterministic floating values | Paired numeric range returned the same three content IDs | Lucene numeric range query | LeanCorpus `range` query over double points | Parity | Non-finite values are rejected during mapping; paired Orchard range passed. |
| DateTime | Orchard `DateTimeField` | UTC ticks in an `Int64Field` | Real article profile indexed deterministic ISO timestamps | Paired range returned the same two interior content IDs | Orchard Lucene stores DateTools date strings and its tested lower-bound behavior excluded an item exactly at `gte` | LeanCorpus parses ISO instants and compares inclusive UTC tick bounds | Intentional difference | Lucene and LeanCorpus require different query encodings; the differential used a one-day wider Lucene range to compare the same interior records. Exact endpoint parity is not claimed. |
| GeoPoint | Orchard `GeoPointField` | `GeoPointField` with validated latitude and longitude | Orchard profile contains the field; direct mapper/query integration used a real LeanCorpus index | Native `geoDistance` query returned only the in-radius document | Orchard Lucene uses its own geo filter/query representation | LeanCorpus exposes a native `geoDistance` clause | Intentional difference | Coordinates and provider-specific query syntax are not asserted to be interchangeable. |
| Vector | Test-only Orchard part index handler emits a deterministic three-dimensional vector | Persisted `VectorField` with manifest dimensions | Real Orchard task processing persisted the vector and dimensions | Native `vectorKnn` returned the expected nearest content item | The embedded Lucene profile does not receive an equivalent vector representation | LeanCorpus native KNN query returned the expected item | Intentional difference | The end-to-end Orchard vector probe passed; generic site search does not expose vector queries. |
| Complex | Orchard `Complex` entry | Explicit unsupported schema entry; no indexed field | Mapper records the field as unsupported and emits no physical field | No search predicate is generated | Lucene may index nested values through its own field handlers | LeanCorpus omits the Complex value | Unsupported | This is an explicit policy, not an accidental omission. |
| null | Orchard null content field | Hidden `field.__lc_null` companion marker when Orchard emits a null entry | Orchard omitted the explicit null field from Lucene; LeanCorpus persisted its null marker | Paired probes observed no Lucene match and one LeanCorpus `isNull` match | Null field is absent from the Lucene query result | LeanCorpus distinguishes the explicit null marker and returns the item | Intentional difference | Direct mapping also verified null marker persistence; the real Orchard differential established the observable difference. |
| multi-valued | Repeated keyword entries emitted by a shared Orchard test handler | Repeated same-name `StringField`s and a schema `MultiValued` flag | Both profiles received the same repeated values through the Orchard task pipeline | Paired exact term queries returned matching IDs for a unique member and a shared member | Lucene matches any repeated keyword value | LeanCorpus matches any repeated keyword value | Parity | Pairwise multi-value equality passed. |

## Lifecycle results

The browser test installed the recipe through Orchard setup, verified the distinct provider in the admin UI, created and rebuilt both profiles, published two items, and compared their result IDs through the real Orchard `ISearchService` path. It then reset and rebuilt the LeanCorpus index, updated one item so the old term disappeared and the new term appeared, unpublished that item, and hard-removed the companion with `IContentManager.RemoveAsync`; each removal made the content disappear from both providers. The test also checked two-page LeanCorpus paging and rendered search results through Orchard's search route. Recovery tests restarted the real test host and found its persisted tenant index and cursor again.

The lifecycle test used Orchard content operations and indexing tasks. Direct `AddOrUpdateDocumentsAsync` calls are limited to provider-focused integration tests.

## Generic site-search results

Orchard's generic Search module returned the expected content-item IDs through both providers for the shared text terms before and after update, unpublish, hard removal, reset, and rebuild. Result IDs are Orchard content-item IDs. A two-item LeanCorpus search returned distinct IDs on adjacent pages and reported the same total count on both pages.

## Lucene differential results

The browser differential compared indexed IDs and generic free-text result sets for initial population, update propagation, unpublish/delete propagation, reset, rebuild, Unicode and punctuation terms, and two-page results. It compared equivalent Orchard query-source IDs for a keyword term, repeated keyword values, stored-field values, explicit null, Boolean term, integer range, number range, and DateTime range. Both providers returned the same four IDs across two pages; LeanCorpus reported total count 4, while Orchard Lucene's result API did not report an exact count. The paired performance run also verified the complete 2,000-item build ID set for Lucene and the exact LeanCorpus result count plus a returned page.

The differential documents matching and intentionally different observable behaviour. Null differs because Orchard omits its null field while LeanCorpus stores a companion marker. Lucene excluded a DateTime document exactly on the lower range endpoint, so the paired probe widened the Lucene bound to compare the same interior values. Geo is validated through the LeanCorpus native query only because the two providers expose different geo query representations. No score or general ranking parity is claimed.

## Intentional behavioural differences

- Result ordering and scores are not required to match Lucene. LeanCorpus uses its own lexical ranking and a deterministic document-ID tie-break where supported.
- Vector KNN is available through the native LeanCorpus query source only; the Lucene profile receives no equivalent vector.
- Geo queries use each provider's own query representation.
- Null values use a LeanCorpus companion marker, so null and missing-field semantics are not claimed to match Lucene.
- Orchard Lucene's tested DateTime range query excluded the item at its exact lower bound. The paired probe widened the Lucene bound to compare the same interior dates; endpoint inclusivity parity is not claimed.
- Orchard omitted the explicitly null content field from Lucene, while LeanCorpus retained an explicit null marker and matched it with `isNull`.
- Complex fields are explicitly unsupported and are recorded in schema without a physical indexed field.
- Orchard Lucene 3.0.1's search result API does not report the same exact total count as this provider; performance assertions use returned IDs and the LeanCorpus count separately.

## Cursor/recovery results

All R1-R8 fault probes passed their invariant. Cursor values below are Orchard task IDs observed by the browser test. `cursor after restart` is the durable published cursor; `cursor after replay` is the final cursor after resuming pending work.

| Probe | Failure point | Cursor before | Cursor after restart | Document/index state | Replay result | Final state | Invariant passed |
| --- | --- | ---: | ---: | --- | --- | --- | --- |
| R1 | `BeforeDocumentWrite` | 0 | 0 | No document was committed | Replayed task and indexed the document | Cursor 1; document searchable | Yes |
| R2 | `AfterDocumentWriteBeforeCommit` | 1 | 1 | Uncommitted document absent | Replayed task and indexed the document | Cursor 2; document searchable | Yes |
| R3 | `AfterCommitBeforeCursor` | 2 | 2 | Committed document searchable; cursor remained old | Replayed replacement idempotently | Cursor 3; document searchable once | Yes |
| R4 | `DuringCursorWrite` | 3 | 3 | Committed document searchable; old cursor file remained published | Replayed replacement idempotently | Cursor 4; document searchable once | Yes |
| R5 | `AfterCursorFlushBeforePublish` | 4 | 4 | Committed document searchable; flushed temporary cursor was not published | Replayed replacement idempotently | Cursor 5; document searchable once | Yes |
| R6 | `AfterCursorPublish` | 5 | 6 | Committed document searchable; new cursor was already published | No pending task remained | Cursor 6; document searchable once | Yes |
| R7 | `AfterDeleteCommitBeforeCursor` | 7 | 7 | Deleted document was absent; cursor remained old | Replayed delete task idempotently | Cursor 8; document absent | Yes |
| R8 | `AfterSchemaPublishBeforeCommit` | 8 | 8 | Schema manifest included the vector field; document was not committed | Replayed task and indexed the vector document | Cursor 9; document returned by KNN | Yes |

R7 uses the test host's explicit delete-task consumer to exercise the provider's durable delete and cursor boundary. The browser lifecycle separately proves that Orchard's indexing-task flow removes documents on unpublish and its deferred content-index handler removes documents through `IContentManager.RemoveAsync`.

## Failure-injection matrix

| Failure point | Expected durable state | Observed restart/replay result |
| --- | --- | --- |
| `BeforeDocumentWrite` | No new document, cursor unchanged | Replayed and indexed once |
| `AfterDocumentWriteBeforeCommit` | No committed new document, cursor unchanged | Replayed and indexed once |
| `AfterCommitBeforeCursor` | New document committed, cursor unchanged | Duplicate replacement replay was harmless |
| `DuringCursorWrite` | New document committed, previous cursor remains visible | Cursor write retried through task replay |
| `AfterCursorFlushBeforePublish` | New document committed, previous cursor remains visible | Unpublished temporary cursor did not advance recovery state |
| `AfterCursorPublish` | New document and cursor both durable | Restart found no pending work |
| `AfterDeleteCommitBeforeCursor` | Target absent, previous cursor remains visible | Replayed delete and advanced cursor |
| `AfterSchemaPublishBeforeCommit` | New schema visible, target document absent, previous cursor visible | Replay completed the document write and advanced cursor |

## Vector capability result

The real Orchard test module emitted a deterministic three-dimensional vector through an `IContentPartIndexHandler`. Orchard task processing stored it in the LeanCorpus index manifest and document. A native `vectorKnn` query returned the expected content-item ID. This capability is provider-specific and is not exposed through generic site search or compared with Lucene.

## Geo capability result

LeanCorpus accepts Orchard GeoPoint values after checking latitude and longitude bounds, stores them as `GeoPointField`, and passed a native `geoDistance` query against a real temporary LeanCorpus index. The test returned the in-radius item and excluded the distant item. This validates the LeanCorpus geo path; the field-specific Lucene result set has not been compared.

## Dependency audit

The production project references Orchard Indexing, Queries, Search, and the LeanCorpus Core project. It has no `Lucene.Net`, `OrchardCore.Lucene`, or `Rowles.LeanCorpus.Server.*` package reference. Lucene is used by the Orchard test host, test module dependencies, and test projects only. LeanCorpus Core has no Orchard dependency.

The provider batch writer required a public Core API addition, `IndexWriter.UpdateDocuments`, because the existing single-document update path did not express the Orchard batch as one ordered delete-before-add operation. The Core method validates distinct delete terms, waits for prior pending writes, applies the queued deletions before adding replacements, and then uses the existing batch add/commit path. The provider consumes this through a project reference; no Core package version override was introduced.

## Performance results

The comparison used the same 2,000 published Orchard content items for both providers. Search and single-update procedures used one cold sample, three warm-ups, and ten measured samples per provider and query. Build, rebuild, batch update, and delete procedures each used three runs. The operating-system page cache was not flushed. Search timing was collected inside `ISearchService.SearchAsync`; single-update timing included the Orchard mutation and provider task processing. Raw records and provenance are in [the machine-readable run file](orchard-core-search-provider-runs.json).

Latency rows report min / median / max in milliseconds. Search values are the ten warm measured samples; the separate cold column reports the one first-search sample.

| Search query | Provider | Cold | Warm min / median / max |
| --- | --- | ---: | ---: |
| Term | Lucene | 303.406 | 1.855 / 3.197 / 11.089 |
| Term | LeanCorpus | 1,543.910 | 37.753 / 54.971 / 131.522 |
| Multi-term | Lucene | 9.527 | 5.243 / 5.562 / 5.604 |
| Multi-term | LeanCorpus | 55.713 | 37.382 / 38.876 / 45.015 |

| Procedure | Provider | Cold | Min | Median | Max |
| --- | --- | ---: | ---: | ---: | ---: |
| Single-document update plus task processing (ms) | Lucene | 3,424.269 | 15.419 | 25.482 | 37.638 |
| Single-document update plus task processing (ms) | LeanCorpus | 410.099 | 67.475 | 112.963 | 187.284 |
| Initial build, 2,000 documents (ms) | Lucene | n/a | 937.345 | 1,457.465 | 3,706.059 |
| Initial build, 2,000 documents (ms) | LeanCorpus | n/a | 1,725.126 | 2,438.287 | 5,977.841 |
| Rebuild, 2,000 documents (ms) | Lucene | n/a | 844.728 | 853.946 | 875.100 |
| Rebuild, 2,000 documents (ms) | LeanCorpus | n/a | 1,444.542 | 1,529.738 | 1,933.725 |
| 100-item batch update throughput (items/s) | Lucene | n/a | 47.827 | 99.814 | 118.705 |
| 100-item batch update throughput (items/s) | LeanCorpus | n/a | 39.751 | 68.181 | 93.692 |
| 100-item delete throughput (items/s) | Lucene | n/a | 340.844 | 346.901 | 375.005 |
| 100-item delete throughput (items/s) | LeanCorpus | n/a | 340.015 | 345.686 | 372.990 |

| Resource measure | Provider | Initial build median | Rebuild median |
| --- | --- | ---: | ---: |
| Index size (bytes) | Lucene | 1,643,614 | 1,594,462 |
| Index size (bytes) | LeanCorpus | 4,239,201 | 4,501,345 |
| Process allocation delta (bytes) | Lucene | 571,263,760 | 579,107,488 |
| Process allocation delta (bytes) | LeanCorpus | 618,703,368 | 611,738,224 |
| Process working set after indexing (bytes) | Lucene | 272,986,112 | 283,295,744 |
| Process working set after indexing (bytes) | LeanCorpus | 298,196,992 | 306,630,656 |

| Open/startup measure | Provider | Result |
| --- | --- | ---: |
| Host startup (ms) | Shared host | 58.980 |
| Index open via `IIndexManager.ExistsAsync` after host restart (ms) | Lucene | 0.012 |
| Index open via `IIndexManager.ExistsAsync` after host restart (ms) | LeanCorpus | 174.345 |

These are diagnostic results from one local host and a small corpus. They do not establish general performance or superiority.

## Problems discovered in LeanCorpus

The provider needed batch replacement semantics so updating many Orchard documents could durably apply all deletes before the batch replacements. A Core `IndexWriter.UpdateDocuments` overload was added and covered by the existing Core bulk-update integration tests. The provider uses it to avoid a delete from the same commit removing its replacement.

## Problems discovered in Orchard integration

Orchard Core 3.0.1's `CreateIndexingTaskContentHandler` queues a `Delete` task when content has no active version. Its stock `ContentIndexingService` returns no `BuildDocumentIndexContext` for that task, and `NamedIndexingService` skips document handlers for a null context. Separately, Orchard's deferred `IndexingContentHandler` handles `IContentManager.RemoveAsync` and calls the provider's `DeleteDocumentsAsync`. The browser lifecycle test now uses that public removal path and verifies that both Lucene and LeanCorpus stop returning the removed content. Unpublish is tested separately through Orchard's content-index lifecycle. R7 continues to use a dedicated test consumer to exercise the provider's durable delete/cursor boundary; it does not claim stock task replay for hard deletes.

## Remaining limitations

- The paired DateTime range matched the same interior records after using Lucene's DateTools encoding and a wider Lucene bound. Exact lower-bound inclusivity differed in the observed query path and remains a documented semantic difference.
- Geo result semantics were tested against LeanCorpus only. The Orchard Lucene geo query is provider-specific and was not included in the differential suite.
- Orchard's stock task processor skips `Delete` contexts. Hard removal is proven through `IContentManager.RemoveAsync` and Orchard's deferred content-index handler; R7 validates the provider's delete commit/cursor recovery boundary through a dedicated test consumer rather than stock task replay.
- Performance was measured on one Debian host, with OS cache state left intact, using a 2,000-item corpus. Windows, hosted CI, and production-sized performance have not been qualified.
- The recipe's Lucene profile has `StoreSourceData=false`; the paired stored-value test uses an explicitly stored Orchard index field.

## Recommendation

Keep this package at its current alpha version. The provider registration, Orchard content lifecycle (including hard removal), generic site search, LeanCorpus query source, vector probe, field-level comparisons, and R1-R8 provider recovery probes have passing evidence. Do not make a general performance or Lucene-parity claim. The evidence distinguishes Orchard's content-removal dispatch from its background task replay, which skips `Delete` contexts.
