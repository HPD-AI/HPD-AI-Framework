using System.Collections.Immutable;

namespace HPD.Gateway;

internal interface IGatewayOutputCacheRuntimeCapabilityProvider
{
    ImmutableDictionary<string, OutputCacheCapability> Capabilities { get; }
}
