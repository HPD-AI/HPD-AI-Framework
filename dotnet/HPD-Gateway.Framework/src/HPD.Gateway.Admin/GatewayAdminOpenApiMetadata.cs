using HPD.Gateway.Abstractions;
using HPD.Gateway.Effective;
using HPD.Gateway.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace HPD.Gateway.Admin;

internal static class GatewayAdminOpenApiMetadata
{
    internal static void Apply(EndpointBuilder builder, GatewayAdminEndpointDescriptor descriptor)
    {
        for (int index = builder.Metadata.Count - 1; index >= 0; index--)
            if (builder.Metadata[index] is IExcludeFromDescriptionMetadata)
                builder.Metadata.RemoveAt(index);
        builder.Metadata.Add(new IncludeInDescriptionMetadata());
        Type? request = descriptor.Operation switch
        {
            "validate" => typeof(GatewayConfiguration),
            "submit" or "submit-and-activate" => typeof(GatewayRevisionRequest),
            "activate" or "rollback" => typeof(GatewayActivationRequest),
            "compare" => typeof(GatewayCompareRequest),
            "import" or "import-and-activate" => typeof(GatewayImportRequest),
            "backup" => typeof(GatewayBackupRequest),
            "purge" => typeof(GatewayPurgeRequest),
            _ => null,
        };
        if (request is not null)
            builder.Metadata.Add(new AcceptsMetadata(
                ["application/json", "application/hpd.gateway+json"], request,
                descriptor.Operation is "activate" or "rollback"));

        (Type Type, int Status) success = descriptor.Operation switch
        {
            "capabilities" => (typeof(GatewayCapabilityCatalog), 200),
            "validate" => (typeof(GatewayValidationResponse), 200),
            "provision" => (typeof(GatewayProvisionResponse), 201),
            "desired" => (typeof(GatewayDesiredProjection), 200),
            "status" => (typeof(GatewayTargetStatusResponse), 200),
            "effective" => (typeof(GatewayEffectiveSnapshot), 200),
            "submit" or "import" => (typeof(GatewayRevisionResponse), 201),
            "submit-and-activate" or "activate" or "rollback" or "import-and-activate" => (typeof(GatewayRevisionResponse), 202),
            "revisions" => (typeof(GatewayAdminPage<GatewayRevisionProjection>), 200),
            "revision" => (typeof(GatewayRevisionProjection), 200),
            "validation" => (typeof(GatewayValidationProjection), 200),
            "activations" => (typeof(GatewayActivationHistoryResponse), 200),
            "compare" => (typeof(GatewayRevisionComparison), 200),
            "export" => (typeof(GatewayExportResponse), 200),
            "operation" => (typeof(GatewayOperationProjection), 200),
            "audit" => (typeof(GatewayAdminPage<GatewayAuditProjection>), 200),
            "backup" or "purge" => (typeof(GatewayAdministrativeResponse), 202),
            _ => throw new InvalidOperationException("The Gateway Admin OpenAPI ledger is incomplete."),
        };
        builder.Metadata.Add(new ProducesMetadata(success.Type, success.Status, "application/json"));

        foreach (int status in ErrorStatuses(descriptor.Operation))
            builder.Metadata.Add(new ProducesMetadata(typeof(GatewayAdminError), status, "application/json"));
    }

    internal static IEnumerable<int> ErrorStatuses(string operation)
    {
        yield return 401;
        yield return 403;
        yield return 429;
        yield return 500;
        yield return 504;
        if (operation is not ("capabilities" or "validate")) yield return 404;
        if (operation is "validate" or "submit" or "submit-and-activate" or "activate" or "rollback" or
            "compare" or "import" or "import-and-activate" or "backup" or "purge")
        {
            yield return 400;
            yield return 413;
            yield return 415;
        }
        if (operation is "provision" or "submit" or "submit-and-activate" or "activate" or "rollback" or
            "import" or "import-and-activate")
        {
            yield return 409;
            yield return 422;
            yield return 503;
        }
        if (operation == "export") yield return 410;
        if (operation is "backup" or "purge") yield return 503;
    }

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
