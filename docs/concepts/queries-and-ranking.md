# Queries and ranking

Use this page to choose a query and understand why results appear in their order.

Queries match indexed terms or values. Collectors retain a bounded result window, similarities calculate relevance scores, and deterministic tie-breaking resolves equal scores. Filters narrow candidates without contributing a score. Ranking profiles, rules, rescoring and cursor sessions compose only where their contracts support stable ordering.

| Need | Start with |
|---|---|
| User-entered search text | Query parser or `TermQuery` after analysis |
| Exact identifier | `TermQuery` over a `StringField` |
| Prefix or autocomplete | Prefix query, n-gram analysis or suggester |
| Ordered words | Phrase or interval query |
| Tolerant spelling | Fuzzy query |
| Numeric/date bounds | Range query |
| Location | Geo query |
| Semantic similarity | Vector query |

See also: [Query types](../searching/01-query-types.md), [Ranking profiles](../searching/12-ranking.md), and <xref:Rowles.LeanCorpus.Search.Searcher.IndexSearcher>.
