using System.Reflection.Emit;
using Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Architecture;

public sealed class AotBoundaryTests
{
    private static readonly Type[] ForbiddenTypes =
    [
        typeof(DynamicMethod),
        typeof(ILGenerator),
        typeof(AssemblyBuilder),
        typeof(ModuleBuilder),
        typeof(TypeBuilder),
        typeof(MethodBuilder),
        typeof(ConstructorBuilder),
        typeof(FieldBuilder),
        typeof(PropertyBuilder),
        typeof(EventBuilder),
    ];

    [Fact]
    public void Production_code_must_not_depend_on_runtime_IL_generation()
    {
        var failures = DependencyInspector.FindViolations(
            ArchitectureContext.CoreAssembly,
            static _ => true,
            dependency => ForbiddenTypes.Any(forbidden => DependencyInspector.IsExactType(dependency, forbidden)));

        RuleAssert.Empty("Production types must not depend on runtime IL-generation types:", failures);
    }
}
