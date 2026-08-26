# Search and explain

Search requests use an explicit query discriminator in query.kind. The Community translator validates the discriminator, required fields, schema field type, nesting, clause count, wildcard/regular-expression complexity and vector dimensions before engine execution.

The supported query kinds are:

~~~text
queryString, term, boolean, phrase, prefix, wildcard,
regexp, spanNear, vector
~~~

For example:

~~~json
{
  "query": {
    "kind": "boolean",
    "must": [
      { "kind": "term", "field": "isbn", "value": "978-1" }
    ]
  },
  "size": 10,
  "includeDocuments": true,
  "includeHighlights": false,
  "consistency": "Local"
}
~~~

Search returns hits, total-hit metadata, measured timing.tookMilliseconds, and optional stored document projections. Terms facets are the supported Community facet subset. Use a positive size and request a facet with kind 0, the JSON value for Terms, when facet buckets are needed. Range facets use kind 1 and are intentionally unsupported. Highlights are also intentionally unsupported and return highlights_not_supported; they are not silently ignored.

## Sort and search-after

Sort definitions are applied in order and _id is always appended as the deterministic final tie-break. With no explicit sort, results use score followed by _id. Supported field sorts are Keyword, Boolean, Int64, Double and DateTime. The JSON SortDirection enum is 0 for ascending and 1 for descending.

~~~json
{
  "query": { "kind": "queryString", "text": "practical" },
  "size": 2,
  "sort": [
    { "field": "rank", "direction": 0 }
  ],
  "searchAfter": null
}
~~~

The response's nextSearchAfter is an opaque, versioned array. Copy it unchanged into the next request with the same query and sort definition:

~~~json
{
  "searchAfter": [1, "<sort identity from the response>", 10, "doc-001"]
}
~~~

The first value is the cursor version, the second binds the cursor to the exact sort shape, and the remaining values are the actual sort values in order, including _id. Cursor length, value types and sort identity are checked. A cursor from another sort or incompatible value is rejected instead of being guessed.

## Explain

Explain uses the same query translator as Search. Term and Vector queries return the engine explanation tree with match, score, description and child details. An existing document with another query kind returns the typed explain_not_supported failure. A missing document returns a non-match response. Explain timings are separate from Search timings.
