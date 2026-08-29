using HPD.Auth.Base;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Audit;
using HPD.Auth.Infrastructure.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Options;
using HPD.Auth.Core.Options;
using HPD.Base;

namespace HPD.Auth.Infrastructure.Base;

/// <summary>
/// Registers the HPD Base-backed persistence adapters owned by HPD Auth.
/// </summary>
public static class AuthBaseInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the closed Auth persistence adapters over an installed HPD Base application graph.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddHPDAuthBaseStores(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<AuthBaseRuntime>();
        services.TryAddSingleton<AuthDataProtectionCacheInvalidationState>();
        services.TryAddScoped<IUserStore<ApplicationUser>, AuthBaseUserStore>();
        services.TryAddScoped<IRoleStore<ApplicationRole>, AuthBaseRoleStore>();
        services.AddScoped<ISessionManager, AuthBaseSessionStore>();
        services.AddScoped<IRefreshTokenStore, AuthBaseRefreshTokenStore>();
        services.AddScoped<IAuthExternalIdentityProfileStore, AuthBaseExternalIdentityProfileStore>();
        services.AddScoped<IAuthAdminUserQuery, AuthBaseAdminUserQuery>();
        services.AddScoped<AuthAuditStore>();
        services.AddScoped<IAuthAuditWriter>(static provider => provider.GetRequiredService<AuthAuditStore>());
        services.AddScoped<IAuthAuditReader>(static provider => provider.GetRequiredService<AuthAuditStore>());
        services.TryAddSingleton(static provider =>
        {
            IBaseSessionFactory sessions = provider.GetService<IBaseSessionFactory>()
                ?? throw new OptionsValidationException(
                    nameof(HPDAuthOptions),
                    typeof(HPDAuthOptions),
                    ["HPD.Auth storage is required. Install AuthBaseModule in the HPD Base application graph."]);
            return new HPDBaseDataProtectionXmlRepository(
                sessions,
                provider.GetRequiredService<HPDAuthOptions>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<AuthDataProtectionCacheInvalidationState>());
        });
        services.TryAddSingleton<IXmlRepository>(static provider =>
            provider.GetRequiredService<HPDBaseDataProtectionXmlRepository>());
        services.TryAddSingleton<IAuthDataProtectionCacheRefresh>(static provider =>
            provider.GetRequiredService<HPDBaseDataProtectionXmlRepository>());
        services.AddSingleton<IHostedService>(static provider =>
            provider.GetRequiredService<HPDBaseDataProtectionXmlRepository>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseCommittedMutationObserver,
            AuthDataProtectionCommittedMutationObserver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseCommittedRestoreObserver,
            AuthDataProtectionCommittedRestoreObserver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<KeyManagementOptions>,
            AuthBaseDataProtectionKeyManagementOptionsSetup>());
        return services;
    }
}

internal sealed class AuthDataProtectionCommittedMutationObserver(
    AuthDataProtectionCacheInvalidationState invalidation) : IBaseCommittedMutationObserver
{
    public ValueTask ObserveAsync(
        BaseRecordMutationEvent mutation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(mutation.Resource.CollectionId,
                AuthDataProtectionKeyRecordV1.Collection.Id, StringComparison.Ordinal))
            invalidation.Invalidate();
        return ValueTask.CompletedTask;
    }
}

internal sealed class AuthDataProtectionCommittedRestoreObserver(
    AuthDataProtectionCacheInvalidationState invalidation) : IBaseCommittedRestoreObserver
{
    public ValueTask ObserveAsync(
        BaseRestoreResult restore,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        invalidation.Invalidate(restore.StoreId);
        return ValueTask.CompletedTask;
    }
}

internal sealed class AuthDataProtectionCacheInvalidationState
{
    private long _generation;
    private string? _logicalStoreId;

    internal long Generation => Volatile.Read(ref _generation);

    internal void Bind(string logicalStoreId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalStoreId);
        Interlocked.CompareExchange(ref _logicalStoreId, logicalStoreId, null);
    }

    internal void Invalidate() => Interlocked.Increment(ref _generation);

    internal void Invalidate(string logicalStoreId)
    {
        string? bound = Volatile.Read(ref _logicalStoreId);
        if (bound is null || string.Equals(bound, logicalStoreId, StringComparison.Ordinal))
            Invalidate();
    }
}

internal sealed class AuthBaseDataProtectionKeyManagementOptionsSetup(IXmlRepository repository)
    : IConfigureOptions<KeyManagementOptions>
{
    public void Configure(KeyManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.XmlRepository = repository;
    }
}
