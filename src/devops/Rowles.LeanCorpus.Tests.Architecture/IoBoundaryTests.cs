using System.IO.MemoryMappedFiles;
using Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Architecture;

public sealed class IoBoundaryTests
{
    private static readonly Type[] ForbiddenTypes =
    [
        typeof(File),
        typeof(FileStream),
        typeof(MemoryMappedFile),
        typeof(Directory),
        typeof(FileInfo),
        typeof(DirectoryInfo),
        typeof(StreamReader),
        typeof(StreamWriter),
    ];

    [Fact]
    public void File_system_IO_must_be_encapsulated_by_Store()
    {
        var failures = DependencyInspector.FindViolations(
            ArchitectureContext.CoreAssembly,
            static type => !NamespaceSegments.Contains(type.Namespace, "Store"),
            dependency => ForbiddenTypes.Any(forbidden => DependencyInspector.IsExactType(dependency, forbidden)));

        RuleAssert.Empty("Types outside Store must not depend on direct file-system IO types:", failures);
    }
}
