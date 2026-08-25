using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rowles.LeanCorpus.Server.AspNetCore.Authentication;
using Rowles.LeanCorpus.Server.Abstractions.Community;
using Rowles.LeanCorpus.Server.Abstractions.Ports;
using Rowles.LeanCorpus.Server.Abstractions.Services;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Services;

namespace Rowles.LeanCorpus.Server.AspNetCore.DependencyInjection;

/// <summary>Registers the reusable LeanCorpus Community server components.</summary>
public static class LeanCorpusServerServiceCollectionExtensions
{
    /// <summary>Registers the local Core services and Community policy defaults.</summary>
    public static IServiceCollection AddLeanCorpusServerCore(
        this IServiceCollection services,
        Action<ServerCoreOptions>? configure = null)
    {
        ServerCoreOptions options = new();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.TryAddSingleton<IOperationRouter, CommunityOperationRouter>();
        services.TryAddSingleton<IAuthorisationService, CommunityAuthorisationService>();
        services.TryAddSingleton<IEntitlementEvaluator, CommunityEntitlementEvaluator>();
        services.TryAddSingleton<IWriteAcknowledgementPolicy, CommunityWriteAcknowledgementPolicy>();
        services.TryAddSingleton<IIndexLifecycleInterceptor, CommunityIndexLifecycleInterceptor>();
        services.TryAddSingleton<IAuditPublisher, CommunityAuditPublisher>();
        services.TryAddSingleton<IConsistencyPolicy, CommunityConsistencyPolicy>();
        services.TryAddSingleton<IInspectionFilter, CommunityInspectionFilter>();
        services.TryAddSingleton<IAuthenticationProvider, CommunityAuthenticationProvider>();
        services.TryAddSingleton<ServerPortSet>(provider => new ServerPortSet(
            provider.GetRequiredService<IOperationRouter>(),
            provider.GetRequiredService<IAuthorisationService>(),
            provider.GetRequiredService<IEntitlementEvaluator>(),
            provider.GetRequiredService<IWriteAcknowledgementPolicy>(),
            provider.GetRequiredService<IIndexLifecycleInterceptor>(),
            provider.GetRequiredService<IAuditPublisher>(),
            provider.GetRequiredService<IConsistencyPolicy>(),
            provider.GetRequiredService<IInspectionFilter>(),
            provider.GetRequiredService<IAuthenticationProvider>()));

        services.AddSingleton<LocalServerCore>(provider =>
        {
            ServerCoreOptions configured = provider.GetRequiredService<ServerCoreOptions>();
            ServerPortSet ports = provider.GetRequiredService<ServerPortSet>();
            return LocalServerCore.OpenAsync(configured, ports).AsTask().GetAwaiter().GetResult();
        });
        services.AddSingleton<IIndexService>(provider => provider.GetRequiredService<LocalServerCore>());
        services.AddSingleton<IDocumentService>(provider => provider.GetRequiredService<LocalServerCore>());
        services.AddSingleton<ISearchService>(provider => provider.GetRequiredService<LocalServerCore>());
        services.AddSingleton<IHealthService>(provider => provider.GetRequiredService<LocalServerCore>());
        services.AddSingleton<IInspectionService>(provider => provider.GetRequiredService<LocalServerCore>());
        return services;
    }

    /// <summary>Registers ASP.NET Core transport integration without owning application composition.</summary>
    public static IServiceCollection AddLeanCorpusServerAspNetCore(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        // Register the transport adapter after Core's anonymous default. Hosts can
        // still replace it with their own provider by registering one afterwards.
        services.AddSingleton<IAuthenticationProvider, AspNetCoreAuthenticationProvider>();
        services.AddProblemDetails();
        return services;
    }
}
