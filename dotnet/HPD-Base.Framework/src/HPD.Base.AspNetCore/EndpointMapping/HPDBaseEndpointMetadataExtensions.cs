using Microsoft.AspNetCore.Builder;

namespace HPD.Base.AspNetCore;

internal static class HPDBaseEndpointMetadataExtensions
{
    internal static TBuilder WithHPDBaseEndpoint<TBuilder>(
        this TBuilder builder,
        string endpointId,
        HPDBaseEndpointAudience audience,
        HPDBaseEndpointOperation operation,
        string? capability = null,
        Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor>? convention = null)
        where TBuilder : IEndpointConventionBuilder
    {
        var descriptor = new HPDBaseEndpointDescriptor
        {
            EndpointId = endpointId,
            Audience = audience,
            Operation = operation,
            Capability = capability
        };
        TBuilder result = builder.WithMetadata(descriptor);
        convention?.Invoke(result, descriptor);
        return result;
    }
}
