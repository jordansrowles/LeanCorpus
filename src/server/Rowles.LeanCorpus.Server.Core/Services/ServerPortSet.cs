using Rowles.LeanCorpus.Server.Abstractions.Community;
using Rowles.LeanCorpus.Server.Abstractions.Ports;

namespace Rowles.LeanCorpus.Server.Core.Services;

/// <summary>Groups the replaceable server ports used by local Core operations.</summary>
public sealed record ServerPortSet(
    IOperationRouter Router,
    IAuthorisationService Authorisation,
    IEntitlementEvaluator Entitlements,
    IWriteAcknowledgementPolicy WriteAcknowledgements,
    IIndexLifecycleInterceptor Lifecycle,
    IAuditPublisher Audit,
    IConsistencyPolicy Consistency,
    IInspectionFilter Inspection,
    IAuthenticationProvider Authentication)
{
    /// <summary>Gets the Community default port implementations.</summary>
    public static ServerPortSet Community { get; } = new(
        new CommunityOperationRouter(),
        new CommunityAuthorisationService(),
        new CommunityEntitlementEvaluator(),
        new CommunityWriteAcknowledgementPolicy(),
        new CommunityIndexLifecycleInterceptor(),
        new CommunityAuditPublisher(),
        new CommunityConsistencyPolicy(),
        new CommunityInspectionFilter(),
        new CommunityAuthenticationProvider());
}
