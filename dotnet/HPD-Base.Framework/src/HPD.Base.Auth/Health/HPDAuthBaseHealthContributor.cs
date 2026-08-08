using HPD.Base.Auth;
using HPD.Base;
using Microsoft.Extensions.Logging;

namespace HPD.Base.Auth;

/// <summary>
/// Reports health for the HPD.Auth BASE adapter registration.
/// </summary>
internal sealed class HPDBaseAuthHealthContributor : IBaseHealthContributor
{
    /// <inheritdoc />
    public string Id => HPDBaseAuthHealthIds.Registration;

    /// <inheritdoc />
    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<HealthDescriptor[]>(
        [
            new HealthDescriptor
            {
                Id = HPDBaseAuthHealthIds.Registration,
                Scope = HealthScope.Module,
                TargetRef = HPDBaseAuthIds.Module,
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
internal sealed class HPDBaseAuthDiagnosticContributor : IBaseDiagnosticContributor
{
    private readonly HPDBaseAuthSnapshot _options;
    private readonly IEnumerable<IHPDBaseAuthHostIntegrationStatus> _hostStatuses;
    private readonly IEnumerable<IHPDBaseAuthGrantProvider> _grantProviders;
    private readonly ILogger<HPDBaseAuthDiagnosticContributor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HPDBaseAuthDiagnosticContributor"/> class.
    /// </summary>
    /// <param name="options">Adapter options.</param>
    /// <param name="hostStatuses">Host integration status providers.</param>
    /// <param name="grantProviders">Registered grant providers.</param>
    /// <param name="logger">The diagnostic contributor logger.</param>
    public HPDBaseAuthDiagnosticContributor(
        HPDBaseAuthSnapshot options,
        IEnumerable<IHPDBaseAuthHostIntegrationStatus> hostStatuses,
        IEnumerable<IHPDBaseAuthGrantProvider> grantProviders,
        ILogger<HPDBaseAuthDiagnosticContributor> logger)
    {
        _options = options;
        _hostStatuses = hostStatuses;
        _grantProviders = grantProviders;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => HPDBaseAuthDiagnosticIds.MissingAuthServices;

    /// <inheritdoc />
    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<DiagnosticDescriptor>();
        var statuses = _hostStatuses.ToArray();
        if (_options.RequireHPDAuthServices && !statuses.Any(static status => status.HPDAuthServicesDetected))
        {
            HPDBaseHPDAuthLog.AuthServicesUnavailable(_logger, HPDBaseAuthDiagnosticIds.MissingAuthServices);
            diagnostics.Add(MissingAuthServices(statuses));
        }

        if (_grantProviders.FirstOrDefault() is null
            && _options.StaticGrants.Length == 0
            && _options.CollectionRules.Length == 0)
        {
            HPDBaseHPDAuthLog.GrantConfigurationMissing(_logger, HPDBaseAuthDiagnosticIds.NoGrantProvider);
            diagnostics.Add(new DiagnosticDescriptor
            {
                Id = HPDBaseAuthDiagnosticIds.NoGrantProvider,
                Code = HPDBaseAuthDiagnosticIds.NoGrantProvider,
                Severity = DiagnosticSeverity.Info,
                TargetRef = HPDBaseAuthIds.Module,
                Message = "No HPD.Auth BASE grant provider, static grant, or collection rule is configured.",
                PublicMessage = "HPD.Auth BASE authorization rules are not configured.",
                Category = DiagnosticCategory.Policy,
                Remediation = "Register IHPDBaseAuthGrantProvider or configure CollectionRules/StaticGrants.",
                RelatedFeatureIds = [HPDBaseAuthFeatureIds.GrantProvider, HPDBaseAuthFeatureIds.PolicyEvaluator],
                Visibility = VisibilityLevel.Admin,
                EmittedAt = DateTimeOffset.UtcNow
            });
        }

        return ValueTask.FromResult(diagnostics.ToArray());
    }

    private static DiagnosticDescriptor MissingAuthServices(IHPDBaseAuthHostIntegrationStatus[] statuses)
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
            Id = HPDBaseAuthDiagnosticIds.MissingAuthServices,
            Code = HPDBaseAuthDiagnosticIds.MissingAuthServices,
            Severity = DiagnosticSeverity.Info,
            TargetRef = HPDBaseAuthIds.Module,
            Message = $"HPD.Auth host services were not fully detected for the BASE HPD.Auth adapter. {missingMessage}",
            PublicMessage = "HPD.Auth integration is not fully detected.",
            Category = DiagnosticCategory.Policy,
            Remediation = "Install AddHPDAuth(), or set RequireHPDAuthServices to false for claim-only hosts.",
            RelatedFeatureIds = [HPDBaseAuthFeatureIds.PrincipalMap, HPDBaseAuthFeatureIds.PolicyEvaluator],
            Visibility = VisibilityLevel.Admin,
            EmittedAt = DateTimeOffset.UtcNow
        };
    }
}

/// <summary>
/// Names HPD.Auth adapter health ids.
/// </summary>
public static class HPDBaseAuthHealthIds
{
    /// <summary>
    /// Registration health id.
    /// </summary>
    public const string Registration = "hpd.base.auth.hpd-auth.registration";
}

/// <summary>
/// Names HPD.Auth adapter diagnostic ids.
/// </summary>
public static class HPDBaseAuthDiagnosticIds
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
