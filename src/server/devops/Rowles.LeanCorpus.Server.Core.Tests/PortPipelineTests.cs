using System.Text.Json;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Abstractions.Ports;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Services;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class PortPipelineTests
{
    [Fact]
    public async Task CommunityOperationsInvokeTheirDeclaredInterceptionPorts()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-server-ports-{Guid.NewGuid():N}");
        RecordingPorts ports = new();
        try
        {
            using LocalServerCore server = await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = root }, ports.CreateSet());
            Assert.True((await server.CreateAsync(CreateRequest())).IsSuccess);
            using JsonDocument document = JsonDocument.Parse("{\"content\":\"port proof\"}");
            Assert.True((await server.BulkAsync(new BulkDocumentsRequest("books", [new BulkDocumentOperation(DocumentOperationKind.Index, "one", document.RootElement.Clone())], Refresh: true))).IsSuccess);
            Assert.True((await server.SearchAsync("books", new SearchRequest(new TermQueryDefinition("content", "proof")))).IsSuccess);
            Assert.True((await server.InspectAsync("books", new InspectionRequest(InspectionResource.Fields, 10))).IsSuccess);
            Assert.True((await server.DeleteAsync(new DeleteIndexRequest("books", ConfirmationTokens.Create("delete-index", "books")))).IsSuccess);

            Assert.True(ports.AuthenticationCalls >= 5);
            Assert.True(ports.AuthorisationCalls >= 5);
            Assert.True(ports.EntitlementCalls >= 3);
            Assert.True(ports.RoutingCalls >= 2);
            Assert.Equal(1, ports.AcknowledgementCalls);
            Assert.Equal(1, ports.ConsistencyCalls);
            Assert.Equal(1, ports.InspectionCalls);
            Assert.Equal(4, ports.LifecycleCalls);
            Assert.True(ports.AuditCalls >= 5);
            Assert.All(ports.AuthorisedCallers, caller => Assert.Equal("test-user", caller.SubjectId));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DeniedAuthorisationPreventsIndexCreation()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-server-denied-{Guid.NewGuid():N}");
        RecordingPorts ports = new() { AllowAuthorisation = false };
        try
        {
            using LocalServerCore server = await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = root }, ports.CreateSet());
            ServiceResult<IndexSummary> result = await server.CreateAsync(CreateRequest());
            Assert.Equal("forbidden", result.Failure?.Code);
            Assert.Empty(Directory.EnumerateDirectories(Path.Combine(root, "indices")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RejectedRoutingAndConsistencyStopEngineAccess()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-server-routing-{Guid.NewGuid():N}");
        RecordingPorts ports = new();
        try
        {
            using LocalServerCore server = await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = root }, ports.CreateSet());
            Assert.True((await server.CreateAsync(CreateRequest())).IsSuccess);
            ports.Route = new RejectedRoute("rejected for test");
            using JsonDocument document = JsonDocument.Parse("{\"content\":\"blocked\"}");
            Assert.Equal("route_unavailable", (await server.BulkAsync(new BulkDocumentsRequest("books", [new BulkDocumentOperation(DocumentOperationKind.Index, "one", document.RootElement.Clone())]))).Failure?.Code);

            ports.Route = new LocalRoute();
            ports.AllowConsistency = false;
            Assert.Equal("consistency_unavailable", (await server.SearchAsync("books", new SearchRequest(new TermQueryDefinition("content", "blocked")))).Failure?.Code);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static CreateIndexRequest CreateRequest() => new(
        "books",
        new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
        new IndexTopologySettings(1, 0),
        new MutableIndexSettings(null, null, "content", null));

    private sealed class RecordingPorts : IAuthenticationProvider, IAuthorisationService, IEntitlementEvaluator,
        IOperationRouter, IWriteAcknowledgementPolicy, IIndexLifecycleInterceptor, IAuditPublisher,
        IConsistencyPolicy, IInspectionFilter
    {
        internal bool AllowAuthorisation { get; set; } = true;
        internal bool AllowConsistency { get; set; } = true;
        internal OperationRoute Route { get; set; } = new LocalRoute();
        internal int AuthenticationCalls { get; private set; }
        internal int AuthorisationCalls { get; private set; }
        internal int EntitlementCalls { get; private set; }
        internal int RoutingCalls { get; private set; }
        internal int AcknowledgementCalls { get; private set; }
        internal int LifecycleCalls { get; private set; }
        internal int AuditCalls { get; private set; }
        internal int ConsistencyCalls { get; private set; }
        internal int InspectionCalls { get; private set; }
        internal List<CallerIdentity> AuthorisedCallers { get; } = [];

        internal ServerPortSet CreateSet() => new(this, this, this, this, this, this, this, this, this);

        public ValueTask<AuthenticationResult> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default)
        {
            AuthenticationCalls++;
            return ValueTask.FromResult(new AuthenticationResult(new CallerIdentity("test-user", "test", [], true), true));
        }

        public ValueTask<AuthorisationDecision> AuthoriseAsync(OperationPermission permission, CancellationToken cancellationToken = default)
        {
            AuthorisationCalls++;
            AuthorisedCallers.Add(permission.Context.Caller);
            return ValueTask.FromResult(new AuthorisationDecision(AllowAuthorisation));
        }

        public ValueTask<EntitlementDecision> EvaluateAsync(ServerFeature feature, CancellationToken cancellationToken = default)
        {
            EntitlementCalls++;
            return ValueTask.FromResult(new EntitlementDecision(true));
        }

        public ValueTask<OperationRoute> RouteAsync(OperationRouteRequest request, CancellationToken cancellationToken = default)
        {
            RoutingCalls++;
            return ValueTask.FromResult(Route);
        }

        public ValueTask<WriteAcknowledgement> AcknowledgeAsync(WriteCommitState state, CancellationToken cancellationToken = default)
        {
            AcknowledgementCalls++;
            return ValueTask.FromResult(new WriteAcknowledgement(true, state.IsDurable ? WriteDurability.LocalFsync : WriteDurability.Memory));
        }

        public ValueTask OnTransitionAsync(IndexLifecycleEvent transition, CancellationToken cancellationToken = default)
        {
            LifecycleCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            AuditCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<ConsistencyDecision> ResolveAsync(OperationContext context, RequestedConsistency requested, CancellationToken cancellationToken = default)
        {
            ConsistencyCalls++;
            return ValueTask.FromResult(new ConsistencyDecision(AllowConsistency, RequestedConsistency.Local));
        }

        public ValueTask<InspectionDecision> EvaluateAsync(OperationContext context, InspectionRequest request, CancellationToken cancellationToken = default)
        {
            InspectionCalls++;
            return ValueTask.FromResult(new InspectionDecision(true, 100));
        }
    }
}
