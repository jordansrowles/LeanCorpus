using System.Reflection;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.Tests.Metadata;

namespace Rowles.LeanCorpus.Tests.Core.Foundation.Unit;

[Category(TestCategory.Unit)]
[Area(TestArea.Foundation)]
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

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Area_and_category_enums_cover_only_declared_values()
    {
        Assert.True(Enum.GetValues<TestCategory>().All(static c => c is TestCategory.Unit or TestCategory.Integration or TestCategory.Chaos));
        Assert.True(Enum.GetValues<TestArea>().All(static a => a is
            TestArea.Foundation or TestArea.CodecKit or TestArea.Diagnostics or TestArea.Document or
            TestArea.Index or TestArea.Linq or TestArea.Mapping or TestArea.Search or
            TestArea.Serialization or TestArea.Store or TestArea.TextIntegration or TestArea.Util or
            TestArea.Analysers or TestArea.Filters or TestArea.Stemmers or TestArea.Tokenisers or
            TestArea.Dictionaries or TestArea.Languages));
    }

    [Fact]
    public void Test_namespace_primary_area_and_category_match_its_metadata()
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

            var segments = Path.GetRelativePath(GetProjectDirectory(), sourcePath).Split(Path.DirectorySeparatorChar);
            if (segments.Length < 2 ||
                !Enum.TryParse<TestArea>(segments[0], ignoreCase: false, out var expectedArea) ||
                !Enum.TryParse<TestCategory>(segments[1], ignoreCase: false, out var expectedCategory))
            {
                offenders.Add($"{type.FullName}: source path must start with Area/Category.");
                continue;
            }

            var actualCategory = type.GetCustomAttribute<CategoryAttribute>()?.Category;
            var actualAreas = type.GetCustomAttributes<AreaAttribute>().Select(static attribute => attribute.Area);
            if (actualCategory != expectedCategory || !actualAreas.Contains(expectedArea))
                offenders.Add($"{type.FullName}: expected Category={expectedCategory}, Area={expectedArea} from source path.");
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static bool HasTestMethod(Type type)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (method.GetCustomAttributes(inherit: true).Any(a => TestMethodAttributeTypes.Any(t => t.IsInstanceOfType(a))))
                return true;
        }
        return false;
    }

    private static string? FindSourcePath(Type type)
    {
        string projectDirectory = GetProjectDirectory();
        return Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                                  !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains($"namespace {type.Namespace}", StringComparison.Ordinal) &&
                           Regex.IsMatch(File.ReadAllText(path), $@"\bclass\s+{Regex.Escape(type.Name)}\b"))
            .SingleOrDefault();
    }

    private static string GetProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && directory.Name != "Rowles.LeanCorpus.Tests.Core")
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find the Core test project directory.");
    }
}
