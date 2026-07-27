# Term vectors and payloads

Positions, offsets, payloads, and term vectors all add information beyond a term's document frequency, but they serve different purposes.

| Feature | Scope | Typical use |
|---|---|---|
| Positions | Field postings | Phrase, span, and interval queries |
| Offsets | Field postings | Mapping matches back to source text |
| Payloads | Individual token positions | Application-defined token metadata |
| Term vectors | One field in one document | Highlighting, More Like This, and document-local term inspection |

## Enable term vectors

```csharp
var config = new IndexWriterConfig
{
    StoreTermVectors = true,
};
```

Term vectors persist a per-document term inventory for eligible indexed fields. They avoid reconstructing that inventory from collection postings, but add `.tvd` and `.tvx` data and more flush work.

Enable them when a feature reads document-local terms frequently. Do not enable them solely for ordinary term, phrase, or Boolean search.

## Enable payload storage

```csharp
var config = new IndexWriterConfig
{
    StorePayloads = true,
};
```

Payloads originate in the analysis pipeline and are attached to positions. They are opaque bytes to the postings codec. Both the token stream and the indexed field options must provide the positional data needed by the consumer.

Payloads are not stored fields and are not returned with a document automatically.

## Storage cost

Cost depends on:

- unique and repeated terms per document;
- position and offset counts;
- payload length;
- stored-field and postings compression;
- merge frequency.

Measure index size using [Index size and statistics](../observability/06-index-size-and-statistics.md) on representative documents. A small synthetic corpus often understates dictionary and positional overhead.

## Compatibility

These settings apply to newly written segments. Readers use segment field metadata to determine which streams exist. If an application requires term vectors or payloads for every result, migrate or reindex older segments rather than assuming a mixed index has uniform capabilities.

See [Highlighting](../advanced/03-highlighting.md), [More Like This](../advanced/07-more-like-this.md), and [Phrase and proximity](../searching/03-phrase-and-proximity.md).
