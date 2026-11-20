namespace Rowles.LeanCorpus.DevOps.Tests.Snowball;

/// <summary>
/// The Snowball code is BSD-licensed and compiled into both Rowles.Text and, through source inclusion
/// (ADR021), Rowles.LeanCorpus. Both packages must therefore carry the notice.
/// </summary>
public sealed class ThirdPartyNoticeTests
{
    [Theory]
    [InlineData("src/core/Rowles.Text/Rowles.Text.csproj")]
    [InlineData("src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj")]
    public void PackagesThatCompileSnowballCodePackTheNotice(string project)
    {
        string root = SnowballTransformTests.FindRepositoryRoot();
        string content = File.ReadAllText(Path.Combine(root, project.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains(@"<None Include=""..\..\..\THIRD-PARTY-NOTICES.md"" Pack=""true"" PackagePath=""\"" />", content, StringComparison.Ordinal);
    }

    [Fact]
    public void NoticeReproducesThePinnedSnowballLicence()
    {
        string root = SnowballTransformTests.FindRepositoryRoot();
        string notice = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md")).ReplaceLineEndings("\n");
        string licence = File.ReadAllText(Path.Combine(root, "src", "devops", "tests", "fixtures", "snowball", "LICENSE-snowball.txt"))
            .ReplaceLineEndings("\n").Trim();

        Assert.Contains(licence, notice, StringComparison.Ordinal);
        Assert.Contains("v3.2.0", notice, StringComparison.Ordinal);
    }
}
