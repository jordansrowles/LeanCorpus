using System.Globalization;
using System.Text;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

[Trait("Category", "Index")]
[Trait("Category", "Migration")]
public sealed class IndexCodecMigratorTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexCodecMigratorTests(TestDirectoryFixture fixture) => _fixture = fixture;

    public static IEnumerable<object[]> CodecCases()
    {
        yield return [new MigrationCodecCase("term-dictionary", [".dic"], 1, directory => RewriteTermDictionaryAsV1(Directory.GetFiles(directory.DirectoryPath, "*.dic").Single()))];
        yield return [new MigrationCodecCase("postings", [".pos"], 2, RewritePostingsAsV2)];
        yield return [new MigrationCodecCase("numeric-doc-values", [".dvn"], 1, RewriteNumericDocValuesAsV1)];
        yield return [new MigrationCodecCase("sorted-doc-values", [".dvs"], 1, RewriteSortedDocValuesAsV1)];
        yield return [new MigrationCodecCase("sorted-set-doc-values", [".dss"], 0, directory => WriteCodecVersion(directory, "*.dss", 0))];
        yield return [new MigrationCodecCase("sorted-numeric-doc-values", [".dsn"], 0, directory => WriteCodecVersion(directory, "*.dsn", 0))];
        yield return [new MigrationCodecCase("binary-doc-values", [".dvb"], 0, directory => WriteCodecVersion(directory, "*.dvb", 0))];
        yield return [new MigrationCodecCase("field-lengths", [".fln"], 1, RewriteFieldLengthsAsV1)];
        yield return [new MigrationCodecCase("stored-fields", [".fdt", ".fdx"], 4, RewriteStoredFieldsAsV4)];
    }

    [Theory]
    [MemberData(nameof(CodecCases))]
    public void Plan_LegacyCodec_ReturnsExecutableRewrite(MigrationCodecCase testCase)
    {
        using var directory = CreateMigrationIndex($"plan_{testCase.Name}", out _);
        testCase.Downgrade(directory);

        var plan = IndexCodecMigrator.Plan(directory);

        Assert.True(plan.CanExecute, string.Join(Environment.NewLine, plan.Issues.Select(static issue => issue.ToString())));
        foreach (var extension in testCase.ExpectedExtensions)
        {
            Assert.Contains(
                plan.Actions,
                action =>
                    action.Kind == IndexCodecMigrationActionKind.RewriteFile &&
                    action.SourcePath.EndsWith(extension, StringComparison.Ordinal) &&
                    action.CanExecute &&
                    action.FromVersion == testCase.LegacyVersion &&
                    action.ToVersion is not null);
        }
    }

    [Theory]
    [MemberData(nameof(CodecCases))]
    public void Migrate_LegacyCodec_ChecksAndReads(MigrationCodecCase testCase)
    {
        using var directory = CreateMigrationIndex($"migrate_{testCase.Name}", out var expected);
        testCase.Downgrade(directory);
        AssertReadable(directory, expected);
        var stagingDirectory = Path.Combine(_fixture.Path, $"migrate_{testCase.Name}_staging_{Guid.NewGuid():N}");

        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
        {
            DryRun = false,
            StagingDirectory = stagingDirectory
        });

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.ToString())));
        foreach (var extension in testCase.ExpectedExtensions)
        {
            Assert.Contains(result.ExecutedActions, action => action.SourcePath.EndsWith(extension, StringComparison.Ordinal));
            AssertCurrentVersion(directory, extension);
        }
        Assert.Equal(IndexMigrationState.Published, IndexMigrationRecovery.GetState(directory.DirectoryPath).State);
        Assert.False(Directory.Exists(stagingDirectory));
        Assert.Equal(IndexCompatibilityStatus.Compatible, IndexCompatibility.Check(directory).Status);
        AssertReadable(directory, expected);
    }

    [Fact]
    public void Migrate_InPlaceLegacyCodec_LeavesNoTemporaryCodecFiles()
    {
        using var directory = CreateMigrationIndex("migrate_in_place_temp", out var expected);
        RewritePostingsAsV2(directory);

        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
        {
            DryRun = false,
            UseStagingDirectory = false,
            AllowInPlaceMigration = true
        });

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.ToString())));
        Assert.DoesNotContain(Directory.GetFiles(directory.DirectoryPath, "*.tmp"), path => path.EndsWith(".pos.tmp", StringComparison.Ordinal));
        Assert.DoesNotContain(Directory.GetFiles(directory.DirectoryPath, "*.tmp"), path => path.EndsWith(".dic.tmp", StringComparison.Ordinal));
        Assert.Equal(IndexCompatibilityStatus.Compatible, IndexCompatibility.Check(directory).Status);
        AssertReadable(directory, expected);
    }

    [Theory]
    [MemberData(nameof(CodecCases))]
    public void Migrate_DryRun_LeavesSourceCodecUnchanged(MigrationCodecCase testCase)
    {
        using var directory = CreateMigrationIndex($"dry_run_{testCase.Name}", out var expected);
        testCase.Downgrade(directory);
        var versionsBefore = CaptureVersions(directory, testCase.ExpectedExtensions);

        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = true });

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.ToString())));
        Assert.True(result.DryRun);
        Assert.NotEmpty(result.ExecutedActions);
        Assert.Equal(versionsBefore, CaptureVersions(directory, testCase.ExpectedExtensions));
        AssertReadable(directory, expected);
    }

    [Fact]
    public void Migrate_AlreadyCurrentIndex_ReturnsNoActionsAndReads()
    {
        using var directory = CreateMigrationIndex("migrate_current", out var expected);

        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false });

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.ToString())));
        Assert.Empty(result.ExecutedActions);
        Assert.Equal(IndexCompatibilityStatus.Compatible, IndexCompatibility.Check(directory).Status);
        AssertReadable(directory, expected);
    }

    [Fact]
    public void Migrate_SecondRunAfterMigration_ReturnsNoActionsAndReads()
    {
        using var directory = CreateMigrationIndex("migrate_second_run", out var expected);
        RewritePostingsAsV2(directory);
        var stagingDirectory = Path.Combine(_fixture.Path, $"migrate_second_run_staging_{Guid.NewGuid():N}");
        var first = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
        {
            DryRun = false,
            StagingDirectory = stagingDirectory
        });
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Issues.Select(static issue => issue.ToString())));

        var second = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false });

        Assert.True(second.Succeeded, string.Join(Environment.NewLine, second.Issues.Select(static issue => issue.ToString())));
        Assert.Empty(second.ExecutedActions);
        Assert.Equal(IndexCompatibilityStatus.Compatible, IndexCompatibility.Check(directory).Status);
        AssertReadable(directory, expected);
    }

    private MMapDirectory CreateMigrationIndex(string name, out IReadOnlyList<ExpectedDocument> expected)
    {
        var path = Path.Combine(_fixture.Path, $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());

        var documents = new[]
        {
            new ExpectedDocument("doc-1", "migration alpha body", "alpha", 10.5d, "source-a"),
            new ExpectedDocument("doc-2", "migration beta body", "beta", 20.25d, "source-b"),
            new ExpectedDocument("doc-3", "migration gamma body", "gamma", 30.75d, "source-c")
        };

        foreach (var item in documents)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", item.Id, stored: true));
            document.Add(new TextField("body", item.Body, stored: true));
            document.Add(new StringField("category", item.Category, stored: true));
            document.Add(new StringField("tags", item.Category, stored: true));
            document.Add(new StringField("tags", "migration", stored: true));
            document.Add(new NumericField("number", item.Number, stored: true));
            document.Add(new NumericField("scores", item.Number, stored: true));
            document.Add(new NumericField("scores", item.Number + 1, stored: true));
            document.Add(new StoredField("source", item.Source));
            document.Add(new StoredField("notes", item.Source));
            document.Add(new StoredField("notes", "migration"));
            writer.AddDocument(document);
        }

        writer.Commit();
        expected = documents;
        AssertRequiredMigrationFiles(directory);
        return directory;
    }

    private static void AssertRequiredMigrationFiles(MMapDirectory directory)
    {
        string[] extensions = [".dic", ".pos", ".dvn", ".dvs", ".dss", ".dsn", ".dvb", ".fln", ".fdt", ".fdx"];
        foreach (var extension in extensions)
            Assert.Single(Directory.GetFiles(directory.DirectoryPath, "*" + extension));
    }

    private static void AssertReadable(MMapDirectory directory, IReadOnlyList<ExpectedDocument> expected)
    {
        using var searcher = new IndexSearcher(directory);

        var results = searcher.Search(new TermQuery("body", "migration"), expected.Count);

        Assert.Equal(expected.Count, results.TotalHits);
        var expectedById = expected.ToDictionary(static item => item.Id, StringComparer.Ordinal);
        var actualIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scoreDoc in results.ScoreDocs)
        {
            var stored = searcher.GetStoredFields(scoreDoc.DocId);
            var id = SingleStoredValue(stored, "id");
            var item = expectedById[id];
            actualIds.Add(id);
            Assert.Equal(item.Body, SingleStoredValue(stored, "body"));
            Assert.Equal(item.Category, SingleStoredValue(stored, "category"));
            Assert.Equal(item.Number.ToString(CultureInfo.InvariantCulture), SingleStoredValue(stored, "number"));
            Assert.Equal(item.Source, SingleStoredValue(stored, "source"));
        }

        Assert.Equal(expectedById.Keys.Order(StringComparer.Ordinal), actualIds.Order(StringComparer.Ordinal));
    }

    private static string SingleStoredValue(IReadOnlyDictionary<string, IReadOnlyList<string>> stored, string fieldName)
    {
        var values = Assert.Contains(fieldName, stored);
        return Assert.Single(values);
    }

    private static Dictionary<string, byte> CaptureVersions(MMapDirectory directory, IReadOnlyList<string> extensions)
    {
        var versions = new Dictionary<string, byte>(StringComparer.Ordinal);
        foreach (var extension in extensions)
        {
            foreach (var path in Directory.GetFiles(directory.DirectoryPath, "*" + extension).Order(StringComparer.Ordinal))
                versions[Path.GetFileName(path)] = ReadCodecVersion(path);
        }

        return versions;
    }

    private static void AssertCurrentVersion(MMapDirectory directory, string extension)
    {
        var expectedVersion = CurrentVersionFor(extension);
        var files = Directory.GetFiles(directory.DirectoryPath, "*" + extension);
        Assert.NotEmpty(files);
        foreach (var path in files)
            Assert.Equal(expectedVersion, ReadCodecVersion(path));
    }

    private static byte CurrentVersionFor(string extension)
        => extension switch
        {
            ".dic" => CodecConstants.TermDictionaryVersion,
            ".pos" => CodecConstants.PostingsVersion,
            ".dvn" => CodecConstants.NumericDocValuesVersion,
            ".dvs" => CodecConstants.SortedDocValuesVersion,
            ".dss" => CodecConstants.SortedSetDocValuesVersion,
            ".dsn" => CodecConstants.SortedNumericDocValuesVersion,
            ".dvb" => CodecConstants.BinaryDocValuesVersion,
            ".fln" => CodecConstants.FieldLengthVersion,
            ".fdt" => CodecConstants.StoredFieldsVersion,
            ".fdx" => CodecConstants.StoredFieldsVersion,
            _ => throw new ArgumentOutOfRangeException(nameof(extension), extension, "Unknown codec extension.")
        };

    private static byte ReadCodecVersion(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        Assert.Equal(CodecConstants.Magic, reader.ReadInt32());
        return reader.ReadByte();
    }

    private static void WriteCodecVersion(MMapDirectory directory, string pattern, byte version)
    {
        var path = Directory.GetFiles(directory.DirectoryPath, pattern).Single();
        using var stream = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = sizeof(int);
        stream.WriteByte(version);
    }

    private static void RewriteTermDictionaryAsV1(string path)
    {
        using var reader = TermDictionaryReader.Open(path);
        var terms = reader.EnumerateAllTerms();

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        CodecConstants.WriteHeader(writer, 1);
        writer.Write(0);
        foreach (var (term, offset) in terms)
        {
            var bytes = Encoding.UTF8.GetBytes(term);
            writer.Write(bytes.Length);
            writer.Write(bytes);
            writer.Write(offset);
        }
    }

    private static void RewritePostingsAsV2(MMapDirectory directory)
    {
        var dicPath = Directory.GetFiles(directory.DirectoryPath, "*.dic").Single();
        var posPath = Directory.GetFiles(directory.DirectoryPath, "*.pos").Single();
        List<(string Term, List<PostingRow> Rows)> terms;
        using (var dictionary = TermDictionaryReader.Open(dicPath))
        using (var input = new IndexInput(posPath))
        {
            byte version = PostingsEnum.ValidateFileHeader(input);
            terms = dictionary
                .EnumerateAllTerms()
                .Select(term => (term.Term, ReadRows(input, term.Offset, version)))
                .ToList();
        }

        var offsets = new Dictionary<string, long>(terms.Count, StringComparer.Ordinal);
        using (var stream = File.Create(posPath))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
        {
            CodecConstants.WriteHeader(writer, 2);
            foreach (var (term, rows) in terms)
            {
                offsets[term] = stream.Position;
                WriteV2Rows(writer, rows);
            }
        }

        TermDictionaryWriter.Write(dicPath, terms.Select(static term => term.Term).ToList(), offsets);
    }

    private static void RewriteNumericDocValuesAsV1(MMapDirectory directory)
    {
        var path = Directory.GetFiles(directory.DirectoryPath, "*.dvn").Single();
        var (values, _) = NumericDocValuesReader.Read(path);
        using var output = new IndexOutput(path);
        CodecConstants.WriteHeader(output, 1);
        output.WriteInt32(values.Count);
        foreach (var (fieldName, fieldValues) in values)
            WriteNumericDocValuesFieldV1(output, fieldName, fieldValues);
    }

    private static void WriteNumericDocValuesFieldV1(IndexOutput output, string fieldName, double[] values)
    {
        var nameBytes = Encoding.UTF8.GetBytes(fieldName);
        output.WriteVarInt(nameBytes.Length);
        output.WriteBytes(nameBytes);
        output.WriteInt32(values.Length);

        long min = long.MaxValue;
        long max = long.MinValue;
        foreach (var value in values)
        {
            var bits = BitConverter.DoubleToInt64Bits(value);
            if (bits < min) min = bits;
            if (bits > max) max = bits;
        }

        output.WriteInt64(min);
        var range = (ulong)max - (ulong)min;
        var bitsPerValue = range == 0 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount(range);
        output.WriteByte((byte)bitsPerValue);
        if (bitsPerValue == 0)
            return;

        byte accum = 0;
        int accBits = 0;
        foreach (var value in values)
        {
            var delta = (ulong)BitConverter.DoubleToInt64Bits(value) - (ulong)min;
            var remaining = bitsPerValue;
            while (remaining > 0)
            {
                var space = 8 - accBits;
                var take = Math.Min(remaining, space);
                accum |= (byte)((delta & ((1UL << take) - 1)) << accBits);
                delta >>= take;
                accBits += take;
                remaining -= take;
                if (accBits == 8)
                {
                    output.WriteByte(accum);
                    accum = 0;
                    accBits = 0;
                }
            }
        }

        if (accBits > 0)
            output.WriteByte(accum);
    }

    private static void RewriteSortedDocValuesAsV1(MMapDirectory directory)
    {
        var path = Directory.GetFiles(directory.DirectoryPath, "*.dvs").Single();
        var (values, _) = SortedDocValuesReader.Read(path);
        using var output = new IndexOutput(path);
        CodecConstants.WriteHeader(output, 1);
        output.WriteInt32(values.Count);
        foreach (var (fieldName, fieldValues) in values)
            WriteSortedDocValuesFieldV1(output, fieldName, fieldValues);
    }

    private static void WriteSortedDocValuesFieldV1(IndexOutput output, string fieldName, string[] values)
    {
        var nameBytes = Encoding.UTF8.GetBytes(fieldName);
        output.WriteVarInt(nameBytes.Length);
        output.WriteBytes(nameBytes);
        output.WriteInt32(values.Length);

        var ordList = values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var ordMap = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < ordList.Length; i++)
            ordMap[ordList[i]] = i;

        output.WriteInt32(ordList.Length);
        foreach (var ord in ordList)
        {
            var bytes = Encoding.UTF8.GetBytes(ord);
            output.WriteVarInt(bytes.Length);
            output.WriteBytes(bytes);
        }

        var bitsPerOrd = ordList.Length <= 1 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)(ordList.Length - 1));
        output.WriteByte((byte)bitsPerOrd);
        if (bitsPerOrd == 0)
            return;

        ulong buffer = 0;
        int bitsInBuffer = 0;
        foreach (var value in values)
        {
            buffer |= (ulong)ordMap[value] << bitsInBuffer;
            bitsInBuffer += bitsPerOrd;
            while (bitsInBuffer >= 8)
            {
                output.WriteByte((byte)(buffer & 0xFF));
                buffer >>= 8;
                bitsInBuffer -= 8;
            }
        }

        if (bitsInBuffer > 0)
            output.WriteByte((byte)(buffer & 0xFF));
    }

    private static void RewriteFieldLengthsAsV1(MMapDirectory directory)
    {
        var path = Directory.GetFiles(directory.DirectoryPath, "*.fln").Single();
        var values = FieldLengthReader.TryRead(path) ?? throw new InvalidDataException("Field lengths file was not readable.");
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        CodecConstants.WriteHeader(writer, 1);
        writer.Write(values.Count);
        foreach (var (fieldName, lengths) in values)
        {
            var nameBytes = Encoding.UTF8.GetBytes(fieldName);
            writer.Write(nameBytes.Length);
            writer.Write(nameBytes);
            writer.Write(lengths.Length);
            foreach (var length in lengths)
                writer.Write((ushort)Math.Clamp(length, 0, ushort.MaxValue));
        }
    }

    private static void RewriteStoredFieldsAsV4(MMapDirectory directory)
    {
        var fdtPath = Directory.GetFiles(directory.DirectoryPath, "*.fdt").Single();
        var fdxPath = Directory.GetFiles(directory.DirectoryPath, "*.fdx").Single();
        var segmentPath = Directory.GetFiles(directory.DirectoryPath, "*.seg").Single();
        var segmentInfo = SegmentInfo.ReadFrom(segmentPath);
        var docs = new List<Dictionary<string, List<string>>>(segmentInfo.DocCount);
        using (var reader = StoredFieldsReader.Open(fdtPath, fdxPath))
        {
            for (int docId = 0; docId < segmentInfo.DocCount; docId++)
                docs.Add(reader.ReadDocument(docId));
        }

        WriteStoredFieldsV4(fdtPath, fdxPath, docs);
    }

    private static void WriteStoredFieldsV4(string fdtPath, string fdxPath, IReadOnlyList<Dictionary<string, List<string>>> docs)
    {
        const int blockSize = 16;
        using var fdtStream = new FileStream(fdtPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var fdtWriter = new BinaryWriter(fdtStream, Encoding.UTF8, leaveOpen: false);
        CodecConstants.WriteHeader(fdtWriter, 4);
        fdtWriter.Write(blockSize);

        var blockOffsets = new List<long>();
        using var rawStream = new MemoryStream(4096);
        using var rawWriter = new BinaryWriter(rawStream, Encoding.UTF8, leaveOpen: true);
        Span<byte> encodeBuffer = stackalloc byte[512];
        for (int blockStart = 0; blockStart < docs.Count; blockStart += blockSize)
        {
            int blockEnd = Math.Min(blockStart + blockSize, docs.Count);
            int blockCount = blockEnd - blockStart;
            rawStream.SetLength(0);
            rawStream.Position = 0;
            var intraOffsets = new int[blockCount];
            for (int i = 0; i < blockCount; i++)
            {
                intraOffsets[i] = (int)rawStream.Position;
                var fields = docs[blockStart + i];
                rawWriter.Write(fields.Count);
                foreach (var (name, values) in fields)
                {
                    WriteUtf8(rawWriter, name, encodeBuffer);
                    rawWriter.Write(values.Count);
                    foreach (var value in values)
                        WriteUtf8(rawWriter, value, encodeBuffer);
                }
            }
            rawWriter.Flush();

            var rawLength = (int)rawStream.Length;
            var (compressedData, compressedLength) = StoredFieldCompression.Compress(rawStream.GetBuffer().AsSpan(0, rawLength), FieldCompressionPolicy.Brotli);
            blockOffsets.Add(fdtStream.Position);
            fdtWriter.Write(blockCount);
            fdtWriter.Write(rawLength);
            fdtWriter.Write(compressedLength);
            foreach (var offset in intraOffsets)
                fdtWriter.Write(offset);
            fdtWriter.Write(compressedData.AsSpan(0, compressedLength));
        }
        fdtWriter.Flush();

        using var fdxStream = new FileStream(fdxPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var fdxWriter = new BinaryWriter(fdxStream, Encoding.UTF8, leaveOpen: false);
        CodecConstants.WriteHeader(fdxWriter, 4);
        fdxWriter.Write(blockSize);
        fdxWriter.Write(docs.Count);
        fdxWriter.Write(blockOffsets.Count);
        foreach (var offset in blockOffsets)
            fdxWriter.Write(offset);
    }

    private static void WriteUtf8(BinaryWriter writer, string value, Span<byte> buffer)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> bytes = byteCount <= buffer.Length ? buffer[..byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(value, bytes);
        writer.Write(byteCount);
        writer.Write(bytes);
    }

    private static List<PostingRow> ReadRows(IndexInput input, long offset, byte version)
    {
        using var postings = PostingsEnum.CreateWithPositions(input, offset, version);
        var rows = new List<PostingRow>(postings.DocFreq);
        while (postings.MoveNext())
        {
            var positions = postings.GetCurrentPositions().ToArray();
            var payloads = new byte[positions.Length][];
            for (int i = 0; i < positions.Length; i++)
                payloads[i] = postings.GetPayload(i).ToArray();
            rows.Add(new PostingRow(postings.DocId, postings.Freq, positions, payloads));
        }

        return rows;
    }

    private static void WriteV2Rows(BinaryWriter writer, IReadOnlyList<PostingRow> rows)
    {
        writer.Write(rows.Count);
        writer.Write(0);
        int previousDocId = 0;
        foreach (var row in rows)
        {
            PostingsWriter.WriteVarInt(writer, row.DocId - previousDocId);
            previousDocId = row.DocId;
        }

        bool hasFreqs = rows.Any(static row => row.Frequency != 1);
        writer.Write(hasFreqs);
        if (hasFreqs)
        {
            foreach (var row in rows)
                PostingsWriter.WriteVarInt(writer, row.Frequency);
        }

        bool hasPositions = rows.Any(static row => row.Positions.Length > 0);
        bool hasPayloads = rows.Any(static row => row.Payloads.Any(static payload => payload.Length > 0));
        writer.Write(hasPositions);
        writer.Write(hasPayloads);
        if (!hasPositions)
            return;

        foreach (var row in rows)
        {
            PostingsWriter.WriteVarInt(writer, row.Positions.Length);
            int previousPosition = 0;
            for (int i = 0; i < row.Positions.Length; i++)
            {
                PostingsWriter.WriteVarInt(writer, row.Positions[i] - previousPosition);
                previousPosition = row.Positions[i];
                if (hasPayloads)
                {
                    var payload = row.Payloads[i];
                    PostingsWriter.WriteVarInt(writer, payload.Length);
                    writer.Write(payload);
                }
            }
        }
    }

    public sealed record MigrationCodecCase(string Name, IReadOnlyList<string> ExpectedExtensions, byte LegacyVersion, Action<MMapDirectory> Downgrade)
    {
        public override string ToString() => Name;
    }

    private sealed record ExpectedDocument(string Id, string Body, string Category, double Number, string Source);

    private sealed record PostingRow(int DocId, int Frequency, int[] Positions, byte[][] Payloads);
}
