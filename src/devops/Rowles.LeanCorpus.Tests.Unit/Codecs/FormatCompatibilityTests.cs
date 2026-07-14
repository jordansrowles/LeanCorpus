using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

/// <summary>
/// Tests codec format versioning (CodecKit header envelope: version byte + VarInt64 body length) to ensure
/// proper format validation and forward/backward compatibility.
/// </summary>
[Trait("Category", "Codecs")]
public sealed class FormatCompatibilityTests : IDisposable
{
    private readonly string _tempDirectory;

    public FormatCompatibilityTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "LeanCorpus_FormatCompatibilityTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            // Force GC to release memory-mapped file handles before deletion
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Best effort cleanup - test temp directory may be locked
            }
        }
    }

    /// <summary>
    /// Helper for writing LEB128 VarInt64 to a BinaryWriter.
    /// </summary>
    private static void WriteVarInt64(BinaryWriter w, long value)
    {
        ulong v = (ulong)value;
        while (v >= 0x80)
        {
            w.Write((byte)(v | 0x80));
            v >>= 7;
        }
        w.Write((byte)v);
    }

    /// <summary>
    /// Verifies the Write Header: Then Read Header Round Trips scenario.
    /// </summary>
    [Fact(DisplayName = "Write Header: Then Read Header Round Trips")]
    public void WriteHeader_ThenReadHeader_RoundTrips()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "roundtrip_header.dat");
        const byte expectedVersion = CodecConstants.TermDictionaryVersion;
        byte[] body = [];

        // Act - Write header using BinaryWriter via CodecFileHeader
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            CodecFileHeader.Write(writer, CodecFormats.TermDictionary, body);
        }

        // Assert - Read version using CodecFileHeader.ReadVersion
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            byte version = CodecFileHeader.ReadVersion(reader, CodecFormats.TermDictionary);
            Assert.Equal(expectedVersion, version);

            // Verify we're positioned right after the header envelope
            // CodecKit envelope: [version:byte][bodyLength:VarInt64]
            // bodyLength=0 → VarInt is 1 byte → total header = 2
            Assert.Equal(2, fs.Position);
        }
    }

    /// <summary>
    /// Verifies that a corrupt CodecKit header (truncated VarInt64) throws InvalidDataException.
    /// </summary>
    [Fact(DisplayName = "Validate Header: Corrupt CodecKit Header Throws Invalid Data Exception")]
    public void ValidateHeader_CorruptHeader_ThrowsInvalidDataException()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "corrupt_header.dat");

        // Act - Write a truncated CodecKit envelope where the VarInt has its
        // continuation bit set but no following byte exists.
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            writer.Write((byte)1);    // version
            writer.Write((byte)0x80); // truncated VarInt (continuation marker, no more data)
        }

        // Assert - Should throw InvalidDataException
        using var readFs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(readFs, System.Text.Encoding.UTF8, leaveOpen: false);

        var exception = Assert.Throws<InvalidDataException>(() =>
            CodecFileHeader.Read(reader, CodecFormats.TermDictionary));

        Assert.Contains("CodecKit file is corrupt or truncated", exception.Message);
    }

    /// <summary>
    /// Verifies that an unknown (forward-compat) version reads successfully and preserves
    /// the version and raw body bytes.
    /// </summary>
    [Fact(DisplayName = "Validate Header: Unknown Version Reads As Forward Compat")]
    public void ValidateHeader_UnknownVersion_ReadsAsForwardCompatible()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "unknown_version.dat");
        const byte fileVersion = 5;

        // Act - Write a CodecKit header with a version that doesn't exist in CodecFormats.TermDictionary
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            writer.Write((byte)fileVersion);
            WriteVarInt64(writer, 0); // no body content
        }

        // Assert - Should succeed (forward compatibility)
        using var readFs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(readFs, System.Text.Encoding.UTF8, leaveOpen: false);

        var result = CodecFileHeader.Read(reader, CodecFormats.TermDictionary);
        Assert.Equal(fileVersion, result.Version);
        Assert.Empty(result.Body);
    }

    /// <summary>
    /// Verifies the Validate Header: Older Version Succeeds scenario.
    /// </summary>
    [Fact(DisplayName = "Validate Header: Older Version Succeeds")]
    public void ValidateHeader_OlderVersion_Succeeds()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "older_version.dat");
        byte[] body = [];

        // Act - Write header using CodecFileHeader.Write
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            CodecFileHeader.Write(writer, CodecFormats.TermDictionary, body);
        }

        // Assert - Should succeed
        using var readFs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(readFs, System.Text.Encoding.UTF8, leaveOpen: false);

        byte version = CodecFileHeader.ReadVersion(reader, CodecFormats.TermDictionary);
        Assert.Equal(CodecConstants.TermDictionaryVersion, version);

        // Verify position after reading version
        Assert.Equal(2, readFs.Position);
    }

    /// <summary>
    /// Verifies the Write And Read Index: Includes Headers scenario.
    /// </summary>
    [Fact(DisplayName = "Write And Read Index: Includes Headers")]
    public void WriteAndReadIndex_IncludesHeaders()
    {
        // Arrange - Create a simple index with one document
        var indexPath = Path.Combine(_tempDirectory, "test_index");
        Directory.CreateDirectory(indexPath);

        var directory = new MMapDirectory(indexPath);
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = new StandardAnalyser(),
            MaxBufferedDocs = 10
        };

        // Act - Write a document to create index files
        using (var writer = new IndexWriter(directory, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", "test document"));
            doc.Add(new TextField("content", "this is a test document with some content"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        // Allow file handles to be released
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Assert - Verify that .dic and .pos files start with the correct CodecKit header
        var dicFiles = System.IO.Directory.GetFiles(indexPath, "*.dic");
        var posFiles = System.IO.Directory.GetFiles(indexPath, "*.pos");

        Assert.NotEmpty(dicFiles);
        Assert.NotEmpty(posFiles);

        // Verify .dic file header
        foreach (var dicFile in dicFiles)
        {
            using var fs = new FileStream(dicFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false);

            byte version = CodecFileHeader.ReadVersion(reader, CodecFormats.TermDictionary);
            Assert.Equal(CodecConstants.TermDictionaryVersion, version);
        }

        // Verify .pos file header
        foreach (var posFile in posFiles)
        {
            using var fs = new FileStream(posFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false);

            byte version = PostingsFileHeader.ReadVersion(reader);
            Assert.Equal(CodecConstants.PostingsVersion, version);
        }
    }

    // MagicNumber_IsCorrectAsciiRepresentation removed — CodecKit has no magic bytes.

    /// <summary>
    /// Verifies the CodecKit header size matches expected layout.
    /// CodecKit envelope: [version:byte][bodyLength:VarInt64].
    /// For an empty body (bodyLength=0), VarInt64 is a single 0x00 byte, so total = 2.
    /// </summary>
    [Fact(DisplayName = "Header Size: Matches CodecKit Envelope Layout")]
    public void HeaderSize_MatchesCodecKitLayout()
    {
        // Arrange & Act - Write a CodecKit header with empty body
        var filePath = Path.Combine(_tempDirectory, "headersize.dat");

        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            CodecFileHeader.Write(writer, CodecFormats.TermDictionary, []);
        }

        // Assert - Read raw bytes and verify structure
        var bytes = File.ReadAllBytes(filePath);

        Assert.Equal(2, bytes.Length);
        Assert.Equal(CodecConstants.TermDictionaryVersion, bytes[0]); // version byte
        Assert.Equal(0x00, bytes[1]);                           // VarInt(0) = single 0x00
    }

    /// <summary>
    /// Verifies the All Codec Versions: Are Defined And Positive scenario.
    /// </summary>
    [Fact(DisplayName = "All Codec Versions: Are Defined And Positive")]
    public void AllCodecVersions_AreDefinedAndPositive()
    {
        // Arrange & Assert - Verify all per-codec version constants exist and are valid
        Assert.True(CodecConstants.TermDictionaryVersion > 0);
        Assert.True(CodecConstants.TermDictionaryVersion > 0);
        Assert.True(CodecConstants.NormsVersion > 0);
        Assert.True(CodecConstants.VectorVersion > 0);
        Assert.True(CodecConstants.StoredFieldsVersion > 0);
        Assert.True(CodecConstants.TermVectorsVersion > 0);
        Assert.True(CodecConstants.NumericDocValuesVersion > 0);
        Assert.True(CodecConstants.SortedDocValuesVersion > 0);
        Assert.True(CodecConstants.SortedSetDocValuesVersion > 0);
        Assert.True(CodecConstants.SortedNumericDocValuesVersion > 0);
        Assert.True(CodecConstants.BinaryDocValuesVersion > 0);
        Assert.True(CodecConstants.BKDVersion > 0);
    }

    /// <summary>
    /// Verifies the Write Header: With Index Output Writes Correct Bytes scenario.
    /// </summary>
    [Fact(DisplayName = "Write Header: With Index Output Writes Correct Bytes")]
    public void WriteHeader_WithIndexOutput_WritesCorrectBytes()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "indexoutput_header.dat");
        const byte expectedVersion = CodecConstants.TermDictionaryVersion;
        byte[] body = [];

        // Act - Write using IndexOutput via CodecFileHeader
        using (var output = new IndexOutput(filePath))
        {
            CodecFileHeader.Write(output, CodecFormats.TermDictionary, body);
            output.Flush();
        }

        // Assert - Read raw bytes and verify CodecKit structure
        var bytes = File.ReadAllBytes(filePath);

        Assert.Equal(2, bytes.Length);

        // Verify version byte
        Assert.Equal(expectedVersion, bytes[0]);

        // Verify VarInt(0) body length
        Assert.Equal(0x00, bytes[1]);
    }

    /// <summary>
    /// Verifies the Validate Header: With Index Input Validates Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Validate Header: With Index Input Validates Correctly")]
    public void ValidateHeader_WithIndexInput_ValidatesCorrectly()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "indexinput_header.dat");
        byte[] body = [];

        // Write header using IndexOutput
        using (var output = new IndexOutput(filePath))
        {
            CodecFileHeader.Write(output, CodecFormats.TermDictionary, body);
            output.Flush();
        }

        // Act & Assert - Read and validate using IndexInput
        using var input = new IndexInput(filePath);

        // Should not throw
        byte version = CodecFileHeader.ReadVersion(input, CodecFormats.TermDictionary);

        // Verify version
        Assert.Equal(CodecConstants.TermDictionaryVersion, version);

        // Verify position after reading version header
        Assert.Equal(2, input.Position);
    }

    /// <summary>
    /// Verifies the Validate Header: With Index Input Wrong Data Throws scenario.
    /// </summary>
    [Fact(DisplayName = "Validate Header: With Index Input Wrong Data Throws")]
    public void ValidateHeader_WithIndexInput_WrongData_Throws()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "indexinput_wrong_data.dat");

        // Write a truncated CodecKit header using IndexOutput
        using (var output = new IndexOutput(filePath))
        {
            output.WriteByte(1);    // version
            output.WriteByte(0x80); // truncated VarInt (continuation marker, no more data)
            output.Flush();
        }

        // Act & Assert
        using var input = new IndexInput(filePath);

        var exception = Assert.Throws<InvalidDataException>(() =>
            CodecFileHeader.Read(input, CodecFormats.TermDictionary));

        Assert.Contains("CodecKit file is corrupt or truncated", exception.Message);
    }

    /// <summary>
    /// Verifies the Validate Header: With Index Input Unknown Version Reads scenario.
    /// </summary>
    [Fact(DisplayName = "Validate Header: With Index Input Unknown Version Reads")]
    public void ValidateHeader_WithIndexInput_UnknownVersion_Reads()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "indexinput_unknown_version.dat");

        // Write a CodecKit header with an unknown version
        using (var output = new IndexOutput(filePath))
        {
            output.WriteByte(10); // unknown version
            output.WriteByte(0);  // VarInt(0) — no body
            output.Flush();
        }

        // Act & Assert
        using var input = new IndexInput(filePath);

        var result = CodecFileHeader.Read(input, CodecFormats.TermDictionary);
        Assert.Equal(10, result.Version);
        Assert.Empty(result.Body);
    }
}
