using HPD.Environment.AppleVirtualization;
using HPD.Environment.Contracts;
using HPD.Environment.Local;
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
registry.RegisterModule(new LocalEnvironmentProviderModule(
    new LocalEnvironmentProviderOptions
    {
        EnableWellKnownSocketDiscovery = false,
    }));

ProviderDescriptor? appleDescriptor =
    await registry.GetAsync(AppleVirtualizationProviderDescriptor.ProviderId);
ProviderCapabilityReport appleCapabilities =
    await registry.GetCapabilitiesAsync(AppleVirtualizationProviderDescriptor.ProviderId);
ProviderDescriptor? localDescriptor =
    await registry.GetAsync(LocalEnvironmentProviderDescriptor.ProviderId);
ProviderCapabilityReport localCapabilities =
    await registry.GetCapabilitiesAsync(LocalEnvironmentProviderDescriptor.ProviderId);

bool valid =
    appleDescriptor is not null &&
    appleDescriptor.Id.Equals(AppleVirtualizationProviderDescriptor.ProviderId) &&
    appleCapabilities.ProviderId.Equals(AppleVirtualizationProviderDescriptor.ProviderId) &&
    localDescriptor is not null &&
    localDescriptor.Id.Equals(LocalEnvironmentProviderDescriptor.ProviderId) &&
    localCapabilities.ProviderId.Equals(LocalEnvironmentProviderDescriptor.ProviderId) &&
    registry.RuntimeHostProviders.Count == 2 &&
    registry.ExecutionUnitProviders.Count == 2 &&
    registry.ProcessProviders.Count == 2 &&
    registry.AuthorityBindingProviders.Count == 2 &&
    registry.EngineControlPlaneProviders.Count == 2 &&
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
    $"HPD Environment NativeAOT smoke passed for Apple and Local with {registry.JsonTypes.Count} generated JSON registrations.");
return 0;
