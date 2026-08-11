using HPD.Gateway;
using HPD.Gateway.ControlPlane;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace HPD.Gateway.ControlPlane;

internal static class GatewayAdminOpenApiMetadata
{
    internal static void Apply(EndpointBuilder builder, GatewayAdminEndpointDescriptor descriptor)
    {
        for (int index = builder.Metadata.Count - 1; index >= 0; index--)
            if (builder.Metadata[index] is IExcludeFromDescriptionMetadata)
                builder.Metadata.RemoveAt(index);
        builder.Metadata.Add(new IncludeInDescriptionMetadata());
        GatewayAdminClientOperationSemantics semantics = GatewayAdminClientSemanticLedger.For(descriptor.Operation);
        Type? request = semantics.RequestType;
        if (request is not null)
            builder.Metadata.Add(new AcceptsMetadata(
                ["application/json", "application/hpd.gateway+json"], request,
                semantics.RequestBodyPresence == GatewayAdminClientRequestBodyPresence.Optional));

        builder.Metadata.Add(new ProducesMetadata(semantics.SuccessType, semantics.SuccessStatus, "application/json"));

        foreach (int status in ErrorStatuses(descriptor.Operation))
            builder.Metadata.Add(new ProducesMetadata(typeof(GatewayAdminError), status, "application/json"));
    }

    internal static IEnumerable<int> ErrorStatuses(string operation) =>
        GatewayAdminClientSemanticLedger.For(operation).DocumentedErrors;

    private sealed record ProducesMetadata(Type Type, int StatusCode, string ContentType)
        : IProducesResponseTypeMetadata
    {
        public string? Description => null;
        public IEnumerable<string> ContentTypes => [ContentType];
    }

    private sealed record IncludeInDescriptionMetadata : IExcludeFromDescriptionMetadata
    {
        public bool ExcludeFromDescription => false;
    }
}
