using HPD.Base;
using HPD.Base.Auth;

namespace HPD.Base.Auth;

/// <summary>
/// Contributes HPD.Auth adapter descriptors to the BASE manifest.
/// </summary>
public sealed class HPDAuthBaseDescriptorContributor : IBaseDescriptorContributor
{
    /// <inheritdoc />
    public string Id => HPDAuthBaseIds.Module;

    /// <inheritdoc />
    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddModule(new BaseModuleDescriptor
        {
            Id = HPDAuthBaseIds.Module,
            Name = "HPD.BASE HPD.Auth Adapter",
            Kind = BaseModuleKind.Policy,
            Version = "1.0.0",
            Status = ModuleStatus.Installed,
            ContributedCapabilities = [HPDAuthBaseFeatureIds.PolicyEvaluator],
            ContributedHealthRefIds = [HPDAuthBaseHealthIds.Registration],
            ContributedDiagnosticIds =
            [
                HPDAuthBaseDiagnosticIds.MissingAuthServices,
                HPDAuthBaseDiagnosticIds.NoGrantProvider,
                HPDAuthBaseDiagnosticIds.TenantClaimMissing,
                HPDAuthBaseDiagnosticIds.AdminPolicyNotConfigured,
                HPDAuthBaseDiagnosticIds.UnsupportedCondition,
                HPDAuthBaseDiagnosticIds.AspNetMapperNotRegistered
            ],
            Visibility = VisibilityLevel.Public
        });

        builder.AddHealthRef(new HealthRefDescriptor
        {
            Id = HPDAuthBaseHealthIds.Registration,
            Scope = HealthScope.Module,
            TargetRef = HPDAuthBaseIds.Module,
            Visibility = VisibilityLevel.Public
        });

        builder.AddHealth(new HealthDescriptor
        {
            Id = HPDAuthBaseHealthIds.Registration,
            Scope = HealthScope.Module,
            TargetRef = HPDAuthBaseIds.Module,
            Status = HealthStatus.Healthy,
            CheckedAt = DateTimeOffset.UnixEpoch,
            Summary = "HPD.Auth adapter is registered.",
            PublicSafe = true,
            Visibility = VisibilityLevel.Public
        });

        foreach (var diagnosticId in DiagnosticIds)
        {
            builder.AddDiagnosticRef(new DiagnosticRefDescriptor
            {
                Id = diagnosticId,
                Visibility = VisibilityLevel.Admin
            });

            builder.AddDiagnostic(new DiagnosticDescriptor
            {
                Id = diagnosticId,
                Code = diagnosticId,
                Severity = DiagnosticSeverity.Info,
                TargetRef = HPDAuthBaseIds.Module,
                Message = "HPD.Auth adapter diagnostics are available.",
                PublicMessage = "HPD.Auth adapter diagnostics are available.",
                Category = DiagnosticCategory.Policy,
                Visibility = VisibilityLevel.Admin,
                EmittedAt = DateTimeOffset.UnixEpoch
            });
        }

        builder.AddCapabilities(new CapabilityDescriptor
        {
            DescriptorVersion = "1.0",
            RuntimeId = HPDAuthBaseIds.Module,
            Families =
            [
                new CapabilityFamilyDescriptor
                {
                    FamilyId = "auth.hpd-auth",
                    FamilyVersion = "1.0",
                    Status = CapabilityStatus.Available,
                    OwnerModuleId = HPDAuthBaseIds.Module,
                    Visibility = VisibilityLevel.Public,
                    Features =
                    [
                        Feature(HPDAuthBaseFeatureIds.PrincipalMap, VisibilityLevel.Public),
                        Feature(HPDAuthBaseFeatureIds.TenantMap, VisibilityLevel.Public),
                        Feature(HPDAuthBaseFeatureIds.SubjectMap, VisibilityLevel.Public),
                        Feature(HPDAuthBaseFeatureIds.PolicyEvaluator, VisibilityLevel.Public),
                        Feature(HPDAuthBaseFeatureIds.GrantProvider, VisibilityLevel.Admin)
                    ]
                }
            ]
        });
    }

    private static CapabilityFeatureDescriptor Feature(string id, VisibilityLevel visibility) => new()
    {
        FeatureId = id,
        Version = "1.0",
        Status = CapabilityStatus.Available,
        SupportLevel = SupportLevel.Optional,
        Scope = CapabilityScope.Runtime,
        HealthRef = HPDAuthBaseHealthIds.Registration,
        DiagnosticRefs = [HPDAuthBaseDiagnosticIds.MissingAuthServices],
        Visibility = visibility
    };

    private static string[] DiagnosticIds =>
    [
        HPDAuthBaseDiagnosticIds.MissingAuthServices,
        HPDAuthBaseDiagnosticIds.NoGrantProvider,
        HPDAuthBaseDiagnosticIds.TenantClaimMissing,
        HPDAuthBaseDiagnosticIds.AdminPolicyNotConfigured,
        HPDAuthBaseDiagnosticIds.UnsupportedCondition,
        HPDAuthBaseDiagnosticIds.AspNetMapperNotRegistered
    ];
}

/// <summary>
/// Names feature ids contributed by the HPD.Auth adapter.
/// </summary>
public static class HPDAuthBaseFeatureIds
{
    /// <summary>
    /// Principal mapping feature id.
    /// </summary>
    public const string PrincipalMap = "auth.hpd-auth.principal-map";

    /// <summary>
    /// Tenant mapping feature id.
    /// </summary>
    public const string TenantMap = "auth.hpd-auth.tenant-map";

    /// <summary>
    /// Subject mapping feature id.
    /// </summary>
    public const string SubjectMap = "auth.hpd-auth.subject-map";

    /// <summary>
    /// Policy evaluator feature id.
    /// </summary>
    public const string PolicyEvaluator = "auth.hpd-auth.policy-evaluator";

    /// <summary>
    /// Grant provider feature id.
    /// </summary>
    public const string GrantProvider = "auth.hpd-auth.grant-provider";
}
