using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index.Compatibility;
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class IndexOpenGuardTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexOpenGuardTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteCodecFile(string filePath, ICodec<byte[]> format, byte version, byte[] body)
    {
        // Write a raw codec file directly: [version][VarInt64 bodyLen][body]
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.WriteByte(version);
        WriteVarInt64(fs, body.Length);
        fs.Write(body);
    }

    private static void WriteVarInt64(Stream stream, long value)
    {
        ulong encoded = unchecked((ulong)((value << 1) ^ (value >> 63)));
        while (encoded >= 0x80)
        {
            stream.WriteByte((byte)(encoded | 0x80));
            encoded >>= 7;
        }
        stream.WriteByte((byte)encoded);
    }

    [Fact(DisplayName = "EnsureCanOpenSegments: current-version legacy framing requires migration")]
    public void EnsureCanOpenSegments_CurrentVersionLegacyFrame_ForWriting_Throws()
    {
        var dir = new MMapDirectory(SubDir(nameof(EnsureCanOpenSegments_CurrentVersionLegacyFrame_ForWriting_Throws)));
        var segPath = Path.Combine(dir.DirectoryPath, "seg_0.dic");
        WriteCodecFile(segPath, CodecFormats.TermDictionary, CodecConstants.TermDictionaryVersion, [0x01]);

        var exception = Assert.Throws<InvalidDataException>(() =>
            IndexOpenGuard.EnsureCanOpenSegments(dir, ["seg_0"], IndexOpenCompatibilityMode.Strict, forWriting: true));
        Assert.Contains("older", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "EnsureCanOpenSegments: current canonical Norms succeeds for writing")]
    public void EnsureCanOpenSegments_CurrentCanonicalNorms_ForWriting_Succeeds()
    {
        var dir = new MMapDirectory(SubDir(nameof(EnsureCanOpenSegments_CurrentCanonicalNorms_ForWriting_Succeeds)));
        var normsPath = Path.Combine(dir.DirectoryPath, "seg_0.nrm");
        var descriptor = CodecCatalog.Default.GetFile("leancorpus.norms.data");
        CodecFileWriter.WriteAtomically(normsPath, descriptor, durable: false, static body => body.WriteByte(0));

        IndexOpenGuard.EnsureCanOpenSegments(dir, ["seg_0"], IndexOpenCompatibilityMode.Strict, forWriting: true);
    }

    [Fact(DisplayName = "EnsureCanOpenSegments: writer rejects future version")]
    public void EnsureCanOpenSegments_FutureVersion_ForWriting_Throws()
    {
        var dir = new MMapDirectory(SubDir(nameof(EnsureCanOpenSegments_FutureVersion_ForWriting_Throws)));
        var segPath = Path.Combine(dir.DirectoryPath, "seg_0.dic");
        WriteCodecFile(segPath, CodecFormats.TermDictionary, (byte)(CodecConstants.TermDictionaryVersion + 10), [0x01]);

        var ex = Assert.Throws<InvalidDataException>(() =>
            IndexOpenGuard.EnsureCanOpenSegments(dir, ["seg_0"], IndexOpenCompatibilityMode.Strict, forWriting: true));
        Assert.Contains("future", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "EnsureCanOpenSegments: older version throws when forWriting")]
    public void EnsureCanOpenSegments_OlderVersion_ForWriting_Throws()
    {
        var dir = new MMapDirectory(SubDir(nameof(EnsureCanOpenSegments_OlderVersion_ForWriting_Throws)));
        var segPath = Path.Combine(dir.DirectoryPath, "seg_0.fdt");
        WriteCodecFile(segPath, CodecFormats.TermDictionary, 1, [0x01]); // current is higher

        var ex = Assert.Throws<InvalidDataException>(() =>
            IndexOpenGuard.EnsureCanOpenSegments(dir, ["seg_0"], IndexOpenCompatibilityMode.Strict, forWriting: true));
        Assert.Contains("older", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "EnsureCanOpenSegments: older version succeeds when reading")]
    public void EnsureCanOpenSegments_OlderVersion_ReadOnly_Succeeds()
    {
        var dir = new MMapDirectory(SubDir(nameof(EnsureCanOpenSegments_OlderVersion_ReadOnly_Succeeds)));
        var segPath = Path.Combine(dir.DirectoryPath, "seg_0.fdt");
        WriteCodecFile(segPath, CodecFormats.TermDictionary, 1, [0x01]); // current is higher

        // Should not throw for read-only open.
        IndexOpenGuard.EnsureCanOpenSegments(dir, ["seg_0"], IndexOpenCompatibilityMode.Strict, forWriting: false);
    }

    [Fact(DisplayName = "EnsureCanOpenSegments: writer rejects corrupt file")]
    public void EnsureCanOpenSegments_CorruptFile_ForWriting_Throws()
    {
        var dir = new MMapDirectory(SubDir(nameof(EnsureCanOpenSegments_CorruptFile_ForWriting_Throws)));
        var segPath = Path.Combine(dir.DirectoryPath, "seg_0.dic");
        // Write a truncated file: version byte only, no body length.
        File.WriteAllBytes(segPath, [0x01]);

        Assert.ThrowsAny<Exception>(() =>
            IndexOpenGuard.EnsureCanOpenSegments(dir, ["seg_0"], IndexOpenCompatibilityMode.Strict, forWriting: true));
    }

    [Fact(DisplayName = "EnsureCanOpenSegments: writer rejects unreadable file")]
    public void EnsureCanOpenSegments_UnreadableFile_ForWriting_Throws()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return; // UnixFileMode is platform-specific.

        var dir = new MMapDirectory(SubDir(nameof(EnsureCanOpenSegments_UnreadableFile_ForWriting_Throws)));
        var segPath = Path.Combine(dir.DirectoryPath, "seg_0.dic");
        WriteCodecFile(segPath, CodecFormats.TermDictionary, CodecConstants.TermDictionaryVersion, [0x01]);

        // Make the file unreadable.
        File.SetUnixFileMode(segPath, UnixFileMode.None);

        try
        {
            Assert.ThrowsAny<Exception>(() =>
                IndexOpenGuard.EnsureCanOpenSegments(dir, ["seg_0"], IndexOpenCompatibilityMode.Strict, forWriting: true));
        }
        finally
        {
            // Restore readability so the fixture can clean up.
            File.SetUnixFileMode(segPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact(DisplayName = "EnsureCanOpenSegments: unsafe mode skips all checks")]
    public void EnsureCanOpenSegments_UnsafeMode_SkipsChecks()
    {
        var dir = new MMapDirectory(SubDir(nameof(EnsureCanOpenSegments_UnsafeMode_SkipsChecks)));
        var segPath = Path.Combine(dir.DirectoryPath, "seg_0.dic");
        // Write a future-version file that would normally throw.
        WriteCodecFile(segPath, CodecFormats.TermDictionary, (byte)(CodecConstants.TermDictionaryVersion + 50), [0x01]);

        // Unsafe mode: should not throw.
        IndexOpenGuard.EnsureCanOpenSegments(dir, ["seg_0"], IndexOpenCompatibilityMode.UnsafeIgnoreCompatibility, forWriting: true);
    }

    [Fact(DisplayName = "EnsureCanOpenSegments: non-codec segment files are skipped")]
    public void EnsureCanOpenSegments_NonCodecFiles_Skipped()
    {
        var dir = new MMapDirectory(SubDir(nameof(EnsureCanOpenSegments_NonCodecFiles_Skipped)));
        // Write a file with an extension not claimed by the codec catalogue.
        File.WriteAllText(Path.Combine(dir.DirectoryPath, "seg_0.xyz"), "garbage");

        // Should not throw — unknown extensions are silently skipped.
        IndexOpenGuard.EnsureCanOpenSegments(dir, ["seg_0"], IndexOpenCompatibilityMode.Strict, forWriting: true);
    }

    [Fact(DisplayName = "EnsureNoBlockingMigration: unsafe mode skips check")]
    public void EnsureNoBlockingMigration_UnsafeMode_SkipsCheck()
    {
        var dir = new MMapDirectory(SubDir(nameof(EnsureNoBlockingMigration_UnsafeMode_SkipsCheck)));

        // Should not throw regardless of migration state.
        IndexOpenGuard.EnsureNoBlockingMigration(dir, IndexOpenCompatibilityMode.UnsafeIgnoreCompatibility);
    }

    [Fact(DisplayName = "EnsureNoBlockingMigration: clean directory succeeds")]
    public void EnsureNoBlockingMigration_CleanDirectory_Succeeds()
    {
        var dir = new MMapDirectory(SubDir(nameof(EnsureNoBlockingMigration_CleanDirectory_Succeeds)));

        IndexOpenGuard.EnsureNoBlockingMigration(dir, IndexOpenCompatibilityMode.Strict);
    }
}
