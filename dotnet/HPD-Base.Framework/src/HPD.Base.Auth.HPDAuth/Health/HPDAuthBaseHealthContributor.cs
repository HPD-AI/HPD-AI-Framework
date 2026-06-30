using HPD.Base.Auth.HPDAuth.Configuration;
using HPD.Base.Auth.HPDAuth.Descriptors;
using HPD.Base.Auth.HPDAuth.Policy;
using HPD.Base.Health;
using HPD.Base.Runtime.Health;
using Microsoft.Extensions.Options;

namespace HPD.Base.Auth.HPDAuth.Health;

/// <summary>
/// Reports health for the HPD.Auth BASE adapter registration.
/// </summary>
public sealed class HPDAuthBaseHealthContributor : IBaseHealthContributor
{
    /// <inheritdoc />
    public string Id => HPDAuthBaseHealthIds.Registration;

    /// <inheritdoc />
    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<HealthDescriptor[]>(
        [
            new HealthDescriptor
            {
                Id = HPDAuthBaseHealthIds.Registration,
                Scope = HealthScope.Module,
                TargetRef = HPDAuthBaseIds.Module,
                Status = HealthStatus.Healthy,
                CheckedAt = DateTimeOffset.UtcNow,
                Summary = "HPD.Auth adapter services are registered.",
                PublicSafe = true,
                Visibility = VisibilityLevel.Public
            }
        ]);
    }
}

/// <summary>
/// Reports diagnostics for the HPD.Auth BASE adapter registration.
/// </summary>
public sealed class HPDAuthBaseDiagnosticContributor : IBaseDiagnosticContributor
{
    private readonly HPDBaseHPDAuthOptions _options;
    private readonly IEnumerable<IHPDAuthBaseHostIntegrationStatus> _hostStatuses;
    private readonly IEnumerable<IHPDAuthBaseGrantProvider> _grantProviders;

    /// <summary>
    /// Initializes a new instance of the <see cref="HPDAuthBaseDiagnosticContributor"/> class.
    /// </summary>
    /// <param name="options">Adapter options.</param>
    /// <param name="hostStatuses">Host integration status providers.</param>
    /// <param name="grantProviders">Registered grant providers.</param>
    public HPDAuthBaseDiagnosticContributor(
        IOptions<HPDBaseHPDAuthOptions> options,
        IEnumerable<IHPDAuthBaseHostIntegrationStatus> hostStatuses,
        IEnumerable<IHPDAuthBaseGrantProvider> grantProviders)
    {
        _options = options.Value;
        _hostStatuses = hostStatuses;
        _grantProviders = grantProviders;
    }

    /// <inheritdoc />
    public string Id => HPDAuthBaseDiagnosticIds.MissingAuthServices;

    /// <inheritdoc />
    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<DiagnosticDescriptor>();
        var statuses = _hostStatuses.ToArray();
        if (_options.RequireHPDAuthServices && !statuses.Any(static status => status.HPDAuthServicesDetected))
            diagnostics.Add(MissingAuthServices(statuses));

        if (_grantProviders.FirstOrDefault() is null
            && _options.StaticGrants.Length == 0
            && _options.CollectionRules.Length == 0)
        {
            diagnostics.Add(new DiagnosticDescriptor
            {
                Id = HPDAuthBaseDiagnosticIds.NoGrantProvider,
                Code = HPDAuthBaseDiagnosticIds.NoGrantProvider,
                Severity = DiagnosticSeverity.Info,
                TargetRef = HPDAuthBaseIds.Module,
                Message = "No HPD.Auth BASE grant provider, static grant, or collection rule is configured.",
                PublicMessage = "HPD.Auth BASE authorization rules are not configured.",
                Category = DiagnosticCategory.Policy,
                Remediation = "Register IHPDAuthBaseGrantProvider or configure CollectionRules/StaticGrants.",
                RelatedFeatureIds = [HPDAuthBaseFeatureIds.GrantProvider, HPDAuthBaseFeatureIds.PolicyEvaluator],
                Visibility = VisibilityLevel.Admin,
                EmittedAt = DateTimeOffset.UtcNow
            });
        }

        return ValueTask.FromResult(diagnostics.ToArray());
    }

    private static DiagnosticDescriptor MissingAuthServices(IHPDAuthBaseHostIntegrationStatus[] statuses)
    {
        var missing = statuses
            .SelectMany(static status => status.MissingRequiredServiceNames)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var missingMessage = missing.Length == 0
            ? "No HPD.Auth host integration status reported required services."
            : $"Missing HPD.Auth services: {string.Join(", ", missing)}.";

        return new DiagnosticDescriptor
        {
            Id = HPDAuthBaseDiagnosticIds.MissingAuthServices,
            Code = HPDAuthBaseDiagnosticIds.MissingAuthServices,
            Severity = DiagnosticSeverity.Info,
            TargetRef = HPDAuthBaseIds.Module,
            Message = $"HPD.Auth host services were not fully detected for the BASE HPD.Auth adapter. {missingMessage}",
            PublicMessage = "HPD.Auth integration is not fully detected.",
            Category = DiagnosticCategory.Policy,
            Remediation = "Call AddHPDAuth() before AddHPDBaseHPDAuthAspNetCore(), or set RequireHPDAuthServices to false for claim-only hosts.",
            RelatedFeatureIds = [HPDAuthBaseFeatureIds.PrincipalMap, HPDAuthBaseFeatureIds.PolicyEvaluator],
            Visibility = VisibilityLevel.Admin,
            EmittedAt = DateTimeOffset.UtcNow
        };
    }
}

/// <summary>
/// Names HPD.Auth adapter health ids.
/// </summary>
public static class HPDAuthBaseHealthIds
{
    /// <summary>
    /// Registration health id.
    /// </summary>
    public const string Registration = "hpd.base.auth.hpd-auth.registration";
}

/// <summary>
/// Names HPD.Auth adapter diagnostic ids.
/// </summary>
public static class HPDAuthBaseDiagnosticIds
{
    /// <summary>
    /// Missing auth services diagnostic id.
    /// </summary>
    public const string MissingAuthServices = "hpd.base.auth.hpd-auth.missingAuthServices";

    /// <summary>
    /// No grant provider diagnostic id.
    /// </summary>
    public const string NoGrantProvider = "hpd.base.auth.hpd-auth.noGrantProvider";

    /// <summary>
    /// Tenant claim missing diagnostic id.
    /// </summary>
    public const string TenantClaimMissing = "hpd.base.auth.hpd-auth.tenantClaimMissing";

    /// <summary>
    /// Admin policy not configured diagnostic id.
    /// </summary>
    public const string AdminPolicyNotConfigured = "hpd.base.auth.hpd-auth.adminPolicyNotConfigured";

    /// <summary>
    /// Unsupported policy condition diagnostic id.
    /// </summary>
    public const string UnsupportedCondition = "hpd.base.auth.hpd-auth.unsupportedCondition";

    /// <summary>
    /// ASP.NET principal mapper not registered diagnostic id.
    /// </summary>
    public const string AspNetMapperNotRegistered = "hpd.base.auth.hpd-auth.aspnetMapperNotRegistered";
}
