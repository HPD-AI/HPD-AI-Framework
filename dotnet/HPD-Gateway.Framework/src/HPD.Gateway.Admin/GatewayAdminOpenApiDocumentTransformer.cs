using HPD.Gateway.Abstractions;
using HPD.Gateway.Effective;
using HPD.Gateway.Management;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace HPD.Gateway.Admin;

internal sealed class GatewayAdminOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info.Title = "HPD.Gateway Admin API";
        document.Info.Version = "1.0.0";
        document.Paths ??= new OpenApiPaths();
        foreach (GatewayAdminEndpointDescriptor descriptor in GatewayAdminEndpointLedger.V1)
        {
            string path = "/management/gateway/v1" + descriptor.Pattern;
            if (!document.Paths.TryGetValue(path, out IOpenApiPathItem? existing))
            {
                existing = new OpenApiPathItem();
                document.Paths[path] = existing;
            }
            var pathItem = (OpenApiPathItem)existing;
            pathItem.Operations ??= new Dictionary<HttpMethod, OpenApiOperation>();
            var operation = new OpenApiOperation
            {
                OperationId = "HpdGatewayAdmin." + descriptor.Operation,
                Responses = new OpenApiResponses(),
            };
            Type? requestType = RequestType(descriptor.Operation);
            if (requestType is not null)
            {
                IOpenApiSchema requestSchema = await context.GetOrCreateSchemaAsync(
                    requestType, null, cancellationToken).ConfigureAwait(false);
                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = descriptor.Operation is not ("activate" or "rollback"),
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new() { Schema = requestSchema },
                        ["application/hpd.gateway+json"] = new() { Schema = requestSchema },
                    },
                };
            }
            (Type responseType, int status) = ResponseType(descriptor.Operation);
            IOpenApiSchema responseSchema = await context.GetOrCreateSchemaAsync(
                responseType, null, cancellationToken).ConfigureAwait(false);
            operation.Responses[status.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                Response("Gateway Admin success response.", responseSchema);
            IOpenApiSchema errorSchema = await context.GetOrCreateSchemaAsync(
                typeof(GatewayAdminError), null, cancellationToken).ConfigureAwait(false);
            foreach (int errorStatus in GatewayAdminOpenApiMetadata.ErrorStatuses(descriptor.Operation))
                operation.Responses[errorStatus.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                    Response("Gateway Admin bounded error response.", errorSchema);
            pathItem.Operations[descriptor.Method == "GET" ? HttpMethod.Get : HttpMethod.Post] = operation;
        }
    }

    private static OpenApiResponse Response(string description, IOpenApiSchema schema) => new()
    {
        Description = description,
        Content = new Dictionary<string, OpenApiMediaType>
        {
            ["application/json"] = new() { Schema = schema },
        },
    };

    private static Type? RequestType(string operation) => operation switch
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

    private static (Type Type, int Status) ResponseType(string operation) => operation switch
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
}
