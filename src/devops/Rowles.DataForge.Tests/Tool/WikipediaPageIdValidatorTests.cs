using System.Text;
using Rowles.DataForge.Tool;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace Rowles.DataForge.Tests.Tool;

public sealed class WikipediaPageIdValidatorTests
{
    [Fact]
    public void Rejects_duplicate_page_ids_adjacent_in_source_order()
    {
        WithIndex(["0:11:first", "1:11:second"], (root, index) =>
        {
            Assert.Throws<InvalidDataException>(() => WikipediaPageIdValidator.Validate(index, root, runPageIdCapacity: 8,
                cancellationToken: TestContext.Current.CancellationToken));
            AssertNoTemporaryRuns(root);
        });
    }

    [Fact]
    public void Rejects_duplicate_page_ids_far_apart_within_one_run()
    {
        WithIndex(["0:11:first", "1:22:middle", "2:33:later", "3:11:last"], (root, index) =>
        {
            Assert.Throws<InvalidDataException>(() => WikipediaPageIdValidator.Validate(index, root, runPageIdCapacity: 8,
                cancellationToken: TestContext.Current.CancellationToken));
            AssertNoTemporaryRuns(root);
        });
    }

    [Fact]
    public void Rejects_duplicate_page_ids_across_external_sort_runs()
    {
        WithIndex(["0:11:first", "1:22:second", "2:33:third", "3:44:fourth", "4:55:fifth", "5:11:last"], (root, index) =>
        {
            Assert.Throws<InvalidDataException>(() => WikipediaPageIdValidator.Validate(index, root, runPageIdCapacity: 3,
                cancellationToken: TestContext.Current.CancellationToken));
            AssertNoTemporaryRuns(root);
        });
    }

    [Fact]
    public void Accepts_reversed_ids_and_reports_bounded_external_sort_metrics()
    {
        var lines = Enumerable.Range(1, 9).Reverse().Select((pageId, index) => $"{index}:{pageId}:Title:{pageId}").ToArray();
        WithIndex(lines, (root, index) =>
        {
            var result = WikipediaPageIdValidator.Validate(index, root, runPageIdCapacity: 3);

            Assert.Equal(9, result.EntriesScanned);
            Assert.Equal(72, result.TemporaryBytes);
            Assert.Equal(3, result.RunCount);
            Assert.True(result.PeakManagedBytes > 0);
            Assert.Equal(0, result.DuplicateCount);
            AssertNoTemporaryRuns(root);
        });
    }

    [Fact]
    public void Accepts_random_page_id_order()
    {
        var pageIds = new ulong[] { 7, 3, 9, 1, 6, 2, 8, 4, 5 };
        var lines = pageIds.Select((pageId, index) => $"{index}:{pageId}:Title:{pageId}").ToArray();
        WithIndex(lines, (root, index) =>
        {
            var result = WikipediaPageIdValidator.Validate(index, root, runPageIdCapacity: 4,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(pageIds.Length, result.EntriesScanned);
            Assert.Equal(0, result.DuplicateCount);
            AssertNoTemporaryRuns(root);
        });
    }

    [Fact]
    public void Cancellation_during_run_creation_cleans_temporary_files()
    {
        WithIndex(Enumerable.Range(1, 8).Select(id => $"{id - 1}:{id}:Title").ToArray(), (root, index) =>
        {
            using var cancellation = new CancellationTokenSource();
            var exception = Assert.ThrowsAny<OperationCanceledException>(() => WikipediaPageIdValidator.Validate(
                index,
                root,
                runPageIdCapacity: 2,
                cancellationToken: cancellation.Token,
                stageObserver: (stage, _) =>
                {
                    if (stage == WikipediaPageIdValidationStage.RunWritten)
                        cancellation.Cancel();
                }));

            Assert.Equal(cancellation.Token, exception.CancellationToken);
            AssertNoTemporaryRuns(root);
        });
    }

    [Fact]
    public void Cancellation_during_merge_cleans_temporary_files()
    {
        WithIndex(Enumerable.Range(1, 8).Select(id => $"{id - 1}:{id}:Title").ToArray(), (root, index) =>
        {
            using var cancellation = new CancellationTokenSource();
            Assert.ThrowsAny<OperationCanceledException>(() => WikipediaPageIdValidator.Validate(
                index,
                root,
                runPageIdCapacity: 2,
                cancellationToken: cancellation.Token,
                stageObserver: (stage, _) =>
                {
                    if (stage == WikipediaPageIdValidationStage.MergeStarted)
                        cancellation.Cancel();
                }));

            AssertNoTemporaryRuns(root);
        });
    }

    [Fact]
    public void Rejects_a_truncated_temporary_uint64_run_and_cleans_it()
    {
        WithIndex(["0:11:first", "1:22:second", "2:33:third"], (root, index) =>
        {
            var corrupted = false;
            Assert.Throws<InvalidDataException>(() => WikipediaPageIdValidator.Validate(
                index,
                root,
                runPageIdCapacity: 2,
                cancellationToken: TestContext.Current.CancellationToken,
                stageObserver: (stage, path) =>
                {
                    if (!corrupted && stage == WikipediaPageIdValidationStage.RunWritten && path is not null)
                    {
                        File.WriteAllBytes(path, new byte[sizeof(ulong) - 1]);
                        corrupted = true;
                    }
                }));

            Assert.True(corrupted);
            AssertNoTemporaryRuns(root);
        });
    }

    private static void WithIndex(string[] lines, Action<string, string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "dataforge-page-ids-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var index = Path.Combine(root, "index.txt.bz2");
        try
        {
            File.WriteAllBytes(index, Compress(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
            action(root, index);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertNoTemporaryRuns(string cacheDirectory) =>
        Assert.Empty(Directory.EnumerateDirectories(cacheDirectory, ".page-id-validation-*"));

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var compressor = BZip2Stream.Create(output, CompressionMode.Compress, decompressConcatenated: false, leaveOpen: true))
            compressor.Write(bytes);
        return output.ToArray();
    }
}
