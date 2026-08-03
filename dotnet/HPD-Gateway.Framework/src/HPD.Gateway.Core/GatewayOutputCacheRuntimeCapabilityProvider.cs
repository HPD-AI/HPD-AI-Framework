using System.Collections.Immutable;

namespace HPD.Gateway.Core;

internal interface IGatewayOutputCacheRuntimeCapabilityProvider
{
    ImmutableDictionary<string, OutputCacheCapability> Capabilities { get; }
}
