using System.Reflection;
using Rowles.LeanCorpus.Tests.Metadata;

namespace Rowles.Text.Tests;

[Category(TestCategory.Unit)]
[Area(TestArea.Util)]
public sealed class MetadataClassificationTests
{
    private static readonly Type[] TestMethodAttributeTypes =
    [
        typeof(FactAttribute),
        typeof(TheoryAttribute)
    ];

    [Fact]
    public void Every_test_class_has_exactly_one_category_and_at_least_one_area()
    {
        var offenders = new List<string>();
        foreach (var type in typeof(MetadataClassificationTests).Assembly.GetTypes())
        {
            if (!HasTestMethod(type))
                continue;

            int categoryCount = type.GetCustomAttributes<CategoryAttribute>().Count();
            int areaCount = type.GetCustomAttributes<AreaAttribute>().Count();
            if (categoryCount != 1 || areaCount < 1)
                offenders.Add($"{type.FullName}: Category={categoryCount}, Area={areaCount}");
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Test_namespace_primary_area_matches_its_metadata()
    {
        var offenders = new List<string>();
        foreach (var type in typeof(MetadataClassificationTests).Assembly.GetTypes())
        {
            if (!HasTestMethod(type))
                continue;

            string? sourcePath = FindSourcePath(type);
            if (sourcePath is null)
            {
                offenders.Add($"{type.FullName}: source file could not be resolved.");
                continue;
            }

            string relativePath = Path.GetRelativePath(GetProjectDirectory(), sourcePath);
            if (!relativePath.Contains(Path.DirectorySeparatorChar))
                continue;

            string areaName = relativePath.Split(Path.DirectorySeparatorChar)[0];
            if (!Enum.TryParse<TestArea>(areaName, ignoreCase: false, out var expectedArea))
            {
                offenders.Add($"{type.FullName}: source path must start with a TestArea name.");
                continue;
            }

            if (!type.GetCustomAttributes<AreaAttribute>().Any(attribute => attribute.Area == expectedArea))
                offenders.Add($"{type.FullName}: expected Area={expectedArea} from source path.");
        }

        Assert.Empty(offenders);
    }

    private static bool HasTestMethod(Type type)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (method.GetCustomAttributes(inherit: true).Any(attribute => TestMethodAttributeTypes.Any(testMethodType => testMethodType.IsInstanceOfType(attribute))))
                return true;
        }

        return false;
    }

    private static string? FindSourcePath(Type type)
    {
        string projectDirectory = GetProjectDirectory();
        return Directory.EnumerateFiles(projectDirectory, $"{type.Name}.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                                  !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SingleOrDefault();
    }

    private static string GetProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && directory.Name != "Rowles.Text.Tests")
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find the Text test project directory.");
    }
}
