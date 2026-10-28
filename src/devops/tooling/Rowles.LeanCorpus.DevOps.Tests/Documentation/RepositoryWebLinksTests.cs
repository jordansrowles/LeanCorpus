using Rowles.LeanCorpus.DevOps.Documentation;

namespace Rowles.LeanCorpus.DevOps.Tests.Documentation;

public sealed class RepositoryWebLinksTests
{
    [Fact]
    public void BuildsCanonicalRepositoryCommitAndBlobLinks()
    {
        Assert.Equal("https://github.com/jordansrowles/LeanCorpus", RepositoryWebLinks.RepositoryUrl);
        Assert.Equal("https://github.com/jordansrowles/LeanCorpus/commit/abcdef0123",
            RepositoryWebLinks.CommitUrl("ABCDEF0123"));
        Assert.Equal("https://github.com/jordansrowles/LeanCorpus/blob/abcdef0123/docs/source page.md",
            RepositoryWebLinks.BlobUrl("ABCDEF0123", "docs/source page.md"));
        Assert.Equal("https://github.com/jordansrowles/LeanCorpus/blob/abcdef0123/",
            RepositoryWebLinks.BlobUrlPrefix("ABCDEF0123"));
    }
}
