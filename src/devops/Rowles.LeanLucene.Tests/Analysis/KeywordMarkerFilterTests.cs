namespace Rowles.LeanLucene.Tests.Analysis;

/// <summary>
/// Unit tests for <see cref="KeywordMarkerFilter"/> covering the null-guard,
/// the Apply no-op, and both IsKeyword overloads (string and ReadOnlySpan).
/// IsKeyword is internal but accessible via InternalsVisibleTo.
/// </summary>
public sealed class KeywordMarkerFilterTests
{
    /// <summary>Verifies constructor throws when keywords enumerable is null.</summary>
    [Fact(DisplayName = "KeywordMarkerFilter: Null Keywords Throws")]
    public void KeywordMarkerFilter_NullKeywords_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new KeywordMarkerFilter(null!));
    }

    /// <summary>Verifies Apply does not throw and leaves the token list unchanged.</summary>
    [Fact(DisplayName = "KeywordMarkerFilter: Apply Is No-Op")]
    public void KeywordMarkerFilter_Apply_IsNoOp()
    {
        var filter = new KeywordMarkerFilter(["stop"]);
        var tokens = new List<Rowles.LeanLucene.Analysis.Token> { new("stop", 0, 4) };
        filter.Apply(tokens);
        Assert.Single(tokens);
    }

    /// <summary>Verifies IsKeyword(string) returns true for a registered keyword.</summary>
    [Fact(DisplayName = "KeywordMarkerFilter: IsKeyword String Returns True For Known Word")]
    public void KeywordMarkerFilter_IsKeyword_String_ReturnsTrueForKnownWord()
    {
        var filter = new KeywordMarkerFilter(["run", "jump"]);
        Assert.True(filter.IsKeyword("run"));
    }

    /// <summary>Verifies IsKeyword(string) returns false for an unregistered term.</summary>
    [Fact(DisplayName = "KeywordMarkerFilter: IsKeyword String Returns False For Unknown Word")]
    public void KeywordMarkerFilter_IsKeyword_String_ReturnsFalseForUnknownWord()
    {
        var filter = new KeywordMarkerFilter(["run", "jump"]);
        Assert.False(filter.IsKeyword("fly"));
    }

    /// <summary>Verifies IsKeyword(ReadOnlySpan) returns true for a registered keyword.</summary>
    [Fact(DisplayName = "KeywordMarkerFilter: IsKeyword Span Returns True For Known Word")]
    public void KeywordMarkerFilter_IsKeyword_Span_ReturnsTrueForKnownWord()
    {
        var filter = new KeywordMarkerFilter(["run", "jump"]);
        Assert.True(filter.IsKeyword("jump".AsSpan()));
    }

    /// <summary>Verifies IsKeyword(ReadOnlySpan) returns false for an unregistered term.</summary>
    [Fact(DisplayName = "KeywordMarkerFilter: IsKeyword Span Returns False For Unknown Word")]
    public void KeywordMarkerFilter_IsKeyword_Span_ReturnsFalseForUnknownWord()
    {
        var filter = new KeywordMarkerFilter(["run", "jump"]);
        Assert.False(filter.IsKeyword("skip".AsSpan()));
    }

    /// <summary>Verifies IsKeyword is case-sensitive (uses Ordinal comparison).</summary>
    [Fact(DisplayName = "KeywordMarkerFilter: IsKeyword Is Case Sensitive")]
    public void KeywordMarkerFilter_IsKeyword_IsCaseSensitive()
    {
        var filter = new KeywordMarkerFilter(["Run"]);
        Assert.False(filter.IsKeyword("run"));
        Assert.True(filter.IsKeyword("Run"));
    }
}
