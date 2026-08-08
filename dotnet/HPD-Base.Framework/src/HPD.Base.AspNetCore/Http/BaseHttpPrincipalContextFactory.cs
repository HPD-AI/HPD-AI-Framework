using HPD.Base;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore;

internal sealed class BaseHttpPrincipalContextFactory(IEnumerable<IBaseHttpPrincipalMapper> mappers)
    : IBaseHttpPrincipalContextFactory
{
    public ValueTask<PrincipalContext> CreateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        HPDBaseEndpointDescriptor[] descriptors = httpContext.GetEndpoint()?.Metadata
            .GetOrderedMetadata<HPDBaseEndpointDescriptor>()
            .ToArray() ?? [];
        if (descriptors.Length != 1)
            throw new InvalidOperationException("The BASE endpoint descriptor is missing or ambiguous.");
        IBaseHttpPrincipalMapper[] resolved = mappers.Take(2).ToArray();
        if (resolved.Length != 1)
            throw new InvalidOperationException("base.auth.principal.ambiguous");
        return resolved[0].MapAsync(httpContext, descriptors[0], cancellationToken);
    }
}
