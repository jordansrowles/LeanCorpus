using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Analysis.Tokenisers.Japanese;

namespace Rowles.LeanCorpus.Tests.Core;

[Category(TestCategory.Unit)]
[Area(TestArea.TextIntegration)]
public sealed class JapaneseDictionaryOwnershipTests
{
    [Fact(DisplayName = "JapaneseDictionary: Dispose closes the Core memory-mapped codec")]
    public void Dispose_ClosesMappedCodecAndInvalidatesTokeniser()
    {
        string path = Path.Combine(Path.GetTempPath(), $"japanese_core_owner_{Guid.NewGuid():N}.jlc");
        File.Copy(JapaneseTokeniser.DefaultDictionaryPath, path);

        try
        {
            var dictionary = new JapaneseDictionary(path);
            var tokeniser = new JapaneseTokeniser(dictionary);
            var sink = new CountingTokenSink();

            tokeniser.Tokenise("\u79C1\u306F\u5B66\u751F\u3067\u3059", sink);
            Assert.Equal(4, sink.Count);
            Assert.Throws<IOException>(() =>
            {
                using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            });

            dictionary.Dispose();

            using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.True(exclusive.CanRead);
            Assert.Throws<ObjectDisposedException>(() => tokeniser.Tokenise("\u79C1", new CountingTokenSink()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class CountingTokenSink : ISpanTokenSink
    {
        internal int Count { get; private set; }

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            Count++;
        }
    }
}
