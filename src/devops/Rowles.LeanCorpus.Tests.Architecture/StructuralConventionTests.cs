using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Architecture;

public sealed class StructuralConventionTests
{
    [Fact]
    public void Concrete_LeanDirectory_subclasses_must_be_sealed()
    {
        var failures = ArchitectureContext.CoreAssembly.GetTypes()
            .Where(type => type != typeof(LeanDirectory) && typeof(LeanDirectory).IsAssignableFrom(type))
            .Where(static type => !type.IsAbstract && !type.IsSealed)
            .Select(static type => type.FullName ?? type.Name);

        RuleAssert.Empty("Concrete LeanDirectory subclasses must be sealed:", failures);
    }

    [Fact]
    public void Exception_types_must_end_in_Exception()
    {
        var failures = ArchitectureContext.CoreAssembly.GetTypes()
            .Where(type => type != typeof(Exception) && typeof(Exception).IsAssignableFrom(type))
            .Where(static type => !type.Name.EndsWith("Exception", StringComparison.Ordinal))
            .Select(static type => type.FullName ?? type.Name);

        RuleAssert.Empty("Exception-derived types must end in 'Exception':", failures);
    }

    [Fact]
    public void Interface_names_must_begin_with_I()
    {
        var failures = ArchitectureContext.CoreAssembly.GetTypes()
            .Where(static type => type.IsInterface && !type.Name.StartsWith('I'))
            .Select(static type => type.FullName ?? type.Name);

        RuleAssert.Empty("Interfaces must begin with 'I':", failures);
    }
}
