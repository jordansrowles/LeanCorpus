# DocValues

DocValues are per-document column stores that enable sorting, faceting, aggregations, and field collapsing without reading the inverted index. They live in separate sidecar files (`*.dvn`, `*.dvs`, etc.) and are populated at index time alongside postings.

## Which fields populate DocValues

| Field type | Default | DocValues populated |
|---|---|---|
| `StringField` | on | `SortedDocValues`, `SortedSetDocValues`, `BinaryDocValues` |
| `NumericField` | on | `NumericDocValues`, `SortedNumericDocValues` |
| `Int64Field` | on | `NumericDocValues`, `SortedNumericDocValues` |
| `BinaryField` | on (hardcoded) | `BinaryDocValues` |
| `StoredField` | on (hardcoded) | `BinaryDocValues` |
| `GeoPointField` | on (hardcoded) | `NumericDocValues` (encoded lat/lon) |
| `TextField` | off | Does not populate DocValues |
| `VectorField` | off (hardcoded) | None (vectors use `.vec` and `.hnsw`) |

## Types

| Type | File | Use |
|---|---|---|
| `NumericDocValues` | `.dvn` | Single-valued `long` per document. Backs numeric sort fields and aggregations. |
| `SortedDocValues` | `.dvs` | Single-valued string ordinal per document. Backs string sort fields, faceting, and field collapsing. |
| `SortedSetDocValues` | `.dss` | Multi-valued string ordinals. Populated for `StringField` when a document has multiple values for the same field. |
| `SortedNumericDocValues` | `.dsn` | Multi-valued numeric values. Populated for `NumericField` and `Int64Field`. |
| `BinaryDocValues` | `.dvb` | Multi-valued byte arrays. Populated for `BinaryField`, `StoredField`, and `StringField`. |

## Opting out

```csharp
// Skip DocValues for a field you never sort or facet on
doc.Add(new NumericField("internal-id", id, stored: false) { StoreDocValues = false });
```

Turning off DocValues cuts per-document buffer overhead during indexing and reduces the flush I/O footprint. Only column-store operations are affected — the inverted index still serves all query types.

For `TextField`, DocValues are off by default. If you need to sort or facet on a text field, use a separate `StringField` with the same value.

For `BinaryField`, `StoredField`, and `GeoPointField`, DocValues are always on and cannot be disabled.

For hierarchical facets, use `FacetPathIndexer.AddToDocument`. It writes each
path prefix as a queryable `StringField` with sorted-set DocValues, so
`HierarchicalFacetRequest` can count immediate children and `DrillDownQuery`
can select an exact path. The path encoder is length-prefixed and does not use
component delimiters as identity.

## Where DocValues are used

| Operation | DocValues type needed |
|---|---|
| Numeric sort | `NumericDocValues` |
| String sort | `SortedDocValues` |
| Faceting | `SortedDocValues` or `SortedSetDocValues` |
| Field collapsing | `SortedDocValues` |
| Numeric aggregations | `NumericDocValues` or `SortedNumericDocValues` |

If a field lacks the required DocValues, the operation fails with an error — there is no fallback to the inverted index for sorting, faceting, or aggregations.

## Reading DocValues

DocValues readers are opened lazily per segment. They expose typed accessors keyed by field name:

```csharp
var reader = searcher.GetSegmentReader(0);
var numericValues = reader.GetNumericDocValues("price");
long value = numericValues.Get(docId);

var sortedValues = reader.GetSortedDocValues("category");
int ordinal = sortedValues.GetOrdinal(docId);
string category = sortedValues.LookupOrdinal(ordinal);
```

Most applications don't read DocValues directly — they go through `IndexSearcher` methods that use DocValues internally (sorting, faceting, aggregations).

## See also

- [Field types](../getting-started/02-fields.md)
- [Aggregations](../advanced/01-aggregations.md)
- [Field collapsing](../advanced/09-field-collapsing.md)
- [Sorting](../searching/06-sorting.md)
