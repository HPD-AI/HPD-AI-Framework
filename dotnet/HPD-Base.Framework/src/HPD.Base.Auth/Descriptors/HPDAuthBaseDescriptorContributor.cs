using HPD.Base;
using HPD.Base.Auth;

namespace HPD.Base.Auth;

/// <summary>
/// Contributes HPD.Auth adapter descriptors to the BASE manifest.
/// </summary>
internal sealed class HPDBaseAuthDescriptorContributor : IBaseDescriptorContributor
{
    /// <inheritdoc />
    public string Id => HPDBaseAuthIds.Module;

    /// <inheritdoc />
    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddModule(new BaseModuleDescriptor
        {
            Id = HPDBaseAuthIds.Module,
            Name = "HPD.BASE HPD.Auth Adapter",
            Kind = BaseModuleKind.Policy,
            Version = "1.0.0",
            Status = ModuleStatus.Installed,
            ContributedCapabilities = [HPDBaseAuthFeatureIds.PolicyEvaluator],
            ContributedHealthRefIds = [HPDBaseAuthHealthIds.Registration],
            ContributedDiagnosticIds =
            [
                HPDBaseAuthDiagnosticIds.MissingAuthServices,
                HPDBaseAuthDiagnosticIds.NoGrantProvider,
                HPDBaseAuthDiagnosticIds.TenantClaimMissing,
                HPDBaseAuthDiagnosticIds.AdminPolicyNotConfigured,
                HPDBaseAuthDiagnosticIds.UnsupportedCondition,
                HPDBaseAuthDiagnosticIds.AspNetMapperNotRegistered
            ],
            Visibility = VisibilityLevel.Public
        });

        builder.AddHealthRef(new HealthRefDescriptor
        {
            Id = HPDBaseAuthHealthIds.Registration,
            Scope = HealthScope.Module,
            TargetRef = HPDBaseAuthIds.Module,
            Visibility = VisibilityLevel.Public
        });

        builder.AddHealth(new HealthDescriptor
        {
            Id = HPDBaseAuthHealthIds.Registration,
            Scope = HealthScope.Module,
            TargetRef = HPDBaseAuthIds.Module,
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
                TargetRef = HPDBaseAuthIds.Module,
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
            RuntimeId = HPDBaseAuthIds.Module,
            Families =
            [
                new CapabilityFamilyDescriptor
                {
                    FamilyId = "auth.hpd-auth",
                    FamilyVersion = "1.0",
                    Status = CapabilityStatus.Available,
                    OwnerModuleId = HPDBaseAuthIds.Module,
                    Visibility = VisibilityLevel.Public,
                    Features =
                    [
                        Feature(HPDBaseAuthFeatureIds.PrincipalMap, VisibilityLevel.Public),
                        Feature(HPDBaseAuthFeatureIds.TenantMap, VisibilityLevel.Public),
                        Feature(HPDBaseAuthFeatureIds.SubjectMap, VisibilityLevel.Public),
                        Feature(HPDBaseAuthFeatureIds.PolicyEvaluator, VisibilityLevel.Public),
                        Feature(HPDBaseAuthFeatureIds.GrantProvider, VisibilityLevel.Admin)
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
        HealthRef = HPDBaseAuthHealthIds.Registration,
        DiagnosticRefs = [HPDBaseAuthDiagnosticIds.MissingAuthServices],
        Visibility = visibility
    };

    private static string[] DiagnosticIds =>
    [
        HPDBaseAuthDiagnosticIds.MissingAuthServices,
        HPDBaseAuthDiagnosticIds.NoGrantProvider,
        HPDBaseAuthDiagnosticIds.TenantClaimMissing,
        HPDBaseAuthDiagnosticIds.AdminPolicyNotConfigured,
        HPDBaseAuthDiagnosticIds.UnsupportedCondition,
        HPDBaseAuthDiagnosticIds.AspNetMapperNotRegistered
    ];
}

/// <summary>
/// Names feature ids contributed by the HPD.Auth adapter.
/// </summary>
public static class HPDBaseAuthFeatureIds
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
