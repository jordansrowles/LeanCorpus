using System.Reflection;
using Rowles.LeanCorpus.Index;

namespace Rowles.LeanCorpus.Tests.Core.Index;
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
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

    [Fact]
    public void ForIssue_ReadOnlyRetiredCodec_RecommendsFloat32Only()
    {
        var actions = IndexRepairRecommendations.ForIssue(IndexCheckIssueCodes.ReadOnlyRetiredCodec);

        Assert.Single(actions);
        Assert.Equal(
            "This codec is read-only in this build; rebuild from original documents and embeddings into a separate Float32 index.",
            actions[0]);
    }
}
