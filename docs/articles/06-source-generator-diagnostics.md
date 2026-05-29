# Source generator diagnostics

`LeanCorpus.SourceGen` reports LCGEN diagnostics when an annotated model cannot produce safe, direct mapping code.

| ID | Meaning | Fix |
|---|---|---|
| LCGEN001 | Property type is unsupported for the selected field attribute. | Use a supported CLR type or a different attribute. |
| LCGEN002 | Two or more properties use the same generated field name. | Give each mapped property a unique field name. |
| LCGEN003 | Field name is null, empty, or contains control characters. | Use a non-empty LeanCorpus field name. |
| LCGEN004 | `FromStoredDocument` cannot materialise a mapped member from stored fields. | Store the field, remove unsupported vector mapping from round-tripped models, or avoid `FromStoredDocument`. |
| LCGEN005 | A property has more than one Lean field attribute. | Keep exactly one mapping attribute, or use `[LeanIgnore]`. |
| LCGEN006 | A temporal or decimal numeric property has no encoding. | Set `Encoding = LeanNumericEncoding...`. |
| LCGEN007 | A vector property has no positive dimension. | Set `Dimension` to the expected vector length. |
| LCGEN008 | A collection shape is unsupported. | Use `string[]`, `IReadOnlyList<string>`, or `float[]` for vectors. |
| LCGEN009 | A geo-point property is not `LeanGeoLocation`. | Change the property type to `LeanGeoLocation` or `LeanGeoLocation?`. |
| LCGEN010 | The document target is generic or nested. | Move the mapped model to a non-generic, non-nested type. |
| LCGEN011 | A mapped property is not accessible to generated code. | Use a public, internal, or protected-internal instance property with an accessible getter. |
| LCGEN012 | The generated materialiser cannot construct or assign the model. | Add an accessible parameterless constructor and assignable mapped members, or remove unmapped `required` members. |
| LCGEN013 | `DecimalAsString` was combined with `Stored = false`. | Keep `Stored = true`; decimal-as-string is stored-only by design. |

## Examples

```csharp
// LCGEN006
[LeanNumeric("published")]
public DateTimeOffset Published { get; init; }
```

Fix:

```csharp
[LeanNumeric("published", Encoding = LeanNumericEncoding.UnixMilliseconds)]
public DateTimeOffset Published { get; init; }
```

```csharp
// LCGEN013
[LeanNumeric("amount", Encoding = LeanNumericEncoding.DecimalAsString, Stored = false)]
public decimal Amount { get; init; }
```

Fix:

```csharp
[LeanNumeric("amount", Encoding = LeanNumericEncoding.DecimalAsString)]
public decimal Amount { get; init; }
```

## See also

- [Source-generated mapping](../tutorials/getting-started/04-source-generated-mapping.md)
- [Stored round-tripping](../tutorials/index-management/05-stored-round-tripping.md)
