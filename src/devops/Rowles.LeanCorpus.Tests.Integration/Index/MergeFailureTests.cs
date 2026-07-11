using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

[Trait("Category", "Index")]
[Trait("Category", "Merge")]
public sealed class MergeFailureTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public MergeFailureTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact(DisplayName = "Background merge failure marks writer failed and gates next AddDocument")]
    public async Task BackgroundMergeFailure_MarksWriterFailed()
    {
        var dirPath = SubDir("bg_merge_fail");
        var dir = new MMapDirectory(dirPath);

        // TieredMergePolicy(2) ensures 2+ segments trigger a merge.
        var config = new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = new TieredMergePolicy(2) };

        using (var writer = new IndexWriter(dir, config))
        {
            // Create 2 segments: MaxBufferedDocs=1 flushes after each doc.
            writer.AddDocument(MakeDoc("doc-0", "hello world first"));
            writer.AddDocument(MakeDoc("doc-1", "hello world second"));

            // Corrupt the first segment before commit triggers the merge.
            var seg0Dic = Path.Combine(dirPath, "seg_0.dic");
            Assert.True(File.Exists(seg0Dic), "Expected seg_0.dic to exist before corruption.");
            File.Delete(seg0Dic);

            // Commit triggers ScheduleBackgroundMerge. The merge runs on a thread-pool
            // thread, fails because seg_0.dic is missing, and marks the writer failed.
            writer.Commit();

            // Wait for the background merge to complete (or fault).
            var task = writer.MergeTask;
            if (task is not null)
            {
                try { await task.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch (Exception) { /* expected: merge failed */ }
            }

            // Writer should now be gated.
            var ex = Assert.Throws<InvalidOperationException>(() =>
                writer.AddDocument(MakeDoc("doc-2", "should be gated")));
            Assert.Contains("unusable", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static LeanDocument MakeDoc(string id, string body)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", body));
        doc.Add(new StringField("id", id));
        return doc;
    }
}
