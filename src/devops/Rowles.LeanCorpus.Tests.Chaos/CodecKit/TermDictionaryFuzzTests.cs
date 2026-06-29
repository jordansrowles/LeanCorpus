using System.Buffers;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.CodecKit;

[Trait("Category", "Chaos")]
[Trait("Category", "CodecKit")]
public sealed class TermDictionaryFuzzTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;
    public TermDictionaryFuzzTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 200)]
    public void WriteRead_RoundTrip_SortedTerms(string[] terms)
    {
        if (terms.Length == 0) return;

        var distinct = terms.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count < 2) return;

        distinct.Sort(StringComparer.Ordinal);

        var offsets = new Dictionary<string, long>(distinct.Count, StringComparer.Ordinal);
        for (int i = 0; i < distinct.Count; i++)
            offsets[distinct[i]] = i * 100L + 16;

        string basePath = System.IO.Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N"));
        TermDictionaryWriter.Write(basePath + ".dic", distinct, offsets);

        Assert.True(File.Exists(basePath + ".dic"), "Dictionary file not written");

        var lookup = TermDictionaryReader.Open(basePath + ".dic");
        Assert.NotNull(lookup);

        int found = 0;
        foreach (string term in distinct)
        {
            bool ok = lookup.TryGetPostingsOffset(term, out long offset);
            Assert.True(ok && offset >= 0, $"Term '{term}' not found");
            found++;
        }

        Assert.Equal(distinct.Count, found);
    }

    [Property(MaxTest = 200)]
    public void MissingTerm_ReturnsNegative(string[] terms, string missingTerm)
    {
        if (terms.Length == 0 || string.IsNullOrWhiteSpace(missingTerm)) return;

        var distinct = terms.Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .Where(t => t != missingTerm)
            .ToList();

        if (distinct.Count < 2) return;

        distinct.Sort(StringComparer.Ordinal);

        var offsets = new Dictionary<string, long>(distinct.Count, StringComparer.Ordinal);
        for (int i = 0; i < distinct.Count; i++)
            offsets[distinct[i]] = i * 100L + 16;

        string basePath = System.IO.Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N"));
        TermDictionaryWriter.Write(basePath + ".dic", distinct, offsets);

        var lookup = TermDictionaryReader.Open(basePath + ".dic");
        bool ok = lookup.TryGetPostingsOffset(missingTerm, out long offset);
        Assert.True(!ok || offset < 0, $"Missing term '{missingTerm}' unexpectedly found at offset {offset}");
    }
}
