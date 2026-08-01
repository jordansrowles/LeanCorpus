# Documents and fields

Use this page when choosing how application data becomes a searchable document.

A `LeanDocument` contains named fields. A field can be indexed for matching, stored for retrieval, exposed through DocValues for sorting and aggregation, or configured for term vectors and payloads.

| Need | Field type | Notes |
|---|---|---|
| Full-text matching | `TextField` | Analyse text at index time. |
| Exact identity or filter | `StringField` | Preserves the whole value as one term. |
| Numeric range, sort or aggregation | Numeric field with DocValues | Use the appropriate integer, floating-point or date representation. |
| Semantic similarity | `VectorField` | All vectors in a field share one dimension. |
| Return original value | Stored field | Storage alone does not make a field searchable. |
| Highlighting or positional queries | Term vectors or positions | Adds index size and indexing cost. |

See also: [Field types](../getting-started/02-fields.md), [DocValues](../index-management/07-docvalues.md), and <xref:Rowles.LeanCorpus.Document.LeanDocument>.
