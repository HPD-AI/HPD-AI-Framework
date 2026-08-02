
namespace HPD.Base;

internal sealed class PolicyAdminDescriptorContributor : IBaseDescriptorContributor
{
    /// <summary>Gets the ID.</summary>
    public string Id => "hpd.base.policy.admin";

    /// <summary>Executes the contribute operation.</summary>
    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        builder.AddModule(new BaseModuleDescriptor
        {
            Id = "hpd.base.policy.admin",
            Name = "HPD.Base Policy Admin",
            Kind = BaseModuleKind.Policy,
            Version = "1.0.0",
            Status = ModuleStatus.Installed,
            Compatibility = new ModuleCompatibility { RequiresBaseContract = "1.0" },
            ContributedCapabilities = ["policy.explain.admin"],
            ContributedHealthRefIds = ["hpd.base.policy.admin.registration"],
            ContributedDiagnosticIds =
            [
                "hpd.base.policy.admin.noPolicyEvaluator",
                "hpd.base.policy.admin.serviceGateMisconfigured",
                "hpd.base.policy.admin.writeCheckRuntimeUnsupported",
                "hpd.base.policy.admin.httpRouteNotMapped",
                "hpd.base.policy.admin.redactionStrictMode"
            ],
            Visibility = VisibilityLevel.Admin
        });

        builder.AddHealthRef(new HealthRefDescriptor
        {
            Id = "hpd.base.policy.admin.registration",
            Scope = HealthScope.Module,
            TargetRef = "hpd.base.policy.admin",
            Visibility = VisibilityLevel.Admin
        });

        builder.AddCapabilities(new CapabilityDescriptor
        {
            DescriptorVersion = "1.0",
            RuntimeId = "hpd.base.runtime",
            Families =
            [
                new CapabilityFamilyDescriptor
                {
                    FamilyId = "policy.admin",
                    FamilyVersion = "1.0",
                    Status = CapabilityStatus.Available,
                    OwnerModuleId = "hpd.base.policy.admin",
                    Visibility = VisibilityLevel.Admin,
                    Features =
                    [
                        new CapabilityFeatureDescriptor
                        {
                            FeatureId = "policy.explain.admin",
                            Version = "1.0",
                            Status = CapabilityStatus.Available,
                            SupportLevel = SupportLevel.Required,
                            Scope = CapabilityScope.Runtime,
                            HealthRef = "hpd.base.policy.admin.registration",
                            DiagnosticRefs =
                            [
                                "hpd.base.policy.admin.noPolicyEvaluator",
                                "hpd.base.policy.admin.serviceGateMisconfigured",
                                "hpd.base.policy.admin.writeCheckRuntimeUnsupported",
                                "hpd.base.policy.admin.httpRouteNotMapped",
                                "hpd.base.policy.admin.redactionStrictMode"
                            ],
                            Visibility = VisibilityLevel.Admin
                        }
                    ]
                }
            ]
        });

        foreach (var diagnosticId in new[]
        {
            "hpd.base.policy.admin.noPolicyEvaluator",
            "hpd.base.policy.admin.serviceGateMisconfigured",
            "hpd.base.policy.admin.writeCheckRuntimeUnsupported",
            "hpd.base.policy.admin.httpRouteNotMapped",
            "hpd.base.policy.admin.redactionStrictMode"
        })
        {
            builder.AddDiagnosticRef(new DiagnosticRefDescriptor
            {
                Id = diagnosticId,
                Visibility = VisibilityLevel.Admin
            });
        }
    }
}
