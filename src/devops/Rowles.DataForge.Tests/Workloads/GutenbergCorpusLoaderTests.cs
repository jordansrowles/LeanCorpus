using Rowles.DataForge.Workloads.Legacy;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class GutenbergCorpusLoaderTests
{
    [Fact]
    public void Explicit_directory_loads_and_strips_reference_text()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dataforge-gutenberg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "11.txt"),
                "*** START OF THE PROJECT GUTENBERG EBOOK\n" +
                "A long opening paragraph with enough text to meet the fifty character paragraph requirement exactly.\n\n" +
                "A second long paragraph with enough text to meet the fifty character paragraph requirement.\n" +
                "*** END OF THE PROJECT GUTENBERG EBOOK\n");

            var paragraphs = GutenbergCorpusLoader.Load(directory);
            Assert.Equal(2, paragraphs.Length);
            Assert.Equal("11-0", paragraphs[0].Id);
            Assert.Equal("Alice's Adventures in Wonderland", paragraphs[0].Title);
            Assert.DoesNotContain("PROJECT GUTENBERG", paragraphs[0].Body, StringComparison.Ordinal);

            var books = GutenbergCorpusLoader.LoadBookTexts(directory);
            Assert.Single(books);
            Assert.Equal("Alice's Adventures in Wonderland", books[0].Title);
            Assert.DoesNotContain("PROJECT GUTENBERG", books[0].Text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
