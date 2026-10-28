using System.Text;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Store;

[Category(TestCategory.Unit)]
[Area(TestArea.Store)]
public sealed class CompoundFileWriterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(),
        "LeanCorpus_CompoundFileWriterTests", Guid.NewGuid().ToString("N"));

    public CompoundFileWriterTests() => Directory.CreateDirectory(_path);

    [Fact]
    public void Pack_DuplicateSources_RejectsBeforeChangingAnyFiles()
    {
        const string destination = "packed.cfs";
        File.WriteAllBytes(Path.Combine(_path, "member"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_path, destination), [4, 5]);
        File.WriteAllBytes(Path.Combine(_path, destination + ".tmp"), [6, 7]);

        var exception = Assert.Throws<InvalidDataException>(() =>
            CompoundFileWriter.Pack(_path, destination, ["member", "member"]));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(_path, "member")));
        Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(Path.Combine(_path, destination)));
        Assert.Equal(new byte[] { 6, 7 }, File.ReadAllBytes(Path.Combine(_path, destination + ".tmp")));
        Assert.Equal(3, Directory.GetFiles(_path).Length);
    }

    [Fact]
    public void Pack_ExplicitDestination_PreservesSuppliedEntryOrderAndFormat()
    {
        const string destination = "container";
        string[] names = ["z.payload", "a.payload"];
        byte[][] payloads = [[1, 2], [3, 4, 5]];
        for (int i = 0; i < names.Length; i++)
            File.WriteAllBytes(Path.Combine(_path, names[i]), payloads[i]);
        File.WriteAllBytes(Path.Combine(_path, "unselected"), [9]);

        Assert.True(CompoundFileWriter.Pack(_path, destination, names));

        using (var stream = File.OpenRead(Path.Combine(_path, destination)))
        using (var reader = new BinaryReader(stream, Encoding.UTF8))
        {
            Assert.Equal(CompoundFileWriter.Magic, reader.ReadInt32());
            Assert.Equal(1, reader.ReadInt32());
            Assert.Equal(2, reader.ReadInt32());
            foreach (string name in names)
            {
                Assert.Equal(name, reader.ReadString());
                Assert.True(reader.ReadInt64() > 0);
                Assert.True(reader.ReadInt64() > 0);
            }
        }
        using var directory = new MMapDirectory(_path);
        using var compound = CompoundFileReader.Open(directory, destination);
        for (int i = 0; i < names.Length; i++)
        {
            using var input = compound.OpenInput(directory, names[i]);
            Assert.Equal(payloads[i], input.ReadBytes(payloads[i].Length).ToArray());
            Assert.False(File.Exists(Path.Combine(_path, names[i])));
        }
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(_path, "unselected")));
        Assert.False(File.Exists(Path.Combine(_path, destination + ".tmp")));
    }

    [Fact]
    public void Pack_EmptySources_DoesNotChangeDestination()
    {
        const string destination = "empty.cfs";
        File.WriteAllBytes(Path.Combine(_path, destination), [1]);

        Assert.False(CompoundFileWriter.Pack(_path, destination, []));

        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(_path, destination)));
        Assert.Single(Directory.GetFiles(_path));
    }

    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);
}
