using System.Reflection;
using Rowles.LeanCorpus.Index;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

[Trait("Category", "Index")]
[Trait("Category", "Validation")]
public sealed class IndexRepairRecommendationsTests
{
    [Fact(DisplayName = "Repair recommendations: Modern issue codes have suggested actions")]
    public void ForIssue_ModernIssueCodes_ReturnsSuggestedActions()
    {
        var codes = typeof(IndexCheckIssueCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(static field => (Name: field.Name, Code: (string)field.GetRawConstantValue()!))
            .Where(static item => item.Code != IndexCheckIssueCodes.LegacyIssue);

        foreach (var (name, code) in codes)
            Assert.NotEmpty(IndexRepairRecommendations.ForIssue(code));
    }
}
