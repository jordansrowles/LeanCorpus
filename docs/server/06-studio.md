# Studio

`Rowles.LeanCorpus.Studio` is an embeddable Razor Class Library. Register it with `AddLeanCorpusStudio()`, enable static files, then map it with `MapLeanCorpusStudio()`. The reference host serves it at `/studio`.

The Community surface includes server health and readiness, index listing and creation, index statistics, schema, bounded document and segment inspection, document indexing, a search and explanation test bench, mutable settings, and confirmed index deletion. It calls the public REST contract, never parses index files, and inserts indexed values into the page with text-only DOM APIs.

The first Community release intentionally contains no cluster or Enterprise pages.
