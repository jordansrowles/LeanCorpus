# Source generator diagnostics

`LeanCorpus.SourceGen` reports LCGEN diagnostics when an annotated model can't produce safe mapping code.

| ID | Meaning | Fix |
|---|---|---|
| LCGEN001 | Property type unsupported for the selected field attribute | Use a supported CLR type or a different attribute |
| LCGEN002 | Two properties use the same generated field name | Give each a unique field name |
| LCGEN003 | Field name is null, empty, or has control characters | Use a valid name |
| LCGEN004 | `FromStoredDocument` can't materialise a member from stored fields | Store the field or remove unsupported vector mapping |
| LCGEN005 | Property has more than one Lean field attribute | Keep exactly one, or use `[LeanIgnore]` |
| LCGEN006 | Temporal or decimal numeric property has no encoding | Set `Encoding = LeanNumericEncoding...` |
| LCGEN007 | Vector property has no positive dimension | Set `Dimension` |
| LCGEN008 | Collection shape unsupported | Use `string[]`, `IReadOnlyList<string>`, or `float[]` for vectors |
| LCGEN009 | Geo-point property is not `LeanGeoLocation` | Change to `LeanGeoLocation` or `LeanGeoLocation?` |
| LCGEN010 | Document target is generic or nested | Move to a non-generic, non-nested type |
| LCGEN011 | Mapped property not accessible to generated code | Use a public, internal, or protected-internal instance property with an accessible getter |
| LCGEN012 | Generated materialiser can't construct or assign the model | Add a parameterless constructor and assignable mapped members, or remove unmapped `required` members |
| LCGEN013 | `DecimalAsString` combined with `Stored = false` | Keep `Stored = true`; decimal-as-string is stored-only |

## Examples

```csharp
// LCGEN006 — missing encoding on a temporal field
[LeanNumeric("published")]
public DateTimeOffset Published { get; init; }
// Fix:
[LeanNumeric("published", Encoding = LeanNumericEncoding.UnixMilliseconds)]
public DateTimeOffset Published { get; init; }

// LCGEN013 — DecimalAsString must be stored
[LeanNumeric("amount", Encoding = LeanNumericEncoding.DecimalAsString, Stored = false)]
public decimal Amount { get; init; }
// Fix:
[LeanNumeric("amount", Encoding = LeanNumericEncoding.DecimalAsString)]
public decimal Amount { get; init; }
```

## See also

- [Source-generated mapping](../tutorials/getting-started/04-source-generated-mapping.md)
- [Stored round-tripping](../tutorials/index-management/05-stored-round-tripping.md)
