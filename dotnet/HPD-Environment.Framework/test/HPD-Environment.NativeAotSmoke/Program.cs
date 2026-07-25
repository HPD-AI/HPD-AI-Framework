using HPD.Environment.AppleVirtualization;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

var options = new AppleVirtualizationProviderOptions
{
    HelperTransportMode = AppleVirtualizationHelperTransportMode.InMemoryFake,
    FeatureGates = new AppleVirtualizationProviderFeatureGates
    {
        EnableInMemoryFakeHelper = true,
        EnableEngineControlPlane = true,
    },
};

var registry = new EnvironmentProviderRegistry();
registry.RegisterModule(new AppleVirtualizationProviderModule(options));

ProviderDescriptor? descriptor =
    await registry.GetAsync(AppleVirtualizationProviderDescriptor.ProviderId);
ProviderCapabilityReport capabilities =
    await registry.GetCapabilitiesAsync(AppleVirtualizationProviderDescriptor.ProviderId);

bool valid =
    descriptor is not null &&
    descriptor.Id.Equals(AppleVirtualizationProviderDescriptor.ProviderId) &&
    capabilities.ProviderId.Equals(AppleVirtualizationProviderDescriptor.ProviderId) &&
    registry.RuntimeHostProviders.Count == 1 &&
    registry.ExecutionUnitProviders.Count == 1 &&
    registry.ProcessProviders.Count == 1 &&
    registry.AuthorityBindingProviders.Count == 1 &&
    registry.EngineControlPlaneProviders.Count == 1 &&
    registry.JsonTypes.Count > 0 &&
    registry.JsonTypes.Any(registration =>
        registration.Type == typeof(ProviderDescriptor)) &&
    registry.JsonTypes.Any(registration =>
        registration.Type == typeof(AppleVirtualizationProviderOptions));

if (!valid)
{
    Console.Error.WriteLine("HPD Environment NativeAOT smoke validation failed.");
    return 1;
}

Console.WriteLine(
    $"HPD Environment NativeAOT smoke passed with {registry.JsonTypes.Count} generated JSON registrations.");
return 0;
