using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Rowles.LeanCorpus.Tests.Architecture;

public sealed class LayeringTests
{
    private static readonly IObjectProvider<IType> Store = Types().That()
        .ResideInNamespaceMatching(@"^Rowles\.LeanCorpus\.Store(\.|$)")
        .As("Store types");

    private static readonly IObjectProvider<IType> Codecs = Types().That()
        .ResideInNamespaceMatching(@"^Rowles\.LeanCorpus\.Codecs(\.|$)")
        .As("codec types");

    private static readonly IObjectProvider<IType> Search = Types().That()
        .ResideInNamespaceMatching(@"^Rowles\.LeanCorpus\.Search(\.|$)")
        .As("Search types");

    private static readonly IObjectProvider<IType> Index = Types().That()
        .ResideInNamespaceMatching(@"^Rowles\.LeanCorpus\.Index(\.|$)")
        .As("Index types");

    private static readonly IObjectProvider<IType> Analysis = Types().That()
        .ResideInNamespaceMatching(@"^Rowles\.LeanCorpus\.Analysis(\.|$)")
        .As("Analysis types");

    [Fact]
    public void Store_must_not_depend_on_Search()
    {
        Types().That().Are(Store).Should().NotDependOnAny(Search).Check(ArchitectureContext.Core);
    }

    [Fact]
    public void Store_must_not_depend_on_Index()
    {
        Types().That().Are(Store).Should().NotDependOnAny(Index).Check(ArchitectureContext.Core);
    }

    [Fact]
    public void Codecs_must_not_depend_on_Search()
    {
        Types().That().Are(Codecs).Should().NotDependOnAny(Search).Check(ArchitectureContext.Core);
    }

    [Fact]
    public void Codecs_must_not_depend_on_Index()
    {
        Types().That().Are(Codecs).Should().NotDependOnAny(Index).Check(ArchitectureContext.Core);
    }

    [Fact]
    public void Analysis_must_not_depend_on_Search()
    {
        Types().That().Are(Analysis).Should().NotDependOnAny(Search).Check(ArchitectureContext.Core);
    }

    [Fact]
    public void Analysis_must_not_depend_on_Index()
    {
        Types().That().Are(Analysis).Should().NotDependOnAny(Index).Check(ArchitectureContext.Core);
    }
}
