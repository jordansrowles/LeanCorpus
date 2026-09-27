using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class StoredFieldsConcurrencyTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public StoredFieldsConcurrencyTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Stored Fields: reader keeps the compression implementation captured at open")]
    public void Reader_CapturesCompressionCodecAtOpen()
    {
        const string marker = "core056-reader-codec-snapshot";
        string path = Path.Combine(_fixture.Path, $"sf-codec-snapshot-{Guid.NewGuid():N}");
        List<Dictionary<string, List<string>>> docs =
        [
            new(StringComparer.Ordinal) { ["id"] = [marker] }
        ];
        StoredFieldsWriter.Write(
            path + ".fdt",
            path + ".fdx",
            docs,
            blockSize: 1,
            compression: FieldCompressionPolicy.Deflate,
            catalog: CodecCatalog.Default);

        var catalog = CodecCatalog.Default;
        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx", catalog);
        var originalCodec = catalog.GetCompressionCodec((byte)FieldCompressionPolicy.Deflate);
        var replacementCodec = new TrackingDecodeCodec(originalCodec);

        Assert.Throws<InvalidOperationException>(() => CompressionCodecRegistry.Register(replacementCodec));
        Assert.Throws<InvalidOperationException>(() => CompressionCodecRegistry.Replace(replacementCodec));
        var values = reader.ReadDocumentValues(0);

        Assert.False(replacementCodec.DecodeWasCalled,
            "An open reader must keep the implementation captured from its immutable codec catalogue.");
        Assert.Equal(marker, values["id"][0].StringValue);
    }

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

        var originalCodec = CodecCatalog.Default.GetCompressionCodec((byte)FieldCompressionPolicy.Deflate);
        using var barrierCodec = new ConcurrentDecodeBarrierCodec(originalCodec, Encoding.UTF8.GetBytes(marker));
        var catalog = new CodecCatalogBuilder()
            .AddBuiltIns()
            .ReplaceCompressionCodec(barrierCodec)
            .Build();
        StoredFieldsWriter.Write(
            path + ".fdt",
            path + ".fdx",
            docs,
            blockSize: 1,
            compression: FieldCompressionPolicy.Deflate,
            catalog: catalog);
        using (var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx", catalog))
        {
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
        var originalCodec = CodecCatalog.Default.GetCompressionCodec((byte)FieldCompressionPolicy.Deflate);
        var failOnceCodec = new FailOnceDecodeCodec(originalCodec, Encoding.UTF8.GetBytes(marker));
        var catalog = new CodecCatalogBuilder()
            .AddBuiltIns()
            .ReplaceCompressionCodec(failOnceCodec)
            .Build();
        StoredFieldsWriter.Write(
            path + ".fdt",
            path + ".fdx",
            docs,
            blockSize: 1,
            compression: FieldCompressionPolicy.Deflate,
            catalog: catalog);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx", catalog);
        Assert.Throws<InvalidDataException>(() => reader.ReadDocumentValues(0));
        var values = reader.ReadDocumentValues(0);
        Assert.Equal($"{marker}-0", values["id"][0].StringValue);
        Assert.Equal("retry-body", values["body"][0].StringValue);
    }

    [Fact(DisplayName = "Stored Fields: writer and reader round-trip codec output larger than twice the raw block")]
    public void WriteAndRead_ExpandingCodecOutput_RoundTrips()
    {
        const string marker = "core055-expanding-codec-round-trip";
        string path = Path.Combine(_fixture.Path, $"sf-expanding-compression-{Guid.NewGuid():N}");
        List<Dictionary<string, List<string>>> docs =
        [
            new(StringComparer.Ordinal) { ["id"] = [marker] }
        ];

        var originalCodec = CodecCatalog.Default.GetCompressionCodec((byte)FieldCompressionPolicy.Deflate);
        var expandingCodec = new ExpandingFieldCompressionCodec(originalCodec, Encoding.UTF8.GetBytes(marker));
        var catalog = new CodecCatalogBuilder()
            .AddBuiltIns()
            .ReplaceCompressionCodec(expandingCodec)
            .Build();
        StoredFieldsWriter.Write(
            path + ".fdt",
            path + ".fdx",
            docs,
            blockSize: 1,
            compression: FieldCompressionPolicy.Deflate,
            catalog: catalog);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx", catalog);
        var values = reader.ReadDocumentValues(0);

        Assert.True(expandingCodec.ExpandedBeyondTwiceRawLength);
        Assert.Equal(marker, values["id"][0].StringValue);
        Assert.True(reader.HasField(0, "id"));
    }

    [Fact(DisplayName = "Stored Fields: parallel immutable codec catalogues stay isolated across repeated reads")]
    public async Task ParallelCatalogs_CaptureIndependentCodecsAcrossRepeatedReads()
    {
        const int catalogueCount = 8;
        const int readsPerCatalogue = 24;
        string root = _fixture.Path;
        var originalCodec = CodecCatalog.Default.GetCompressionCodec((byte)FieldCompressionPolicy.Deflate);
        var cases = Enumerable.Range(0, catalogueCount)
            .Select(index =>
            {
                string marker = $"core056-independent-catalogue-{index}";
                string path = Path.Combine(root, $"sf-independent-{Guid.NewGuid():N}");
                var trackingCodec = new TrackingDecodeCodec(originalCodec);
                var catalog = new CodecCatalogBuilder()
                    .AddBuiltIns()
                    .ReplaceCompressionCodec(trackingCodec)
                    .Build();
                var docs = new List<Dictionary<string, List<string>>>
                {
                    new(StringComparer.Ordinal) { ["id"] = [marker] }
                };
                StoredFieldsWriter.Write(
                    path + ".fdt",
                    path + ".fdx",
                    docs,
                    blockSize: 1,
                    compression: FieldCompressionPolicy.Deflate,
                    catalog: catalog);
                return (Path: path, Marker: marker, Codec: trackingCodec, Catalog: catalog);
            })
            .ToArray();

        await Task.WhenAll(cases.Select(testCase => Task.Run(() =>
        {
            for (int read = 0; read < readsPerCatalogue; read++)
            {
                using var reader = StoredFieldsReader.Open(
                    testCase.Path + ".fdt", testCase.Path + ".fdx", testCase.Catalog);
                var values = reader.ReadDocumentValues(0);
                Assert.Equal(testCase.Marker, values["id"][0].StringValue);
            }
        }, TestContext.Current.CancellationToken)));

        foreach (var testCase in cases)
            Assert.Equal(readsPerCatalogue, testCase.Codec.DecodeCalls);
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

    private sealed class TrackingDecodeCodec(IFieldCompressionCodec inner) : IFieldCompressionCodec
    {
        private int _decodeCalls;

        public byte PolicyByte => inner.PolicyByte;

        internal bool DecodeWasCalled => Volatile.Read(ref _decodeCalls) != 0;
        internal int DecodeCalls => Volatile.Read(ref _decodeCalls);

        public byte[] Compress(ReadOnlySpan<byte> raw) => inner.Compress(raw);

        public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
        {
            Interlocked.Increment(ref _decodeCalls);
            return inner.Decompress(compressed, originalSize);
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

    private sealed class ExpandingFieldCompressionCodec(IFieldCompressionCodec inner, byte[] marker) : IFieldCompressionCodec
    {
        private const byte PaddingByte = 0xA5;
        private static ReadOnlySpan<byte> Header => "SFEXP001"u8;
        private int _expandedBeyondTwiceRawLength;

        public byte PolicyByte => inner.PolicyByte;

        internal bool ExpandedBeyondTwiceRawLength => Volatile.Read(ref _expandedBeyondTwiceRawLength) != 0;

        public byte[] Compress(ReadOnlySpan<byte> raw)
        {
            if (raw.IndexOf(marker) < 0)
                return inner.Compress(raw);

            int compressedLength = checked(Header.Length + checked(raw.Length * 3));
            var compressed = new byte[compressedLength];
            Header.CopyTo(compressed);
            raw.CopyTo(compressed.AsSpan(Header.Length));
            compressed.AsSpan(Header.Length + raw.Length).Fill(PaddingByte);
            Volatile.Write(ref _expandedBeyondTwiceRawLength, compressedLength > (long)raw.Length * 2 ? 1 : 0);
            return compressed;
        }

        public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
        {
            if (!compressed.StartsWith(Header))
                return inner.Decompress(compressed, originalSize);

            int expectedLength = checked(Header.Length + checked(originalSize * 3));
            if (compressed.Length != expectedLength ||
                compressed[(Header.Length + originalSize)..].IndexOfAnyExcept(PaddingByte) >= 0)
                throw new InvalidDataException("Expanding test codec received an invalid payload.");

            return compressed.Slice(Header.Length, originalSize).ToArray();
        }
    }
}
