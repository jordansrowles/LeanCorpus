using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Platform.Integration;

[Category(TestCategory.Integration)]
[Area(TestArea.Store)]
[Scope(TestScope.PlatformSmoke)]
public sealed class PlatformFilesystemSmokeTests
{
    [Fact]
    public void Mapped_file_can_be_read_renamed_and_deleted_after_disposal()
    {
        string root = Path.Combine(Path.GetTempPath(), $"leancorpus-platform-filesystem-{Guid.NewGuid():N}");
        const string sourceName = "mapped.bin";
        const string renamedName = "mapped-renamed.bin";
        const string deleteName = "delete-through-directory.bin";
        byte[] expected = [0x00, 0x11, 0x7f, 0x80, 0xfe, 0xff];

        try
        {
            using (var directory = new MMapDirectory(root))
            {
                using (var output = directory.CreateOutput(sourceName))
                    output.WriteBytes(expected);

                using (var input = directory.OpenInput(sourceName))
                    Assert.Equal(expected, input.ReadBytes(expected.Length));

                using (var output = directory.CreateOutput(deleteName))
                    output.WriteByte(0x5a);
                directory.DeleteFile(deleteName);
                Assert.False(File.Exists(Path.Combine(root, deleteName)));
            }

            string sourcePath = Path.Combine(root, sourceName);
            string renamedPath = Path.Combine(root, renamedName);
            File.Move(sourcePath, renamedPath);
            Assert.True(File.Exists(renamedPath));

            File.Delete(renamedPath);
            Assert.False(File.Exists(renamedPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        Assert.False(Directory.Exists(root));
    }
}
