using HPD.Gateway.Abstractions;
using HPD.Gateway.Effective;
using HPD.Gateway.Management;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace HPD.Gateway.Admin;

internal sealed class GatewayAdminOpenApiSchemaTransformer(GatewayAdminOpenApiContract contract) : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? property = context.JsonPropertyInfo?.Name;
        Type declaringType = context.JsonPropertyInfo?.DeclaringType ?? context.JsonTypeInfo.Type;
        if (property is null) return Task.CompletedTask;
        ImmutableArray<GatewayAdminClientSchemaConstraint> constraints =
            GatewayAdminClientSchemaConstraintLedger.For(declaringType, property);
        foreach (GatewayAdminClientSchemaConstraint constraint in constraints)
        {
            contract.RecordSchemaTarget(constraint);
            GatewayAdminClientConstraintRules rules = constraint.Rules;
            if (constraint.AppliesTo == GatewayAdminClientSchemaConstraintTarget.Collection)
            {
                schema.MinItems = rules.CollectionMinimum;
                schema.MaxItems = rules.CollectionMaximum;
                schema.Description = $"{rules.CollectionMinimum}-{rules.CollectionMaximum} items; " +
                    $"uniqueness {rules.Uniqueness}, ordering {rules.Ordering}.";
                continue;
            }
            if (constraint.Brand == GatewayAdminClientStringBrand.None &&
                rules == new GatewayAdminClientConstraintRules())
                continue;
            OpenApiSchema target = constraint.AppliesTo == GatewayAdminClientSchemaConstraintTarget.Items
                ? schema.Items as OpenApiSchema ?? throw new InvalidOperationException("Schema item constraint target is missing.")
                : schema;
            ApplyStringRules(target, rules);
            target.Description = $"Semantic UTF-8 bytes {rules.MinimumUtf8Bytes?.ToString() ?? "0"}-" +
                $"{rules.MaximumUtf8Bytes}; normalization {rules.Normalization}; character set {rules.CharacterSet}.";
        }
        return Task.CompletedTask;
    }

    private static void ApplyStringRules(OpenApiSchema schema, GatewayAdminClientConstraintRules rules)
    {
        schema.Type = JsonSchemaType.String;
        schema.MaxLength = rules.MaximumUtf8Bytes;
        schema.MinLength = rules.CharacterSet is GatewayAdminClientCharacterSet.VisibleAscii or
            GatewayAdminClientCharacterSet.LowercaseAsciiName or GatewayAdminClientCharacterSet.AsciiArtifactLabel or
            GatewayAdminClientCharacterSet.StrongEntityTag
                ? rules.MinimumUtf8Bytes
                : rules.MinimumUtf8Bytes > 0 ? 1 : 0;
        schema.Pattern = rules.CharacterSet switch
        {
            GatewayAdminClientCharacterSet.VisibleAscii => $"^[!-~]{{{rules.MinimumUtf8Bytes ?? 0},{rules.MaximumUtf8Bytes}}}$",
            GatewayAdminClientCharacterSet.LowercaseAsciiName => $"^[a-z0-9.-]{{{rules.MinimumUtf8Bytes ?? 0},{rules.MaximumUtf8Bytes}}}$",
            GatewayAdminClientCharacterSet.AsciiArtifactLabel =>
                $"^[A-Za-z0-9][A-Za-z0-9._-]{{{Math.Max(0, (rules.MinimumUtf8Bytes ?? 1) - 1)},{rules.MaximumUtf8Bytes - 1}}}$",
            GatewayAdminClientCharacterSet.StrongEntityTag =>
                $"^\"(?=[!-~]{{{rules.MinimumUtf8Bytes},{rules.MaximumUtf8Bytes}}}\"$)[^\",]+\"$",
            _ when rules.RejectUnicodeControls =>
                $"^[^\\u0000-\\u001F\\u007F-\\u009F]{{{(rules.MinimumUtf8Bytes > 0 ? 1 : 0)},{rules.MaximumUtf8Bytes}}}$",
            _ => null,
        };
    }
}

internal sealed class GatewayAdminOpenApiContract
{
    private readonly object _sync = new();
    private string? _securityScheme;
    private readonly AsyncLocal<SchemaCorrelationScope?> _schemaCorrelation = new();

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

    internal void RecordSchemaTarget(GatewayAdminClientSchemaConstraint constraint)
    {
        _schemaCorrelation.Value?.Observed.Add(GatewayAdminClientSchemaConstraintLedger.TargetKey(constraint));
    }

    internal SchemaCorrelationScope BeginSchemaCorrelation()
    {
        var scope = new SchemaCorrelationScope(this, _schemaCorrelation.Value);
        _schemaCorrelation.Value = scope;
        return scope;
    }

