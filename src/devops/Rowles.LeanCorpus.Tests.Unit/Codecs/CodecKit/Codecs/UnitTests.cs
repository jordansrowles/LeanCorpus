using CodecUnit = Rowles.LeanCorpus.Codecs.CodecKit.Codecs.Unit;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Codecs;

[Trait("Category", "CodecKit")]
public sealed class UnitTests
{
    [Fact(DisplayName = "Unit: Value is accessible and is a Unit")]
    public void Value_IsUnit()
    {
        Assert.IsType<CodecUnit>(CodecUnit.Value);
    }

    [Fact(DisplayName = "Unit: Equals(Unit) returns true for any Unit")]
    public void EqualsUnit_ReturnsTrue()
    {
        var a = default(CodecUnit);
        var b = CodecUnit.Value;
        Assert.True(a.Equals(b));
        Assert.True(b.Equals(a));
        Assert.True(a.Equals(a));
    }

    [Fact(DisplayName = "Unit: Equals(object) returns true for Unit, false for non-Unit")]
    public void EqualsObject_ReturnsTrueForUnit()
    {
        var u = default(CodecUnit);

        Assert.True(u.Equals((object)u));
        Assert.True(u.Equals((object)CodecUnit.Value));

        Assert.False(u.Equals((object?)null));
        Assert.False(u.Equals("()"));
        Assert.False(u.Equals(0));
        Assert.False(u.Equals(false));
    }

    [Fact(DisplayName = "Unit: GetHashCode returns 0")]
    public void GetHashCode_ReturnsZero()
    {
        Assert.Equal(0, default(CodecUnit).GetHashCode());
        Assert.Equal(0, CodecUnit.Value.GetHashCode());
    }

    [Fact(DisplayName = "Unit: ToString returns \"()\"")]
    public void ToString_ReturnsParens()
    {
        Assert.Equal("()", default(CodecUnit).ToString());
        Assert.Equal("()", CodecUnit.Value.ToString());
    }

    [Fact(DisplayName = "Unit: == returns true for any two Units")]
    public void OperatorEquals_ReturnsTrue()
    {
        Assert.True(default(CodecUnit) == CodecUnit.Value);
        Assert.True(CodecUnit.Value == default(CodecUnit));
        Assert.True(CodecUnit.Value == default(CodecUnit));
    }

    [Fact(DisplayName = "Unit: != returns false for any two Units")]
    public void OperatorNotEquals_ReturnsFalse()
    {
        Assert.False(default(CodecUnit) != CodecUnit.Value);
        Assert.False(CodecUnit.Value != default(CodecUnit));
        Assert.False(CodecUnit.Value != default(CodecUnit));
    }

    [Fact(DisplayName = "Unit: IEquatable<Unit>.Equals works")]
    public void IEquatableEquals_Works()
    {
        IEquatable<CodecUnit> equatable = default(CodecUnit);
        Assert.True(equatable.Equals(CodecUnit.Value));
    }

    [Fact(DisplayName = "Unit: Default Unit equals Unit.Value")]
    public void Default_EqualsValue()
    {
        Assert.True(default(CodecUnit) == CodecUnit.Value);
        Assert.Equal(default(CodecUnit), CodecUnit.Value);
    }
}
