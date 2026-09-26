using System.Collections.Concurrent;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Index.Indexer;

namespace Rowles.LeanCorpus.Tests.Core.Index.Indexer;

[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class StoredFieldBlockSizeValidationTests
{
    [Fact(DisplayName = "IndexWriterConfig: stored-field block size accepts both shared range endpoints")]
    public void Validate_StoredFieldBlockSize_AcceptsRangeEndpoints()
    {
        foreach (int blockSize in new[] { 1, StoredFieldsBlockPolicy.MaximumDocumentCount })
        {
            var config = new IndexWriterConfig { StoredFieldBlockSize = blockSize };
            config.Validate();
        }
    }

    [Fact(DisplayName = "IndexWriterConfig: stored-field block size rejects values outside the shared range")]
    public void Validate_StoredFieldBlockSize_RejectsOutsideRange()
    {
        foreach (int blockSize in new[] { 0, StoredFieldsBlockPolicy.MaximumDocumentCount + 1 })
        {
            var config = new IndexWriterConfig { StoredFieldBlockSize = blockSize };
            var error = Assert.Throws<ArgumentOutOfRangeException>(config.Validate);
            Assert.Equal(nameof(IndexWriterConfig.StoredFieldBlockSize), error.ParamName);
        }
    }

    [Fact(DisplayName = "IndexWriterConfig: parallel block-size validation remains isolated and deterministic")]
    public void Validate_StoredFieldBlockSize_InParallel_RemainsIsolated()
    {
        int maximumDocumentCount = StoredFieldsBlockPolicy.MaximumDocumentCount;
        var configs = new[]
        {
            new IndexWriterConfig { StoredFieldBlockSize = 1 },
            new IndexWriterConfig { StoredFieldBlockSize = maximumDocumentCount },
            new IndexWriterConfig { StoredFieldBlockSize = 0 },
            new IndexWriterConfig { StoredFieldBlockSize = maximumDocumentCount + 1 }
        };
        var failures = new ConcurrentQueue<string>();

        Parallel.For(0, 1024, iteration =>
        {
            int configIndex = iteration % configs.Length;
            bool shouldThrow = configIndex >= 2;
            try
            {
                configs[configIndex].Validate();
                if (shouldThrow)
                    failures.Enqueue($"Invalid block size at config index {configIndex} was accepted.");
            }
            catch (ArgumentOutOfRangeException error) when (shouldThrow)
            {
                if (error.ParamName != nameof(IndexWriterConfig.StoredFieldBlockSize))
                    failures.Enqueue($"Unexpected argument name for config index {configIndex}: {error.ParamName}.");
            }
            catch (Exception error)
            {
                failures.Enqueue($"Unexpected error for config index {configIndex}: {error.GetType().Name}.");
            }
        });

        Assert.Empty(failures);
    }
}
