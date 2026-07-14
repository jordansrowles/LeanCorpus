namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Vectors;

/// <summary>
/// Unit tests for <see cref="Rowles.LeanCorpus.Codecs.Vectors.QuantisedVectorError"/> —
/// an internal readonly record struct that stores a per-vector floating-point correction
/// value applied after dequantisation to recover lost precision.
/// </summary>
[Trait("Category", "CodecKit")]
public sealed class QuantisedVectorErrorTests
{
    // ── Construction and property ─────────────────────────────────────────

    [Fact(DisplayName = "QuantisedVectorError: Constructor sets Correction property")]
    public void Constructor_SetsCorrection()
    {
        var error = new QuantisedVectorError(0.42f);
        Assert.Equal(0.42f, error.Correction);
    }

    [Fact(DisplayName = "QuantisedVectorError: Correction can be positive")]
    public void Correction_Positive()
    {
        var error = new QuantisedVectorError(1.5f);
        Assert.Equal(1.5f, error.Correction);
    }

    [Fact(DisplayName = "QuantisedVectorError: Correction can be negative")]
    public void Correction_Negative()
    {
        var error = new QuantisedVectorError(-0.75f);
        Assert.Equal(-0.75f, error.Correction);
    }

    [Fact(DisplayName = "QuantisedVectorError: Correction can be zero")]
    public void Correction_Zero()
    {
        var error = new QuantisedVectorError(0f);
        Assert.Equal(0f, error.Correction);
    }

    [Fact(DisplayName = "QuantisedVectorError: Correction can be very small (e.g. 0.0001f)")]
    public void Correction_VerySmall()
    {
        var error = new QuantisedVectorError(0.0001f);
        Assert.Equal(0.0001f, error.Correction);
    }

    [Fact(DisplayName = "QuantisedVectorError: Correction can be very large (e.g. 1000.0f)")]
    public void Correction_VeryLarge()
    {
        var error = new QuantisedVectorError(1000.0f);
        Assert.Equal(1000.0f, error.Correction);
    }

    // ── Infinity and NaN ──────────────────────────────────────────────────

    [Fact(DisplayName = "QuantisedVectorError: Correction can be PositiveInfinity")]
    public void Correction_PositiveInfinity()
    {
        var error = new QuantisedVectorError(float.PositiveInfinity);
        Assert.Equal(float.PositiveInfinity, error.Correction);
    }

    [Fact(DisplayName = "QuantisedVectorError: Correction can be NegativeInfinity")]
    public void Correction_NegativeInfinity()
    {
        var error = new QuantisedVectorError(float.NegativeInfinity);
        Assert.Equal(float.NegativeInfinity, error.Correction);
    }

    [Fact(DisplayName = "QuantisedVectorError: NaN Correction equals itself via Equals (record struct uses EqualityComparer<float>.Default)")]
    public void Correction_NaN_EqualsItself()
    {
        var error = new QuantisedVectorError(float.NaN);
        // EqualityComparer<float>.Default.Equals(NaN, NaN) returns true,
        // so the generated record-struct Equals also returns true.
        Assert.True(error.Equals(error));
        Assert.True(error.Equals(new QuantisedVectorError(float.NaN)));
    }

    // ── Equality (record struct value comparison) ─────────────────────────

    [Fact(DisplayName = "QuantisedVectorError: Equal instances (same Correction) are equal")]
    public void Equals_SameValue_ReturnsTrue()
    {
        var a = new QuantisedVectorError(0.5f);
        var b = new QuantisedVectorError(0.5f);
        Assert.Equal(a, b);
    }

    [Fact(DisplayName = "QuantisedVectorError: Different instances (different Correction) are not equal")]
    public void Equals_DifferentValue_ReturnsFalse()
    {
        var a = new QuantisedVectorError(0.5f);
        var b = new QuantisedVectorError(0.6f);
        Assert.NotEqual(a, b);
    }

    [Fact(DisplayName = "QuantisedVectorError: == operator returns true for equal values")]
    public void EqualityOperator_EqualValues_ReturnsTrue()
    {
        var a = new QuantisedVectorError(0.25f);
        var b = new QuantisedVectorError(0.25f);
        Assert.True(a == b);
    }

    [Fact(DisplayName = "QuantisedVectorError: != operator returns true for different values")]
    public void InequalityOperator_DifferentValues_ReturnsTrue()
    {
        var a = new QuantisedVectorError(0.25f);
        var b = new QuantisedVectorError(0.50f);
        Assert.True(a != b);
    }

    [Fact(DisplayName = "QuantisedVectorError: Value equality rather than reference equality (struct semantic)")]
    public void ValueEquality_NotReferenceEquality()
    {
        var a = new QuantisedVectorError(0.0f);
        var b = new QuantisedVectorError(0.0f);
        Assert.True(a.Equals(b));
        // Constrained box—struct Equals returns true for same field values.
        object boxedA = a;
        object boxedB = b;
        Assert.Equal(boxedA, boxedB);
    }

