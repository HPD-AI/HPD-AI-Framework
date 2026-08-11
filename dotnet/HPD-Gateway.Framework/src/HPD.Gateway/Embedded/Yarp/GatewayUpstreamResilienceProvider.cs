using System.Collections.Immutable;

namespace HPD.Gateway;

internal abstract class GatewayUpstreamResilienceProvider
{
    internal GatewayUpstreamResilienceProvider() { }

    internal abstract ImmutableArray<UpstreamResilienceCapability> Capabilities { get; }

    internal abstract bool IsInstalled(string name, int version);

    internal abstract HttpMessageHandler Wrap(string name, int version, HttpMessageHandler inner);
}