    internal sealed class SchemaCorrelationScope(
        GatewayAdminOpenApiContract owner,
        SchemaCorrelationScope? prior) : IDisposable
    {
        internal HashSet<string> Observed { get; } = new(StringComparer.Ordinal);

        internal void RequireComplete()
        {
            string[] missing = GatewayAdminClientSchemaConstraintLedger.V1
                .Select(GatewayAdminClientSchemaConstraintLedger.TargetKey)
                .Where(target => !Observed.Contains(target))
                .Order(StringComparer.Ordinal).ToArray();
            if (missing.Length != 0)
                throw new InvalidOperationException("Gateway Admin OpenAPI omitted managed schema targets: " +
                    string.Join(", ", missing));
        }

        public void Dispose()
        {
            if (ReferenceEquals(owner._schemaCorrelation.Value, this))
                owner._schemaCorrelation.Value = prior;
        }
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
        using GatewayAdminOpenApiContract.SchemaCorrelationScope correlation = contract.BeginSchemaCorrelation();
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
                Parameters = Parameters(semantics),
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
        correlation.RequireComplete();
    }

    private static List<IOpenApiParameter> Parameters(GatewayAdminClientOperationSemantics semantics)
    {
        var parameters = new List<IOpenApiParameter>();
        foreach (GatewayAdminClientParameterConstraint constraint in semantics.ParameterConstraints)
        {
            if (constraint.Location == GatewayAdminClientParameterLocation.Query && constraint.Name == "maximum")
            {
                parameters.Add(Parameter(constraint.Name, ParameterLocation.Query, constraint.Required,
                    PaginationMaximumSchema(semantics.Pagination), PaginationDescription(semantics.Pagination)));
                continue;
            }
            parameters.Add(Parameter(
                constraint.Name,
                Convert(constraint.Location),
                constraint.Required,
                ParameterStringSchema(constraint),
                ParameterDescription(constraint)));
        }
        return parameters;
    }

    private static ParameterLocation Convert(GatewayAdminClientParameterLocation location) => location switch
    {
        GatewayAdminClientParameterLocation.Path => ParameterLocation.Path,
        GatewayAdminClientParameterLocation.Query => ParameterLocation.Query,
        GatewayAdminClientParameterLocation.Header => ParameterLocation.Header,
        _ => throw new InvalidOperationException("Unsupported Gateway client parameter location."),
    };

    internal static OpenApiSchema ParameterStringSchema(GatewayAdminClientParameterConstraint constraint)
    {
        constraint.Validate();
        GatewayAdminClientConstraintRules rules = constraint.Rules;
        return rules.CharacterSet switch
        {
            GatewayAdminClientCharacterSet.StrongEntityTag =>
                StringSchema(checked(rules.MaximumUtf8Bytes!.Value + 2), checked(rules.MinimumUtf8Bytes!.Value + 2),
                    $"^\"(?=[!-~]{{{rules.MinimumUtf8Bytes.Value},{rules.MaximumUtf8Bytes.Value}}}\"$)[^\",]+\"$"),
            GatewayAdminClientCharacterSet.VisibleAscii =>
                StringSchema(rules.MaximumUtf8Bytes!.Value, rules.MinimumUtf8Bytes,
                    $"^[!-~]{{{rules.MinimumUtf8Bytes ?? 0},{rules.MaximumUtf8Bytes.Value}}}$"),
            _ when rules.RejectUnicodeControls =>
                StringSchema(rules.MaximumUtf8Bytes!.Value, rules.MinimumUtf8Bytes > 0 ? 1 : 0,
                    $"^[^\\u0000-\\u001F\\u007F-\\u009F]{{{(rules.MinimumUtf8Bytes > 0 ? 1 : 0)},{rules.MaximumUtf8Bytes.Value}}}$"),
            _ => StringSchema(rules.MaximumUtf8Bytes!.Value, rules.MinimumUtf8Bytes),
        };
    }

    internal static string ParameterDescription(GatewayAdminClientParameterConstraint constraint)
    {
        constraint.Validate();
        return constraint.Rules.CharacterSet switch
        {
            GatewayAdminClientCharacterSet.StrongEntityTag =>
                $"One strong quoted entity-tag containing {constraint.Rules.MinimumUtf8Bytes}-" +
                $"{constraint.Rules.MaximumUtf8Bytes} visible-ASCII bytes except quote and comma; " +
                "weak, wildcard, unquoted, duplicate, and comma-joined validators are rejected. Absence asserts create-only.",
            GatewayAdminClientCharacterSet.VisibleAscii =>
                $"Visible-ASCII {constraint.Brand.ToString().ToLowerInvariant()} value, " +
                $"{constraint.Rules.MinimumUtf8Bytes}-{constraint.Rules.MaximumUtf8Bytes} bytes when supplied.",
            _ when constraint.Rules.RejectUnicodeControls =>
                $"Gateway resource identifier: NFC-normalized, no Unicode control characters, and " +
                $"{constraint.Rules.MinimumUtf8Bytes}-{constraint.Rules.MaximumUtf8Bytes} UTF-8 bytes. " +
                $"maxLength is the representable character bound; the {constraint.Rules.MaximumUtf8Bytes}-byte bound is enforced by the server.",
            _ => $"Opaque value limited to {constraint.Rules.MaximumUtf8Bytes} UTF-8 bytes.",
        };
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

    private static OpenApiResponse Response(string description, IOpenApiSchema schema) => new()
    {
        Description = description,
        Content = new Dictionary<string, OpenApiMediaType>
        {
            ["application/json"] = new() { Schema = schema },
        },
    };

}