    // ── Hash code ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "QuantisedVectorError: GetHashCode produces same value for equal instances")]
    public void GetHashCode_SameValue_ReturnsSame()
    {
        var a = new QuantisedVectorError(0.3f);
        var b = new QuantisedVectorError(0.3f);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact(DisplayName = "QuantisedVectorError: GetHashCode differs for different Correction values")]
    public void GetHashCode_DifferentValue_ReturnsDifferent()
    {
        var a = new QuantisedVectorError(0.3f);
        var b = new QuantisedVectorError(0.4f);
        // Collisions are theoretically possible but vanishingly unlikely here.
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    // ── ToString ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "QuantisedVectorError: ToString includes the Correction value")]
    public void ToString_IncludesCorrection()
    {
        var error = new QuantisedVectorError(1.25f);
        string text = error.ToString();
        Assert.Contains("1.25", text, StringComparison.Ordinal);
        Assert.Contains("Correction", text, StringComparison.Ordinal);
    }

    // ── Deconstruction ────────────────────────────────────────────────────

    [Fact(DisplayName = "QuantisedVectorError: Deconstruction yields Correction")]
    public void Deconstruct_ReturnsCorrection()
    {
        var error = new QuantisedVectorError(-0.33f);
        error.Deconstruct(out float correction);
        Assert.Equal(-0.33f, correction);
    }

    [Fact(DisplayName = "QuantisedVectorError: Explicit Deconstruct call returns Correction")]
    public void ExplicitDeconstruct_CapturesCorrection()
    {
        var error = new QuantisedVectorError(0.99f);
        error.Deconstruct(out float corr);
        Assert.Equal(0.99f, corr);
    }

    // ── Named parameters ──────────────────────────────────────────────────

    [Fact(DisplayName = "QuantisedVectorError: Named parameter syntax constructs correctly")]
    public void NamedParameters_ConstructsCorrectly()
    {
        var error = new QuantisedVectorError(Correction: 0.5f);
        Assert.Equal(0.5f, error.Correction);
    }

    // ── Default constructor ───────────────────────────────────────────────

    [Fact(DisplayName = "QuantisedVectorError: Default constructor gives Correction = 0f")]
    public void DefaultConstructor_SetsCorrectionToZero()
    {
        var error = default(QuantisedVectorError);
        Assert.Equal(0f, error.Correction);

        var alsoDefault = new QuantisedVectorError();
        Assert.Equal(0f, alsoDefault.Correction);
    }

    // ── With-expression ───────────────────────────────────────────────────

    [Fact(DisplayName = "QuantisedVectorError: With-expression creates a new instance with updated Correction")]
    public void WithExpression_CreatesNewValue()
    {
        var original = new QuantisedVectorError(1.0f);
        var modified = original with { Correction = 2.0f };

        Assert.Equal(2.0f, modified.Correction);
        Assert.Equal(1.0f, original.Correction); // original unchanged
        Assert.NotEqual(original, modified);
    }

    // ── Dictionary key ────────────────────────────────────────────────────

    [Fact(DisplayName = "QuantisedVectorError: Can be used as a dictionary key")]
    public void DictionaryKey_WorksAsExpected()
    {
        var dict = new Dictionary<QuantisedVectorError, string>
        {
            { new QuantisedVectorError(0.1f), "low" },
            { new QuantisedVectorError(0.5f), "medium" },
            { new QuantisedVectorError(0.9f), "high" },
        };

        Assert.Equal("low",    dict[new QuantisedVectorError(0.1f)]);
        Assert.Equal("medium", dict[new QuantisedVectorError(0.5f)]);
        Assert.Equal("high",   dict[new QuantisedVectorError(0.9f)]);

        // Key not present should throw.
        Assert.Throws<KeyNotFoundException>(() => dict[new QuantisedVectorError(0.2f)]);
    }

    [Fact(DisplayName = "QuantisedVectorError: Dictionary lookup uses value equality (not reference)")]
    public void DictionaryKey_ValueEquality()
    {
        var dict = new Dictionary<QuantisedVectorError, string>();
        var key1 = new QuantisedVectorError(42f);
        var key2 = new QuantisedVectorError(42f);

        dict[key1] = "answer";
        // key2 is a different instance but equal by value — should succeed.
        Assert.True(dict.ContainsKey(key2));
        Assert.Equal("answer", dict[key2]);
    }

    // ── Borderline: NaN in dictionary ─────────────────────────────────────

    [Fact(DisplayName = "QuantisedVectorError: NaN Correction can be used as dictionary key (consistent Equals + GetHashCode)")]
    public void DictionaryKey_NaNCorrection()
    {
        // EqualityComparer<float>.Default gives NaN a stable hash code
        // and Equals(NaN, NaN) returns true, so dictionary should work.
        var key = new QuantisedVectorError(float.NaN);
        var same = new QuantisedVectorError(float.NaN);

        var dict = new Dictionary<QuantisedVectorError, string> { [key] = "nan" };

        Assert.True(dict.ContainsKey(same));
        Assert.Equal("nan", dict[same]);
    }
}
