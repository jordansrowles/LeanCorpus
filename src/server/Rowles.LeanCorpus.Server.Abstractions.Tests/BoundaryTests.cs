using Rowles.LeanCorpus.Server.Abstractions.Community;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Ports;
using Rowles.LeanCorpus.Server.Abstractions.Serialisation;

namespace Rowles.LeanCorpus.Server.Abstractions.Tests;

[Trait("Area", "Server")]
public sealed class BoundaryTests
{
    [Fact]
    public void AbstractionsAssemblyHasNoFrameworkOrPrivateReferences()
    {
        string[] references = typeof(IOperationRouter).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!).ToArray();

        Assert.DoesNotContain(references, name => name.Contains("AspNetCore", StringComparison.Ordinal) || name.Contains("Grpc", StringComparison.Ordinal) || name.Contains("DotNext", StringComparison.Ordinal));
    }

    [Fact]
    public void CommunityDefaultsImplementEveryInterceptionPort()
    {
        Assert.IsAssignableFrom<IOperationRouter>(new CommunityOperationRouter());
        Assert.IsAssignableFrom<IAuthorisationService>(new CommunityAuthorisationService());
        Assert.IsAssignableFrom<IEntitlementEvaluator>(new CommunityEntitlementEvaluator());
        Assert.IsAssignableFrom<IWriteAcknowledgementPolicy>(new CommunityWriteAcknowledgementPolicy());
        Assert.IsAssignableFrom<IIndexLifecycleInterceptor>(new CommunityIndexLifecycleInterceptor());
        Assert.IsAssignableFrom<IAuditPublisher>(new CommunityAuditPublisher());
        Assert.IsAssignableFrom<IConsistencyPolicy>(new CommunityConsistencyPolicy());
        Assert.IsAssignableFrom<IInspectionFilter>(new CommunityInspectionFilter());
        Assert.IsAssignableFrom<IAuthenticationProvider>(new CommunityAuthenticationProvider());
    }

    [Fact]
    public void EndpointCatalogueContainsAllRequiredPorts()
    {
        Assert.Contains(ServerEndpointCatalog.All, endpoint => endpoint.Route == "/v1/indices/{name}/documents:bulk" && endpoint.RequiredPorts.Contains(InterceptionPort.WriteAcknowledgement));
        Assert.Contains(ServerEndpointCatalog.All, endpoint => endpoint.Route == "/v1/indices/{name}/inspection/{resource}" && endpoint.RequiredPorts.Contains(InterceptionPort.Inspection));
        Assert.Contains(ServerEndpointCatalog.All, endpoint => endpoint.Route == "/v1/admin/license:validate" && endpoint.Edition == ApiEdition.Enterprise);
    }

    [Fact]
    public void SerialiserContextProvidesMetadataForSearchRequests()
    {
        Assert.NotNull(ServerJsonSerialiserContext.Default.SearchRequest);
    }
}
