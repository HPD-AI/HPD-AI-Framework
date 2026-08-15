using HPD.Base.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace HPD.Base.Auth;

/// <summary>Adds the official HPD.Auth composition to HPD.BASE.</summary>
public static class HPDBaseAuthBuilderExtensions
{
    /// <summary>Adds Runtime policy and ASP.NET control-plane identity composition.</summary>
    public static HPDBaseBuilder AddHPDAuth(
        this HPDBaseBuilder builder,
        Action<HPDBaseAuthOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddPolicyAuthorityFromServices<HPDBaseAuthPolicyEvaluator>(new BasePolicyAuthorityDefinition
        {
            Id = "hpd.auth.base.policy", Version = 1, OwningModuleId = "hpd.auth",
            EvaluatorContractId = "hpd.auth.base.policy-evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
        });
        return builder.Use(new Installer(configure));
    }

    private sealed class Installer(Action<HPDBaseAuthOptions>? configure) : IHPDBaseBuilderExtension
    {
        public string Id => "hpdAuth";

        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections)
        {
            var options = new HPDBaseAuthOptions();
            configure?.Invoke(options);
            services.AddHPDBaseAuthServices(options);
        }
    }

    internal static IServiceCollection AddHPDBaseAuthServices(
        this IServiceCollection services,
        Action<HPDBaseAuthOptions>? configure = null)
    {
        var options = new HPDBaseAuthOptions();
        configure?.Invoke(options);
        return services.AddHPDBaseAuthServices(options);
    }

    private static IServiceCollection AddHPDBaseAuthServices(this IServiceCollection services, HPDBaseAuthOptions options)
    {
            HPDBaseAuthOptionsValidator.ValidateAndFreeze(options);
            HPDBaseAuthSnapshot snapshot = HPDBaseAuthSnapshot.Create(options);
            if (services.Any(descriptor => descriptor.ServiceType == typeof(IOptions<HPDBaseAuthOptions>) ||
                descriptor.ServiceType == typeof(HPDBaseAuthOptions) || descriptor.ServiceType == typeof(HPDBaseAuthSnapshot)))
                throw new InvalidOperationException("base.auth.options.ambiguous");
            services.AddOptions();
            services.AddSingleton(snapshot);
            services.TryAddSingleton<HPDBaseAuthSubjectProjector>();
            services.TryAddSingleton<HPDBaseAuthPolicyEvaluator>();
            services.AddSingleton<IPolicyEvaluator>(provider => provider.GetRequiredService<HPDBaseAuthPolicyEvaluator>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, HPDBaseAuthDescriptorContributor>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, HPDBaseAuthHealthContributor>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, HPDBaseAuthDiagnosticContributor>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHPDBaseAuthHostIntegrationStatus, HPDBaseAuthAspNetCoreHostIntegrationStatus>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHPDBaseEndpointSecurityMetadataValidator, HPDBaseControlPlaneMetadataValidator>());
            if (snapshot.EnrichFromUserManager)
                services.TryAddEnumerable(ServiceDescriptor.Scoped<IHPDBaseAuthPrincipalEnricher, HPDBaseAuthUserManagerPrincipalEnricher>());
            services.Replace(ServiceDescriptor.Scoped<IBaseHttpPrincipalMapper, HPDBaseAuthHttpPrincipalMapper>());
            services.Replace(ServiceDescriptor.Scoped<IBaseHttpCorrelationProvider, HPDBaseAuthCorrelationProvider>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HPDBaseAuthPrincipalRegistrationValidator>());
            return services;
    }
}

internal sealed class HPDBaseAuthPrincipalRegistrationValidator(IServiceScopeFactory scopeFactory) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetServices<IBaseHttpPrincipalMapper>().Take(2).Count() != 1)
            throw new InvalidOperationException("base.auth.principal.ambiguous");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal static class HPDBaseAuthOptionsValidator
{
    internal static void ValidateAndFreeze(HPDBaseAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxClaims is < 0 or > 64 || options.MaxRoles is < 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(options), "Claim and role limits must be between zero and 64.");
        options.SubjectIdClaimTypes = Copy(options.SubjectIdClaimTypes, nameof(options.SubjectIdClaimTypes));
        options.DisplayNameClaimTypes = Copy(options.DisplayNameClaimTypes, nameof(options.DisplayNameClaimTypes));
        options.RoleClaimTypes = Copy(options.RoleClaimTypes, nameof(options.RoleClaimTypes));
        options.ServicePrincipalClaimTypes = Copy(options.ServicePrincipalClaimTypes, nameof(options.ServicePrincipalClaimTypes));
        options.CopiedClaimTypes = Copy(options.CopiedClaimTypes, nameof(options.CopiedClaimTypes));
        options.AdminRoleNames = Copy(options.AdminRoleNames, nameof(options.AdminRoleNames));
        options.TenantClaimType = CopyRequired(options.TenantClaimType, nameof(options.TenantClaimType));
        options.SubscriptionTierClaimType = CopyRequired(options.SubscriptionTierClaimType, nameof(options.SubscriptionTierClaimType));
        options.SessionIdClaimType = CopyRequired(options.SessionIdClaimType, nameof(options.SessionIdClaimType));
        options.CredentialIdClaimType = CopyOptional(options.CredentialIdClaimType, nameof(options.CredentialIdClaimType));
        string[] forbidden = ["token", "secret", "password", "credential", "authorization", "securitystamp", "recovery"];
        if (options.CopiedClaimTypes.Any(type => forbidden.Any(fragment => type.Contains(fragment, StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("Copied claim types contain a forbidden credential family.", nameof(options));
        options.CollectionRules = [.. options.CollectionRules ?? throw new ArgumentNullException(nameof(options.CollectionRules))];
        options.StaticGrants = [.. options.StaticGrants ?? throw new ArgumentNullException(nameof(options.StaticGrants))];
    }

    private static string[] Copy(string[]? source, string name)
    {
        if (source is null) throw new ArgumentNullException(name);
        string[] copy = source.Select(value => new string(value.AsSpan())).ToArray();
        if (copy.Any(value => string.IsNullOrWhiteSpace(value) || System.Text.Encoding.UTF8.GetByteCount(value) > 128 || value.Any(char.IsControl)) ||
            copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Values must be distinct bounded visible strings.", name);
        return copy;
    }

    private static string CopyRequired(string? value, string name) =>
        Copy([value ?? throw new ArgumentNullException(name)], name)[0];

    private static string? CopyOptional(string? value, string name) =>
        value is null ? null : Copy([value], name)[0];
}
