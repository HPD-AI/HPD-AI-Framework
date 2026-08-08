using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace HPD.Base.AspNetCore;

internal sealed class HPDBaseEndpointInventoryValidator(IEnumerable<EndpointDataSource> dataSources) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Endpoint endpoint in dataSources.SelectMany(static source => source.Endpoints))
        {
            HPDBaseEndpointDescriptor[] descriptors = endpoint.Metadata.GetOrderedMetadata<HPDBaseEndpointDescriptor>().ToArray();
            if (descriptors.Length == 0)
                continue;
            if (descriptors.Length != 1)
                Fail("base.http.endpoint.descriptorDuplicate");
            HPDBaseEndpointDescriptor descriptor = descriptors[0];
            if (!ValidId(descriptor.EndpointId) || !ids.Add(descriptor.EndpointId))
                Fail("base.http.endpoint.idDuplicate");
            bool publicEndpoint = descriptor.Audience == HPDBaseEndpointAudience.Public;
            if (publicEndpoint != (descriptor.Capability is null) ||
                (!publicEndpoint && !ValidCapability(descriptor.Capability!)))
                Fail("base.http.endpoint.capabilityInvalid");
            if (!publicEndpoint && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                Fail("base.http.endpoint.anonymous");
            if (descriptor.Audience == HPDBaseEndpointAudience.Application &&
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
                Fail("base.http.endpoint.audienceConflict");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool ValidId(string value) => value is { Length: > 0 and <= 128 }
        && value.All(static character => character is >= '!' and <= '~');
    private static bool ValidCapability(string value) => value is { Length: >= 3 and <= 128 }
        && value[0] is >= 'a' and <= 'z'
        && value[^1] is (>= 'a' and <= 'z') or (>= '0' and <= '9')
        && value.All(static character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-')
        && !value.Contains("..", StringComparison.Ordinal);
    private static void Fail(string code) => throw new InvalidOperationException(code);
}
