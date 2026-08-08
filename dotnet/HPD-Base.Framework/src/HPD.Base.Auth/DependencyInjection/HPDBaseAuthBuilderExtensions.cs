using HPD.Base.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        return builder.Use(new Installer(configure));
    }

    private sealed class Installer(Action<HPDBaseAuthOptions>? configure) : IHPDBaseBuilderExtension
    {
        public string Id => "hpdAuth";
        public bool IsRecordProvider => false;
        public bool SupportsRequiredIndexes => false;

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
            services.AddOptions();
            services.TryAddSingleton(options);
            services.TryAddSingleton<IOptions<HPDBaseAuthOptions>>(Options.Create(options));
            services.TryAddSingleton<HPDBaseAuthSubjectProjector>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPolicyEvaluator, HPDBaseAuthPolicyEvaluator>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, HPDBaseAuthDescriptorContributor>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, HPDBaseAuthHealthContributor>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, HPDBaseAuthDiagnosticContributor>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHPDBaseAuthHostIntegrationStatus, HPDBaseAuthAspNetCoreHostIntegrationStatus>());
            if (options.EnrichFromUserManager)
                services.TryAddEnumerable(ServiceDescriptor.Scoped<IHPDBaseAuthPrincipalEnricher, HPDBaseAuthUserManagerPrincipalEnricher>());
            services.Replace(ServiceDescriptor.Scoped<IBaseHttpPrincipalMapper, HPDBaseAuthHttpPrincipalMapper>());
            services.Replace(ServiceDescriptor.Scoped<IBaseHttpCorrelationProvider, HPDBaseAuthCorrelationProvider>());
            return services;
    }
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
        if (copy.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl)) ||
            copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Values must be distinct bounded visible strings.", name);
        return copy;
    }
}
