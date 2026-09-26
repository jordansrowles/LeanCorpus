using System.Text;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class StoredFieldsConcurrencyTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public StoredFieldsConcurrencyTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Stored Fields: independent document reads decompress concurrently and remain isolated")]
    public async Task ConcurrentReads_UseIndependentCursorsAndPreserveValues()
    {
        const string marker = "core052-parallel-decompression-regression";
        string path = Path.Combine(_fixture.Path, $"sf-concurrent-{Guid.NewGuid():N}");
        List<Dictionary<string, List<string>>> docs = Enumerable.Range(0, 4)
            .Select(docId =>
            {
                var fields = new Dictionary<string, List<string>>(StringComparer.Ordinal)
                {
                    ["id"] = [$"{marker}-{docId}"]
                };
                if (docId != 2)
                    fields["body"] = [$"stored-body-{docId}"];
                return fields;
            })
            .ToList();

        StoredFieldsWriter.Write(
            path + ".fdt",
            path + ".fdx",
            docs,
            blockSize: 1,
            compression: FieldCompressionPolicy.Deflate);

        var originalCodec = CompressionCodecRegistry.Get(FieldCompressionPolicy.Deflate);
        using var barrierCodec = new ConcurrentDecodeBarrierCodec(originalCodec, Encoding.UTF8.GetBytes(marker));
        CompressionCodecRegistry.Register(barrierCodec);
        try
        {
            using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
            using var start = new Barrier(3);
            Task<Dictionary<string, List<StoredFieldValue>>> read = Task.Factory.StartNew(
                () =>
                {
                    start.SignalAndWait(TestContext.Current.CancellationToken);
                    return reader.ReadDocumentValues(0);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Task<bool> hasField = Task.Factory.StartNew(
                () =>
                {
                    start.SignalAndWait(TestContext.Current.CancellationToken);
                    return reader.HasField(1, "body");
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            start.SignalAndWait(TestContext.Current.CancellationToken);
            await Task.WhenAll(read, hasField).WaitAsync(TestContext.Current.CancellationToken);
            var readValues = await read;
            bool hasBodyField = await hasField;

            Assert.True(barrierCodec.MaximumConcurrentDecodes >= 2,
                "Stored-field blocks were decoded serially through the shared reader lock.");
            Assert.Equal($"{marker}-0", readValues["id"][0].StringValue);
            Assert.Equal("stored-body-0", readValues["body"][0].StringValue);
            Assert.True(hasBodyField);
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadDocumentValues(-1));
            Assert.Equal($"{marker}-3", reader.ReadDocumentValues(3)["id"][0].StringValue);

            Parallel.For(0, 512, operation =>
            {
                int docId = operation % docs.Count;
                var values = reader.ReadDocumentValues(docId);
                Assert.Equal($"{marker}-{docId}", values["id"][0].StringValue);
                if (docId != 2)
                    Assert.Equal($"stored-body-{docId}", values["body"][0].StringValue);
                Assert.Equal(docId != 2, reader.HasField(docId, "body"));
            });

            reader.Dispose();
            Assert.Throws<ObjectDisposedException>(() => reader.ReadDocumentValues(0));
        }
        finally
        {
            CompressionCodecRegistry.Register(originalCodec);
        }
    }

    [Fact(DisplayName = "Stored Fields: a failed block decode does not poison later reads")]
    public void FailedBlockDecode_DoesNotPoisonReaderCache()
    {
        const string marker = "core052-failed-decompression-retry";
        string path = Path.Combine(_fixture.Path, $"sf-retry-{Guid.NewGuid():N}");
        List<Dictionary<string, List<string>>> docs =
        [
            new(StringComparer.Ordinal)
            {
                ["id"] = [$"{marker}-0"],
                ["body"] = ["retry-body"]
            }
        ];
        StoredFieldsWriter.Write(
            path + ".fdt",
            path + ".fdx",
            docs,
            blockSize: 1,
            compression: FieldCompressionPolicy.Deflate);

        var originalCodec = CompressionCodecRegistry.Get(FieldCompressionPolicy.Deflate);
        CompressionCodecRegistry.Register(new FailOnceDecodeCodec(originalCodec, Encoding.UTF8.GetBytes(marker)));
        try
        {
            using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
            Assert.Throws<InvalidDataException>(() => reader.ReadDocumentValues(0));
            var values = reader.ReadDocumentValues(0);
            Assert.Equal($"{marker}-0", values["id"][0].StringValue);
            Assert.Equal("retry-body", values["body"][0].StringValue);
        }
        finally
        {
            CompressionCodecRegistry.Register(originalCodec);
        }
    }

    private sealed class ConcurrentDecodeBarrierCodec : IFieldCompressionCodec, IDisposable
    {
        private readonly IFieldCompressionCodec _inner;
        private readonly byte[] _marker;
        private readonly CountdownEvent _entered = new(2);
        private int _activeDecodes;
        private int _maximumConcurrentDecodes;

        internal ConcurrentDecodeBarrierCodec(IFieldCompressionCodec inner, byte[] marker)
        {
            _inner = inner;
            _marker = marker;
        }

        public byte PolicyByte => _inner.PolicyByte;

        internal int MaximumConcurrentDecodes => Volatile.Read(ref _maximumConcurrentDecodes);

        public byte[] Compress(ReadOnlySpan<byte> raw) => _inner.Compress(raw);

        public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
        {
            byte[] raw = _inner.Decompress(compressed, originalSize);
            if (raw.AsSpan().IndexOf(_marker) < 0)
                return raw;

            int active = Interlocked.Increment(ref _activeDecodes);
            UpdateMaximum(active);
            if (_entered.CurrentCount > 0)
            {
                _entered.Signal();
                _entered.Wait(TimeSpan.FromSeconds(5));
            }
            Interlocked.Decrement(ref _activeDecodes);
            return raw;
        }

        public void Dispose() => _entered.Dispose();

        private void UpdateMaximum(int value)
        {
            int observed;
            while ((observed = Volatile.Read(ref _maximumConcurrentDecodes)) < value
                && Interlocked.CompareExchange(ref _maximumConcurrentDecodes, value, observed) != observed)
            {
            }
        }
    }

    private sealed class FailOnceDecodeCodec(IFieldCompressionCodec inner, byte[] marker) : IFieldCompressionCodec
    {
        private int _shouldFail = 1;

        public byte PolicyByte => inner.PolicyByte;

        public byte[] Compress(ReadOnlySpan<byte> raw) => inner.Compress(raw);

        public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
        {
            byte[] raw = inner.Decompress(compressed, originalSize);
            if (raw.AsSpan().IndexOf(marker) >= 0 && Interlocked.Exchange(ref _shouldFail, 0) == 1)
                throw new InvalidDataException("Injected stored-field decompression failure.");
            return raw;
        }
    }
}
