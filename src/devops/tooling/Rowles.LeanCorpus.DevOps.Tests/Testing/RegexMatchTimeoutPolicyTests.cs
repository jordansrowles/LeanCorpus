using System.Text.RegularExpressions;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class RegexMatchTimeoutPolicyTests
{
    [Fact]
    public void Shared_timeout_bounds_backtracking_matches()
    {
        var expression = new Regex("^(a+)+$", RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeout);
        var input = string.Concat(Enumerable.Repeat("a", 200_000)) + "!";

        Assert.Throws<RegexMatchTimeoutException>(() => expression.IsMatch(input));
    }
}
