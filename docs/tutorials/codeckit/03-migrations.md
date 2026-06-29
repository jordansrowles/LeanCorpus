# Codec migrations

When a codec format's binary layout changes, you bump its version and add a new `CodecVersionStep`. Readers consult the step list to dispatch to the correct parser.

## When to bump a version

Bump when the wire format changes in a way that older readers can't parse. Examples:

- Adding a new field to the body
- Changing a field's encoding (e.g. `Int32LE` → `VarInt32`)
- Adding optional data gated by a flag

Don't bump for cosmetic changes or parser refactors that produce the same wire bytes.

## How to bump

### 1. Add the new constant

In `CodecConstants.cs`:

```csharp
public const int PostingsVersion = 2; // was 1
```

### 2. Add the version step

In `CodecFormats.cs`:

```csharp
reg.Register(new CodecFormat("pos", [
    new CodecVersionStep(1, "pos-v1", Codec.BytesOwnedRemaining()),
    new CodecVersionStep(2, "pos-v2", Codec.BytesOwnedRemaining()),  // new
]));
```

Version steps are ordered oldest to newest. `Steps[^1]` is always the current version.

### 3. Update the reader

Add a version branch in the reader class:

```csharp
byte version = CodecFileHeader.ReadVersion(input, CodecFormats.Postings);

switch (version)
{
    case 1:
        ReadPostingsV1(input);
        break;
    case 2:
        ReadPostingsV2(input);
        break;
    default:
        throw new InvalidDataException($"Unknown postings version: {version}");
}
```

### 4. Update the writer

Writers always produce the current version. `CodecFileHeader.Write` picks it up from `CodecConstants` automatically. Just update the body-writing code to produce the new format.

## Compatibility

`IndexCompatibility.Check` inspects codec versions in existing index files and compares them against the registered formats:

- `Compatible` — all files are at the current version
- `MigrationRecommended` — some files are at older but supported versions
- `MigrationRequired` — policy requires migration before open
- `UnsupportedFutureFormat` — a file has a version newer than any registered step

`IndexCodecMigrator.Migrate` copies the index to a staging directory, rewrites older codec files through the current writer, deep-validates, and publishes back:

```csharp
var result = IndexCodecMigrator.Migrate(dir, new IndexCodecMigrationOptions
{
    DryRun = false,
    StagingDirectory = "./index.migration"
});
```

## Version step details

Each `CodecVersionStep` is:

```csharp
public sealed record CodecVersionStep(
    int Version,        // numeric version (1, 2, ...)
    string Label,       // human-readable label ("pos-v1")
    ICodec<byte[]> Reader  // codec for this version's body
);
```

The `Reader` codec is almost always `Codec.BytesOwnedRemaining()` — it reads the remaining bytes as a `byte[]`. The actual structural parsing lives in the reader class, not in the version step itself. This keeps the codec layer thin and the format layer where it belongs.

For complex formats that need structural parsing at the codec level, you can supply a full `RecordCodec<T>` as the reader. The `IndexedCodec<THeader, TCursor>` combinator is useful here: it decodes a small header eagerly and returns a cursor for lazy body access.

## See also

- [Adding formats](02-adding-formats.md)
- [Creating codecs](01-creating-codecs.md)
- [Validation and recovery](../../index-management/03-validation-recovery.md)
