using HPD.Gateway.Abstractions;
using HPD.Gateway.Effective;
using HPD.Gateway.Management;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace HPD.Gateway.Admin;

internal sealed class GatewayAdminOpenApiSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? property = context.JsonPropertyInfo?.Name;
        Type declaringType = context.JsonPropertyInfo?.DeclaringType ?? context.JsonTypeInfo.Type;
        if (declaringType == typeof(GatewayPurgeRequest) &&
            StringComparer.OrdinalIgnoreCase.Equals(property, nameof(GatewayPurgeRequest.ResourceIds)))
        {
            schema.MinItems = 1;
            schema.MaxItems = 256;
            schema.Description = "One to 256 unique, ordinally sorted resource identifiers. Each value is NFC-normalized, " +
                "contains no Unicode control characters, and is limited to 128 UTF-8 bytes.";
            if (schema.Items is OpenApiSchema item)
            {
                item.Type = JsonSchemaType.String;
                item.MinLength = 1;
                item.MaxLength = 128;
                item.Pattern = "^[^\\u0000-\\u001F\\u007F-\\u009F]+$";
            }
        }
        else if ((declaringType == typeof(GatewayRevisionRequest) &&
                  StringComparer.OrdinalIgnoreCase.Equals(property, nameof(GatewayRevisionRequest.ConfigurationJson))) ||
                 (declaringType == typeof(GatewayImportRequest) &&
                  StringComparer.OrdinalIgnoreCase.Equals(property, nameof(GatewayImportRequest.ConfigurationJson))))
        {
            schema.Description = "Canonical candidate text limited to 4,194,304 UTF-8 bytes; maxLength is the corresponding character ceiling.";
        }
        else if ((declaringType == typeof(GatewayRevisionRequest) &&
                  (StringComparer.OrdinalIgnoreCase.Equals(property, nameof(GatewayRevisionRequest.SourceKind)) ||
                   StringComparer.OrdinalIgnoreCase.Equals(property, nameof(GatewayRevisionRequest.SourceId)))) ||
                 (declaringType == typeof(GatewayImportRequest) &&
                  StringComparer.OrdinalIgnoreCase.Equals(property, nameof(GatewayImportRequest.SourceId))) ||
                 declaringType == typeof(GatewayCompareRequest))
        {
            schema.Description = "NFC-normalized, control-free identifier limited to 128 UTF-8 bytes; maxLength is the character ceiling.";
        }
        return Task.CompletedTask;
    }
}

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
        });
        document.Paths ??= new OpenApiPaths();
        foreach (GatewayAdminEndpointDescriptor descriptor in GatewayAdminEndpointLedger.V1)
        {
            GatewayAdminClientOperationSemantics semantics = GatewayAdminClientSemanticLedger.For(descriptor.Operation);
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
                Parameters = Parameters(descriptor, semantics),
                Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(securityScheme, document)] = []
                    }
                ],
            };
            Type? requestType = semantics.RequestType;
            if (requestType is not null)
            {
                IOpenApiSchema requestSchema = await context.GetOrCreateSchemaAsync(
                    requestType, null, cancellationToken).ConfigureAwait(false);
                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = semantics.RequestBodyPresence == GatewayAdminClientRequestBodyPresence.Required,
                    Description = RequestConstraintDescription(descriptor.Operation),
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new() { Schema = requestSchema },
                        ["application/hpd.gateway+json"] = new() { Schema = requestSchema },
                    },
                };
            }
            IOpenApiSchema responseSchema = await context.GetOrCreateSchemaAsync(
                semantics.SuccessType, null, cancellationToken).ConfigureAwait(false);
            operation.Responses[semantics.SuccessStatus.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                Response("Gateway Admin success response.", responseSchema);
            IOpenApiSchema errorSchema = await context.GetOrCreateSchemaAsync(
                typeof(GatewayAdminError), null, cancellationToken).ConfigureAwait(false);
            foreach (int errorStatus in semantics.DocumentedErrors)
                operation.Responses[errorStatus.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                    Response("Gateway Admin bounded error response.", errorSchema);
            pathItem.Operations[descriptor.Method == "GET" ? HttpMethod.Get : HttpMethod.Post] = operation;
        }
    }

    private static List<IOpenApiParameter> Parameters(
        GatewayAdminEndpointDescriptor descriptor,
        GatewayAdminClientOperationSemantics semantics)
    {
        var parameters = new List<IOpenApiParameter>();
        foreach (string name in PathParameterNames(descriptor.Pattern))
            parameters.Add(Parameter(name, ParameterLocation.Path, required: true,
                StringSchema(128, 1, "^[^\\u0000-\\u001F\\u007F-\\u009F]+$"),
                "Gateway resource identifier: NFC-normalized, no Unicode control characters, and 1-128 UTF-8 bytes. " +
                "maxLength is the representable character bound; the 128-byte bound is enforced by the server."));
        parameters.Add(Parameter("X-Correlation-ID", ParameterLocation.Header, required: false,
            StringSchema(128, 1, "^[!-~]+$"), "Visible-ASCII request correlation identifier, 1-128 characters when supplied."));
        if (semantics.Idempotency == GatewayAdminClientIdempotency.Required)
            parameters.Add(Parameter("Idempotency-Key", ParameterLocation.Header, required: true,
                StringSchema(128, 1, "^[!-~]+$"), "Visible-ASCII product idempotency identity, 1-128 characters."));
        if (semantics.DesiredPrecondition == GatewayAdminClientDesiredPrecondition.CreateOrReplace)
            parameters.Add(Parameter("If-Match", ParameterLocation.Header, required: false,
                StringSchema(514, 3, "^\"(?=[!-~]{1,512}\"$)[^\",]+\"$"),
                "One strong quoted entity-tag containing 1-512 visible-ASCII characters except quote and comma; " +
                "weak, wildcard, unquoted, duplicate, and comma-joined validators are rejected. Absence asserts create-only."));
        if (semantics.Pagination.Kind == GatewayAdminClientPaginationKind.OpaqueCursor)
        {
            parameters.Add(Parameter("maximum", ParameterLocation.Query, required: false,
                PaginationMaximumSchema(semantics.Pagination), PaginationDescription(semantics.Pagination)));
            parameters.Add(Parameter("cursor", ParameterLocation.Query, required: false,
                StringSchema(4096), "Opaque stable continuation token."));
        }
        return parameters;
    }

    internal static OpenApiSchema PaginationMaximumSchema(GatewayAdminClientPaginationSpecification specification)
    {
        specification.Validate();
        if (specification.Kind != GatewayAdminClientPaginationKind.OpaqueCursor)
            throw new InvalidOperationException("Pagination schema requires opaque-cursor pagination.");
        return new OpenApiSchema
        {
            Type = JsonSchemaType.Integer,
            Minimum = specification.MinimumMaximum!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Maximum = specification.MaximumMaximum!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Default = JsonValue.Create(specification.DefaultMaximum!.Value),
        };
    }

    internal static string PaginationDescription(GatewayAdminClientPaginationSpecification specification)
    {
        specification.Validate();
        return $"Maximum page size from {specification.MinimumMaximum!.Value} to " +
            $"{specification.MaximumMaximum!.Value}; defaults to {specification.DefaultMaximum!.Value}.";
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

    private static OpenApiSchema StringSchema(
        int maximumLength,
        int? minimumLength = null,
        string? pattern = null) => new()
    {
        Type = JsonSchemaType.String,
        MaxLength = maximumLength,
        MinLength = minimumLength,
        Pattern = pattern,
    };

    private static string RequestConstraintDescription(string operation) => operation switch
    {
        "validate" => "A strict Gateway declaration document. The complete HTTP body and canonical configuration are each limited to 4,194,304 UTF-8 bytes.",
        "submit" or "submit-and-activate" => "configurationJson is limited to 4,194,304 UTF-8 bytes; sourceKind and sourceId are NFC-normalized, control-free identifiers limited to 128 UTF-8 bytes; description is limited to 1,024 characters.",
        "activate" or "rollback" => "The optional description is limited to 1,024 characters.",
        "compare" => "Both revision identifiers are NFC-normalized, control-free values limited to 128 UTF-8 bytes.",
        "import" or "import-and-activate" => "configurationJson is limited to 4,194,304 UTF-8 bytes; sourceId is NFC-normalized and control-free with a 128 UTF-8 byte limit; description is limited to 1,024 characters.",
        "backup" => "sinkName is 1-128 lowercase ASCII letters, digits, dots, or hyphens. artifactLabel, when present, is 1-128 ASCII letters, digits, dots, underscores, or hyphens and begins with a letter or digit.",
        "purge" => "resourceIds contains 1-256 unique, ordinally sorted, NFC-normalized, control-free identifiers; each identifier is limited to 128 UTF-8 bytes.",
        _ => "Gateway Admin bounded request.",
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

}
