using HPD.Gateway.Abstractions;
using HPD.Gateway.Effective;
using HPD.Gateway.Management;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace HPD.Gateway.Admin;

internal sealed class GatewayAdminOpenApiContract
{
    private readonly object _sync = new();
    private string? _securityScheme;

    internal void Seal(string securityScheme)
    {
        lock (_sync)
        {
            if (_securityScheme is not null && !StringComparer.Ordinal.Equals(_securityScheme, securityScheme))
                throw new InvalidOperationException("The Gateway Admin OpenAPI security scheme is already sealed.");
            _securityScheme = securityScheme;
        }
    }

    internal string GetSecurityScheme()
    {
        lock (_sync)
            return _securityScheme ?? throw new InvalidOperationException("The Gateway Admin OpenAPI contract is not sealed.");
    }
}

internal sealed class GatewayAdminOpenApiDocumentTransformer(GatewayAdminOpenApiContract contract)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info.Title = "HPD.Gateway Admin API";
        document.Info.Version = "1.0.0";
        string securityScheme = contract.GetSecurityScheme();
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes.TryAdd(securityScheme, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
        });
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
                Parameters = Parameters(descriptor),
                Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(securityScheme, document)] = []
                    }
                ],
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

    private static List<IOpenApiParameter> Parameters(GatewayAdminEndpointDescriptor descriptor)
    {
        var parameters = new List<IOpenApiParameter>();
        foreach (string name in PathParameterNames(descriptor.Pattern))
            parameters.Add(Parameter(name, ParameterLocation.Path, required: true, StringSchema(128),
                "Gateway resource identifier."));
        parameters.Add(Parameter("X-Correlation-ID", ParameterLocation.Header, required: false,
            StringSchema(128), "Bounded request correlation identifier."));
        if (descriptor.Mutation)
            parameters.Add(Parameter("Idempotency-Key", ParameterLocation.Header, required: true,
                StringSchema(128), "Visible-ASCII product idempotency identity."));
        if (descriptor.Operation is "submit-and-activate" or "activate" or "rollback" or "import-and-activate")
            parameters.Add(Parameter("If-Match", ParameterLocation.Header, required: false,
                StringSchema(514), "Exact desired-state generation validator; absence asserts create-only."));
        if (descriptor.Operation is "revisions" or "activations" or "audit")
        {
            parameters.Add(Parameter("maximum", ParameterLocation.Query, required: false,
                new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1", Maximum = "256" },
                "Maximum page size; defaults to 64."));
            parameters.Add(Parameter("cursor", ParameterLocation.Query, required: false,
                StringSchema(4096), "Opaque stable continuation token."));
        }
        return parameters;
    }

    private static OpenApiParameter Parameter(
        string name, ParameterLocation location, bool required, IOpenApiSchema schema, string description) => new()
    {
        Name = name,
        In = location,
        Required = required,
        Schema = schema,
        Description = description,
    };

    private static OpenApiSchema StringSchema(int maximumLength) => new()
    {
        Type = JsonSchemaType.String,
        MaxLength = maximumLength,
    };

    private static IEnumerable<string> PathParameterNames(string pattern)
    {
        int offset = 0;
        while ((offset = pattern.IndexOf('{', offset)) >= 0)
        {
            int end = pattern.IndexOf('}', offset + 1);
            if (end < 0) throw new InvalidOperationException("The Gateway Admin path ledger is malformed.");
            yield return pattern[(offset + 1)..end];
            offset = end + 1;
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
