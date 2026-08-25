# Adding persistent formats

A persistent format is a file role in an immutable `CodecCatalog`. It is not merely an extension or a `CodecConstants` value.

## Choose stable identities

Use namespaced lowercase identifiers. A family groups files that must be understood or migrated together.

```text
example.product
example.product.data
example.product.index
```

Identifiers are persistent wire identities. Do not rename one after release.

## Declare versions and storage policy

```csharp
var data = new CodecFileDescriptor(
    formatId: "example.product.data",
    familyId: "example.product",
    displayName: "Product data",
    fileMatcher: CodecFileMatcher.Extension(".prd"),
    currentFormatVersion: 2,
    supportedVersions:
    [
        new CodecVersionDescriptor(
            1,
            "example-product-v1",
            isReadable: true,
            legacyFraming: CodecLegacyFraming.CodecKitEnvelope,
            migrationBehaviour: CodecMigrationBehaviour.Rewrite),
        new CodecVersionDescriptor(
            2,
            "example-product-v2",
            isReadable: true,
            isWritable: true,
            migrationBehaviour: CodecMigrationBehaviour.Rewrite),
    ],
    accessKind: CodecAccessKind.RandomAccess,
    currentFraming: CodecFramingPolicy.Canonical,
    checksumPolicy: CodecChecksumPolicy.XxHash64,
    migrationBehaviour: CodecMigrationBehaviour.Rewrite,
    temporaryFileMatchers:
    [
        CodecFileMatcher.ExtensionWithTrailingSuffix(".prd", ".tmp")
    ]);

var family = new CodecFamilyDescriptor(
    "example.product",
    "Product files",
    [data]);

var catalog = new CodecCatalogBuilder()
    .AddBuiltIns()
    .Add(family)
    .Build();
```

The builder rejects duplicate IDs, overlapping physical claims, inconsistent current versions, canonical formats without checksums, and invalid temporary-file matchers. Built-in IDs cannot be silently replaced.

## Select the access kind

| Access kind | Use when |
|---|---|
| `Materialised` | The complete body is deliberately decoded into an in-memory model |
| `Streaming` | The body is consumed sequentially and may be large |
| `RandomAccess` | The reader retains a bounded `IndexInput` and seeks directly |
| `External` | Another serialiser owns the representation, such as JSON or a container |

The descriptor is an enforceable resource contract. Do not label a file `RandomAccess` and then call `ReadBody()` during a normal open.

## Implement the writer

Use the descriptor's current version. Do not repeat a version literal in the writer.

```csharp
CodecFileWriter.WriteAtomically(path, data, durable, body =>
{
    body.WriteInt32(recordCount);
    WriteRecords(body, records);
});
```

For a multi-file family, ensure direct, flush, merge and migration paths all call the same normal writers. Coordinated files must be published as one validated operation.

## Implement the reader

Use `CodecFileReader.OpenSupported` when supported historical envelopes or trailers must remain readable. Branch on `FormatVersion`, not frame version.

```csharp
using var input = new IndexInput(path);
using var session = CodecFileReader.OpenSupported(input, data);

return session.FormatVersion switch
{
    1 => OpenV1(input, session.BodyStart, session.BodyLength),
    2 => OpenV2(input, session.BodyStart, session.BodyLength),
    _ => throw new InvalidDataException("Unsupported product format version.")
};
```

Retained readers must keep all offsets inside `[BodyStart, BodyStart + BodyLength)`. Deep checksum validation is explicit.

For specialist semantic checks, register an `ICodecFileValidationHandler` on the file descriptor. Deep inspection supplies it with a separately owned `IndexInput` bounded to the decoded body. When correctness depends on several members, register an `ICodecFamilyValidationCoordinator` on the family instead. It receives the available body inputs keyed by stable format ID for each logical segment, with loose and compound storage handled identically.

## Add the complete integration

A persistent format is complete only when all applicable surfaces agree:

1. catalogue descriptor and invariant tests;
2. current direct, flush and merge writers;
3. canonical and supported legacy readers;
4. logical loose and compound inspection;
5. compatibility and open-guard policy;
6. shallow and deep validation;
7. migration or an explicit unsupported policy;
8. temporary-file and recovery recognition;
9. golden, corruption and backwards-fixture tests;
10. contributor and user documentation.

## Version changes

Bump the body-format version only when the body wire representation changes. A move from a legacy envelope to canonical Frame v1 does not itself change the body-format version. Retain readable historical descriptors and fixtures for every version promised by the compatibility policy.

## Versionless formats

JSON manifests, compound containers and deliberately retained sidecars still belong in the catalogue. Declare them with `currentFormatVersion: null`, the appropriate `External` or `Container` framing policy, and no canonical checksum policy. Their own schema or container validator remains responsible for integrity.

## See also

- [CodecKit overview](index.md)
- [Creating codecs](01-creating-codecs.md)
- [Codec migrations](03-migrations.md)
