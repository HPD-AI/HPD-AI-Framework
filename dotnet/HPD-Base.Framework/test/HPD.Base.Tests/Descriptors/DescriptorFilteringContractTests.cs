using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Descriptors;

public sealed class DescriptorFilteringContractTests
{
    [Fact]
    public void DescriptorTypesExposeSharedVisibilityLevel()
    {
        Assert.Equal(typeof(VisibilityLevel), typeof(BaseManifest).GetProperty(nameof(BaseManifest.Visibility))!.PropertyType);
        Assert.Equal(typeof(VisibilityLevel), typeof(RouteDescriptor).GetProperty(nameof(RouteDescriptor.Visibility))!.PropertyType);
        Assert.Equal(typeof(VisibilityLevel), typeof(CapabilityFeatureDescriptor).GetProperty(nameof(CapabilityFeatureDescriptor.Visibility))!.PropertyType);
    }
}
