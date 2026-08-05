using System.Globalization;

namespace HPD.Base;

internal sealed class BaseLiveQueryDescriptorContributor(
    BaseLiveQueryOptions options) : IBaseDescriptorContributor
{
    /// <summary>Gets the ID.</summary>
    public string Id => BaseLiveQueryModuleIds.Module;

    /// <summary>Executes the contribute operation.</summary>
    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        builder.AddModule(new BaseModuleDescriptor
        {
            Id = BaseLiveQueryModuleIds.Module,
            Name = "HPD.Base",
            Kind = BaseModuleKind.Custom,
            Version = "1.0.0",
            Status = ModuleStatus.Installed,
            Compatibility = new ModuleCompatibility { RequiresBaseContract = "1.0" },
            ContributedCapabilities =
            [
                BaseLiveQueryFeatureIds.ServerRerun,
                BaseLiveQueryFeatureIds.CommittedInvalidation
            ],
            Visibility = VisibilityLevel.Public
        });

        builder.AddCapabilities(new CapabilityDescriptor
        {
            DescriptorVersion = "1.0",
            RuntimeId = BaseLiveQueryModuleIds.Module,
            Families =
            [
                new CapabilityFamilyDescriptor
                {
                    FamilyId = "base.liveQuery",
                    FamilyVersion = "1.0",
                    Status = CapabilityStatus.Available,
                    OwnerModuleId = BaseLiveQueryModuleIds.Module,
                    Visibility = VisibilityLevel.Public,
                    Features =
                    [
                        Feature(BaseLiveQueryFeatureIds.ServerRerun),
                        Feature(BaseLiveQueryFeatureIds.CommittedInvalidation)
                    ],
                    Limits =
                    [
                        Limit("activeSubscriptions", options.MaxActiveSubscriptions, "subscriptions"),
                        Limit("dependenciesPerEvaluation", options.MaxDependenciesPerEvaluation, "references"),
                        Limit(
                            "evaluationDuration",
                            checked((int)options.MaxEvaluationDuration.TotalMilliseconds),
                            "milliseconds")
                    ]
                }
            ]
        });
    }

    private static CapabilityFeatureDescriptor Feature(string id) => new()
    {
        FeatureId = id,
        Version = "1.0",
        Status = CapabilityStatus.Available,
        SupportLevel = SupportLevel.Required,
        Scope = CapabilityScope.Runtime,
        Visibility = VisibilityLevel.Public
    };

    private static CapabilityLimitDescriptor Limit(string name, int value, string unit) => new()
    {
        Name = name,
        Value = value.ToString(CultureInfo.InvariantCulture),
        Unit = unit
    };
}
