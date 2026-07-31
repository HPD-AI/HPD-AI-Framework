
namespace HPD.Base;

internal sealed class BaseMutationDescriptorContributor : IBaseDescriptorContributor
{
    public string Id => "hpd.base.runtime.mutations";

    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddCapabilities(new CapabilityDescriptor
        {
            DescriptorVersion = "1.0",
            RuntimeId = "hpd.base.runtime",
            Families =
            [
                new CapabilityFamilyDescriptor
                {
                    FamilyId = BaseCapabilityFamilies.Batch,
                    FamilyVersion = "1.0",
                    Status = CapabilityStatus.Available,
                    Features =
                    [
                        Feature(BaseFeatureIds.RecordsBatch),
                        Feature(BaseFeatureIds.RecordsUpsert),
                        Feature(BaseFeatureIds.BatchOrderedIndependent),
                        Feature(BaseFeatureIds.BatchOrderedStopOnFailure),
                        Feature(BaseFeatureIds.BatchAtomic),
                        Feature(BaseFeatureIds.BatchPartialResults)
                    ]
                }
            ]
        });
    }

    private static CapabilityFeatureDescriptor Feature(string featureId) => new()
    {
        FeatureId = featureId,
        Version = "1.0",
        Status = CapabilityStatus.Available,
        SupportLevel = SupportLevel.Required,
        Scope = CapabilityScope.Runtime,
        Visibility = VisibilityLevel.Public
    };
}
