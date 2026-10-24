using Xunit.v3;

namespace Rowles.LeanCorpus.Tests.Metadata;

public enum TestCategory
{
    Unit,
    Integration,
    Chaos
}

public enum TestTechnique
{
    PropertyBased,
    StateMachine,
    Metamorphic
}

public enum TestScope
{
    PlatformSmoke
}

public enum TestArea
{
    Foundation,
    CodecKit,
    Diagnostics,
    Document,
    Index,
    Linq,
    Mapping,
    Search,
    Serialisation,
    Store,
    TextIntegration,
    Util,
    Analysers,
    Filters,
    Stemmers,
    Tokenisers,
    Dictionaries,
    Languages
}

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CategoryAttribute(TestCategory category) : Attribute, ITraitAttribute
{
    public TestCategory Category { get; } = category;

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
    [
        new("Category", Category.ToString())
    ];
}

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class AreaAttribute(TestArea area) : Attribute, ITraitAttribute
{
    public TestArea Area { get; } = area;

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
    [
        new("Area", Area.ToString())
    ];
}

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class TechniqueAttribute(TestTechnique technique) : Attribute, ITraitAttribute
{
    public TestTechnique Technique { get; } = technique;

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
    [
        new("Technique", Technique.ToString())
    ];
}

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ScopeAttribute(TestScope scope) : Attribute, ITraitAttribute
{
    public TestScope Scope { get; } = scope;

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
    [
        new("Scope", Scope.ToString())
    ];
}
