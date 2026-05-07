using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Hnsw;
using Rowles.LeanLucene.Codecs.Fst;
using Rowles.LeanLucene.Codecs.Bkd;
using Rowles.LeanLucene.Codecs.Vectors;
using Rowles.LeanLucene.Codecs.TermVectors;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Codecs.StoredFields;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Tests codec format versioning (magic number + version byte) to ensure
/// backward compatibility and proper format validation.
/// </summary>
[Trait("Category", "Codecs")]
public sealed class FormatCompatibilityTests : IDisposable
{
    private readonly string _tempDirectory;

    public FormatCompatibilityTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "LeanLucene_FormatCompatibilityTests",
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
    /// Verifies the Write Header: Then Validate Header Round Trips scenario.
    /// </summary>
    [Fact(DisplayName = "Write Header: Then Validate Header Round Trips")]
    public void WriteHeader_ThenValidateHeader_RoundTrips()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "roundtrip_header.dat");
        const byte expectedVersion = 3;

        // Act - Write header using BinaryWriter
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            CodecConstants.WriteHeader(writer, expectedVersion);
        }

        // Assert - Validate header using BinaryReader
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            // Should not throw
            CodecConstants.ValidateHeader(reader, expectedVersion, "TestFile");

            // Verify we're positioned right after the header
            Assert.Equal(CodecConstants.HeaderSize, fs.Position);
        }
    }

    /// <summary>
    /// Verifies the Validate Header: Wrong Magic Throws Invalid Data Exception scenario.
    /// </summary>
    [Fact(DisplayName = "Validate Header: Wrong Magic Throws Invalid Data Exception")]
    public void ValidateHeader_WrongMagic_ThrowsInvalidDataException()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "wrong_magic.dat");
        const int wrongMagic = unchecked((int)0xDEADBEEF); // Not the correct magic number
        const byte version = 1;

        // Act - Write wrong magic number
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(wrongMagic); // Wrong magic
            writer.Write(version);
        }

        // Assert - Should throw InvalidDataException with descriptive message
        using var readFs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(readFs, System.Text.Encoding.UTF8, leaveOpen: false);

        var exception = Assert.Throws<InvalidDataException>(() =>
            CodecConstants.ValidateHeader(reader, version, "TestFile"));

        Assert.Contains("Invalid TestFile file", exception.Message);
        Assert.Contains("0x4C4C4E31", exception.Message); // Expected magic
        Assert.Contains("0xDEADBEEF", exception.Message); // Actual magic
    }

    /// <summary>
    /// Verifies the Validate Header: Version Too New Throws Invalid Data Exception scenario.
    /// </summary>
    [Fact(DisplayName = "Validate Header: Version Too New Throws Invalid Data Exception")]
    public void ValidateHeader_VersionTooNew_ThrowsInvalidDataException()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "version_too_new.dat");
        const byte fileVersion = 5;
        const byte maxSupportedVersion = 3;

        // Act - Write a version that's too new
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            CodecConstants.WriteHeader(writer, fileVersion);
        }

        // Assert - Should throw InvalidDataException about unsupported version
        using var readFs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(readFs, System.Text.Encoding.UTF8, leaveOpen: false);

        var exception = Assert.Throws<InvalidDataException>(() =>
            CodecConstants.ValidateHeader(reader, maxSupportedVersion, "TestFile"));

        Assert.Contains("Unsupported TestFile format version 5", exception.Message);
        Assert.Contains("supports up to version 3", exception.Message);
        Assert.Contains("Please upgrade LeanLucene", exception.Message);
    }

    /// <summary>
    /// Verifies the Validate Header: Older Version Succeeds scenario.
    /// </summary>
    [Fact(DisplayName = "Validate Header: Older Version Succeeds")]
    public void ValidateHeader_OlderVersion_Succeeds()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "older_version.dat");
        const byte fileVersion = 1;
        const byte maxSupportedVersion = 3;

        // Act - Write an older version
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            CodecConstants.WriteHeader(writer, fileVersion);
        }

        // Assert - Should succeed (backward compatibility)
        using var readFs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(readFs, System.Text.Encoding.UTF8, leaveOpen: false);

        // Should not throw
        CodecConstants.ValidateHeader(reader, maxSupportedVersion, "TestFile");

        // Verify position after validation
        Assert.Equal(CodecConstants.HeaderSize, readFs.Position);
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

        // Assert - Verify that .dic and .pos files start with the correct magic number
        var dicFiles = System.IO.Directory.GetFiles(indexPath, "*.dic");
        var posFiles = System.IO.Directory.GetFiles(indexPath, "*.pos");

        Assert.NotEmpty(dicFiles);
        Assert.NotEmpty(posFiles);

        // Verify .dic file header
        foreach (var dicFile in dicFiles)
        {
            using var fs = new FileStream(dicFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false);

            int magic = reader.ReadInt32();
            byte version = reader.ReadByte();

            Assert.Equal(CodecConstants.Magic, magic);
            Assert.Equal(CodecConstants.TermDictionaryVersion, version);
        }

        // Verify .pos file header
        foreach (var posFile in posFiles)
        {
            using var fs = new FileStream(posFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false);

            int magic = reader.ReadInt32();
            byte version = reader.ReadByte();

            Assert.Equal(CodecConstants.Magic, magic);
            Assert.Equal(CodecConstants.PostingsVersion, version);
        }
    }

    /// <summary>
    /// Verifies the Magic Number: Is Correct Ascii Representation scenario.
    /// </summary>
    [Fact(DisplayName = "Magic Number: Is Correct Ascii Representation")]
    public void MagicNumber_IsCorrectAsciiRepresentation()
    {
        // Arrange - Magic number should be "LLN1" in ASCII
        const int magic = CodecConstants.Magic;
        
        // Act - Convert magic to bytes and then to ASCII string
        var bytes = BitConverter.GetBytes(magic);
        var ascii = System.Text.Encoding.ASCII.GetString(bytes);

        // Assert - Should spell "LLN1" (little-endian on most systems)
        Assert.Equal("1NLL", ascii); // Little-endian: reversed byte order
        Assert.Equal(0x4C4C4E31, magic); // Hex representation
    }

    /// <summary>
    /// Verifies the Header Size: Matches Expected Layout scenario.
    /// </summary>
    [Fact(DisplayName = "Header Size: Matches Expected Layout")]
    public void HeaderSize_MatchesExpectedLayout()
    {
        // Arrange & Assert - Header is 4 bytes (magic) + 1 byte (version)
        Assert.Equal(5, CodecConstants.HeaderSize);
        Assert.Equal(sizeof(int) + sizeof(byte), CodecConstants.HeaderSize);
    }

    /// <summary>
    /// Verifies the All Codec Versions: Are Defined And Positive scenario.
    /// </summary>
    [Fact(DisplayName = "All Codec Versions: Are Defined And Positive")]
    public void AllCodecVersions_AreDefinedAndPositive()
    {
        // Arrange & Assert - Verify all per-codec version constants exist and are valid
        Assert.True(CodecConstants.TermDictionaryVersion > 0);
        Assert.True(CodecConstants.PostingsVersion > 0);
        Assert.True(CodecConstants.NormsVersion > 0);
        Assert.True(CodecConstants.VectorVersion > 0);
        Assert.True(CodecConstants.StoredFieldsVersion > 0);
        Assert.True(CodecConstants.TermVectorsVersion > 0);
        Assert.True(CodecConstants.NumericDocValuesVersion > 0);
        Assert.True(CodecConstants.SortedDocValuesVersion > 0);
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
        const byte expectedVersion = 2;

        // Act - Write using IndexOutput
        using (var output = new IndexOutput(filePath))
        {
            CodecConstants.WriteHeader(output, expectedVersion);
            output.Flush();
        }

        // Assert - Read raw bytes and verify
        var bytes = File.ReadAllBytes(filePath);
        
        Assert.Equal(CodecConstants.HeaderSize, bytes.Length);
        
        // Verify magic (4 bytes)
        int magic = BitConverter.ToInt32(bytes, 0);
        Assert.Equal(CodecConstants.Magic, magic);
        
        // Verify version (1 byte)
        Assert.Equal(expectedVersion, bytes[4]);
    }

    /// <summary>
    /// Verifies the Validate Header: With Index Input Validates Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Validate Header: With Index Input Validates Correctly")]
    public void ValidateHeader_WithIndexInput_ValidatesCorrectly()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "indexinput_header.dat");
        const byte expectedVersion = 3;

        // Write header using IndexOutput
        using (var output = new IndexOutput(filePath))
        {
            CodecConstants.WriteHeader(output, expectedVersion);
            output.Flush();
        }

        // Act & Assert - Read and validate using IndexInput
        using var input = new IndexInput(filePath);
        
        // Should not throw
        CodecConstants.ValidateHeader(input, expectedVersion, "TestFile");
        
        // Verify position after validation
        Assert.Equal(CodecConstants.HeaderSize, input.Position);
    }

    /// <summary>
    /// Verifies the Validate Header: With Index Input Wrong Magic Throws scenario.
    /// </summary>
    [Fact(DisplayName = "Validate Header: With Index Input Wrong Magic Throws")]
    public void ValidateHeader_WithIndexInput_WrongMagic_Throws()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "indexinput_wrong_magic.dat");

        // Write wrong header using IndexOutput
        using (var output = new IndexOutput(filePath))
        {
            output.WriteInt32(unchecked((int)0xBADC0FFE)); // Wrong magic
            output.WriteByte(1);
            output.Flush();
        }

        // Act & Assert
        using var input = new IndexInput(filePath);
        
        var exception = Assert.Throws<InvalidDataException>(() =>
            CodecConstants.ValidateHeader(input, 1, "TestFile"));
        
        Assert.Contains("Invalid TestFile file", exception.Message);
    }

    /// <summary>
    /// Verifies the Validate Header: With Index Input Version Too New Throws scenario.
    /// </summary>
    [Fact(DisplayName = "Validate Header: With Index Input Version Too New Throws")]
    public void ValidateHeader_WithIndexInput_VersionTooNew_Throws()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "indexinput_version_new.dat");
        const byte fileVersion = 10;
        const byte maxVersion = 3;

        // Write header with newer version
        using (var output = new IndexOutput(filePath))
        {
            CodecConstants.WriteHeader(output, fileVersion);
            output.Flush();
        }

        // Act & Assert
        using var input = new IndexInput(filePath);
        
        var exception = Assert.Throws<InvalidDataException>(() =>
            CodecConstants.ValidateHeader(input, maxVersion, "TestFile"));
        
        Assert.Contains($"Unsupported TestFile format version {fileVersion}", exception.Message);
    }
}
