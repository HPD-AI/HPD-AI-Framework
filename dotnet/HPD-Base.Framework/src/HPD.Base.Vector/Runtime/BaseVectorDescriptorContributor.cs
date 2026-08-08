namespace HPD.Base;

internal sealed class BaseVectorDescriptorContributor(HPDBaseVectorSnapshot options) : IBaseDescriptorContributor
{
    public string Id => "hpd.base.vector";

    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        builder.AddModule(new BaseModuleDescriptor
        {
            Id = Id,
            Name = "HPD Base Vector",
            Kind = BaseModuleKind.Vector,
            Version = "1.0.0",
            Status = ModuleStatus.Installed,
            ContributedCapabilities = ["base.vector.query"],
            ContributedHealthRefIds = ["hpd.base.vector.provider"],
            ContributedDiagnosticIds = ["hpd.base.vector.configuration"],
            Visibility = VisibilityLevel.Public,
        });
        builder.AddHealthRef(new HealthRefDescriptor
        {
            Id = "hpd.base.vector.provider",
            Scope = HealthScope.Module,
            TargetRef = "hpd.base.vector.provider",
            Visibility = VisibilityLevel.Admin,
        });
        builder.AddDiagnosticRef(new DiagnosticRefDescriptor
        {
            Id = "hpd.base.vector.configuration",
            Visibility = VisibilityLevel.Admin,
        });
        builder.AddCapabilities(new CapabilityDescriptor
        {
            DescriptorVersion = "1.0",
            RuntimeId = Id,
            Families =
            [
                new CapabilityFamilyDescriptor
                {
                    FamilyId = "base.vector",
                    FamilyVersion = "1.0",
                    Status = CapabilityStatus.Available,
                    OwnerModuleId = Id,
                    Scopes = [CapabilityScope.Collection],
                    Visibility = VisibilityLevel.Public,
                    Features =
                    [
                        Feature("base.vector.query", VisibilityLevel.Public),
                        Feature("base.vector.exact", VisibilityLevel.Public),
                        Feature("base.vector.consistency", VisibilityLevel.Public),
                        Feature("base.vector.diagnostics", VisibilityLevel.Admin),
                    ],
                    Limits =
                    [
                        new CapabilityLimitDescriptor { Name = "maxDimensions", Value = options.MaxDimensions.ToString(System.Globalization.CultureInfo.InvariantCulture), Unit = "elements" },
                        new CapabilityLimitDescriptor { Name = "maxTopK", Value = options.MaxTopK.ToString(System.Globalization.CultureInfo.InvariantCulture), Unit = "records" },
                        new CapabilityLimitDescriptor { Name = "maxFilterFields", Value = options.MaxFilterFields.ToString(System.Globalization.CultureInfo.InvariantCulture), Unit = "fields" },
                    ],
                },
            ],
        });
    }

    private static CapabilityFeatureDescriptor Feature(string id, VisibilityLevel visibility) => new()
    {
        FeatureId = id,
        Version = "1.0",
        Status = CapabilityStatus.Available,
        SupportLevel = SupportLevel.Required,
        Scope = CapabilityScope.Collection,
        HealthRef = "hpd.base.vector.provider",
        DiagnosticRefs = ["hpd.base.vector.configuration"],
        Visibility = visibility,
    };
}
