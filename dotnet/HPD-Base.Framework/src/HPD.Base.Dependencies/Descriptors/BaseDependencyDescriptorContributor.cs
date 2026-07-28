using System.Text.Json;
using HPD.Base.Dependencies.Serialization;
using HPD.Base.Descriptors;
using HPD.Base.Runtime.Descriptors;

namespace HPD.Base.Dependencies.Descriptors;

internal sealed class BaseDependencyDescriptorContributor(
    IBaseDependencyTemplateProvider templates) : IBaseDescriptorContributor
{
    public string Id => BaseDependencyModuleIds.Module;

    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        var publicTemplates = templates.Templates
            .Where(static template => template.Visibility == BaseDependencyVisibility.Public)
            .ToArray();
        builder.AddModule(new BaseModuleDescriptor
        {
            Id = BaseDependencyModuleIds.Module,
            Name = "HPD.Base.Dependencies",
            Kind = BaseModuleKind.Custom,
            Version = "1.0.0",
            Status = ModuleStatus.Installed,
            Compatibility = new ModuleCompatibility { RequiresBaseContract = "1.0" },
            ContributedCapabilities =
            [
                BaseDependencyFeatureIds.OpaqueReferences,
                BaseDependencyFeatureIds.MutationInvalidation
            ],
            PublicConfig = new Dictionary<string, JsonElement>
            {
                ["templates"] = JsonSerializer.SerializeToElement(
                    publicTemplates,
                    HPDBaseDependenciesJsonSerializerContext.Default.BaseDependencyTemplateArray)
            },
            Visibility = VisibilityLevel.Public
        });

        builder.AddCapabilities(new CapabilityDescriptor
        {
            DescriptorVersion = "1.0",
            RuntimeId = BaseDependencyModuleIds.Module,
            Families =
            [
                new CapabilityFamilyDescriptor
                {
                    FamilyId = "base.dependencies",
                    FamilyVersion = "1.0",
                    Status = CapabilityStatus.Available,
                    OwnerModuleId = BaseDependencyModuleIds.Module,
                    Visibility = VisibilityLevel.Public,
                    Features =
                    [
                        Feature(BaseDependencyFeatureIds.OpaqueReferences),
                        Feature(BaseDependencyFeatureIds.MutationInvalidation)
                    ],
                    Limits =
                    [
                        new CapabilityLimitDescriptor
                        {
                            Name = "publicTemplateCount",
                            Value = publicTemplates.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            Unit = "templates"
                        }
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
}
