namespace Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

public sealed class RuleInfrastructureTests
{
    [Fact]
    public void Exact_dependency_matching_does_not_confuse_File_and_FileInfo()
    {
        var fileFailures = DependencyInspector.FindViolations(
            typeof(ExactDependencyFixture).Assembly,
            static type => type == typeof(ExactDependencyFixture),
            static dependency => DependencyInspector.IsExactType(dependency, typeof(File)));

        var fileInfoFailures = DependencyInspector.FindViolations(
            typeof(ExactDependencyFixture).Assembly,
            static type => type == typeof(ExactDependencyFixture),
            static dependency => DependencyInspector.IsExactType(dependency, typeof(FileInfo)));

        Assert.Empty(fileFailures);
        Assert.Equal([typeof(ExactDependencyFixture).FullName!], fileInfoFailures);
    }

    [Theory]
    [InlineData("Rowles.LeanCorpus.Index", "Index", true)]
    [InlineData("Rowles.LeanCorpus.Indexing", "Index", false)]
    [InlineData("Rowles.LeanCorpus.FileInfo", "File", false)]
    public void Namespace_segments_are_matched_exactly(string value, string segment, bool expected)
    {
        Assert.Equal(expected, NamespaceSegments.Contains(value, segment));
    }

    [Fact]
    public void Async_state_machine_dependencies_are_reported_against_the_owning_type()
    {
        var failures = DependencyInspector.FindViolations(
            typeof(AsyncDependencyFixture).Assembly,
            static type => type == typeof(AsyncDependencyFixture) || type.DeclaringType == typeof(AsyncDependencyFixture),
            static dependency => DependencyInspector.IsExactType(dependency, typeof(File)));

        Assert.Equal([typeof(AsyncDependencyFixture).FullName!], failures);
    }

    [Fact]
    public void Dynamic_code_generation_dependencies_are_detected()
    {
        var dynamicMethodFailures = DependencyInspector.FindViolations(
            typeof(DynamicCodeGenerationFixture).Assembly,
            static type => type == typeof(DynamicCodeGenerationFixture),
            static dependency => DependencyInspector.IsExactType(dependency, typeof(System.Reflection.Emit.DynamicMethod)));

        var ilGeneratorFailures = DependencyInspector.FindViolations(
            typeof(DynamicCodeGenerationFixture).Assembly,
            static type => type == typeof(DynamicCodeGenerationFixture),
            static dependency => DependencyInspector.IsExactType(dependency, typeof(System.Reflection.Emit.ILGenerator)));

        string fixtureName = typeof(DynamicCodeGenerationFixture).FullName!;
        Assert.Equal([fixtureName], dynamicMethodFailures);
        Assert.Equal([fixtureName], ilGeneratorFailures);
    }

    [Fact]
    public void Method_calls_are_detected()
    {
        var failures = DependencyInspector.FindMethodCallViolations(
            typeof(AssemblyLoadingFixture).Assembly,
            static type => type == typeof(AssemblyLoadingFixture),
            static method => method.DeclaringType == typeof(System.Reflection.Assembly) && method.Name.StartsWith("Load", StringComparison.Ordinal));

        Assert.Equal([typeof(AssemblyLoadingFixture).FullName!], failures);
    }

    [Fact]
    public void Failure_messages_are_sorted_and_readable()
    {
        string message = RuleAssert.FormatFailures("Broken rule:", ["Z.Type", "A.Type", "Z.Type"]);

        Assert.Equal($"Broken rule:{Environment.NewLine}  A.Type{Environment.NewLine}  Z.Type", message);
    }

    private sealed class ExactDependencyFixture
    {
        private readonly FileInfo _fileInfo = new("fixture");
    }

    private sealed class AsyncDependencyFixture
    {
        internal async Task<bool> ExistsAsync(string path)
        {
            await Task.Yield();
            return File.Exists(path);
        }
    }

    private sealed class DynamicCodeGenerationFixture
    {
        internal System.Reflection.Emit.ILGenerator CreateGenerator()
        {
            var method = new System.Reflection.Emit.DynamicMethod("Fixture", null, Type.EmptyTypes);
            return method.GetILGenerator();
        }
    }

    private sealed class AssemblyLoadingFixture
    {
        internal System.Reflection.Assembly Load(string assemblyName) => System.Reflection.Assembly.Load(assemblyName);
    }
}
