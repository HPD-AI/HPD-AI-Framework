using System.Text.Json.Nodes;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace HPD.Base.AspNetCore;

internal sealed class HPDBaseOpenApiOperationTransformer(IOptions<HPDBaseOpenApiOptions> options) : IOpenApiOperationTransformer
{
    private static readonly string[] s_responseHeaders =
    [
        "ETag",
        BaseHttpHeaders.Revision,
        "Last-Modified",
        "Location",
        BaseHttpHeaders.EventIds,
        BaseHttpHeaders.CorrelationId,
        BaseHttpHeaders.PreferenceApplied,
        BaseHttpHeaders.RetryAfter,
        BaseHttpHeaders.RequestDisposition
    ];

    private static readonly string[] s_adminPolicyExplainResponseHeaders =
    [
        "Cache-Control",
        BaseHttpHeaders.CorrelationId
    ];

    private const string FileObjectsUploadOperationId = "base.files.objects.upload";
    private const string FileObjectsListOperationId = "base.files.objects.list";

    /// <summary>Executes the transform async operation.</summary>
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata.OfType<HPDBaseOpenApiRouteMetadata>().FirstOrDefault();
        if (metadata is null)
            return TransformModuleRouteAsync(operation, context);

        operation.OperationId = metadata.OperationId;
        operation.Summary ??= metadata.Summary;
        operation.Description ??= metadata.Description;

        AddPathParameters(operation, context.Description.RelativePath);
        AddQueryParameters(operation, metadata.OperationId);
        AddRequestHeader(operation, BaseHttpHeaders.CorrelationId, required: false, "Safe caller-provided correlation id echoed in responses.");

        if (metadata.OperationId is BaseRouteIds.RecordsPatch
            or BaseRouteIds.RecordsReplace
            or BaseRouteIds.RecordsDelete
            or BaseRouteIds.RecordsUpsert)
            AddRequestHeader(operation, BaseHttpHeaders.IfMatch, required: false, "Expected record revision for optimistic concurrency.");
        if (metadata.OperationId == BaseRouteIds.RecordsBatch)
            AddRequestHeader(operation, BaseHttpHeaders.IdempotencyKey, required: false, "Identifies an exact atomic batch request for durable duplicate resolution.");

        var responseHeaders = metadata.OperationId == BaseHttpRouteNames.AdminPolicyExplain
            ? s_adminPolicyExplainResponseHeaders
            : s_responseHeaders;
        foreach (var header in responseHeaders)
            AddResponseHeader(operation, header);

        if (options.Value.AddBearerSecurityScheme && metadata.IsAdmin && HasAuthorizationMetadata(context))
            AddSecurityRequirement(operation, options.Value.BearerSecuritySchemeName, context.Document);

        if (options.Value.AddHPDExtensions)
            AddHPDExtensions(operation, metadata);

        return Task.CompletedTask;
    }

    private Task TransformModuleRouteAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata.OfType<IHPDBaseModuleOpenApiMetadata>().FirstOrDefault();
        if (metadata is null)
            return Task.CompletedTask;

        operation.OperationId = metadata.OperationId;
        operation.Summary ??= metadata.Summary;
        operation.Description ??= metadata.Description;

        AddPathParameters(operation, context.Description.RelativePath);
        AddModuleQueryParameters(operation, metadata.OperationId);
        AddRequestHeader(operation, BaseHttpHeaders.CorrelationId, required: false, "Safe caller-provided correlation id echoed in responses.");
        AddModuleRequestHeaders(operation, metadata.OperationId);
        foreach (var header in s_responseHeaders)
            AddResponseHeader(operation, header);

        if (options.Value.AddHPDExtensions)
            AddHPDExtensions(operation, metadata);

        return Task.CompletedTask;
    }

    private static bool HasAuthorizationMetadata(OpenApiOperationTransformerContext context) =>
        context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any();

    private static void AddPathParameters(OpenApiOperation operation, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return;

        foreach (var name in ExtractRouteParameterNames(relativePath))
            AddParameter(operation, name, ParameterLocation.Path, required: true, PathParameterDescription(name));
    }

    private static IEnumerable<string> ExtractRouteParameterNames(string relativePath)
    {
        var start = 0;
        while ((start = relativePath.IndexOf('{', start)) >= 0)
        {
            var end = relativePath.IndexOf('}', start + 1);
            if (end < 0)
                yield break;

            var name = relativePath[(start + 1)..end].Trim('?', '*');
            if (!string.IsNullOrWhiteSpace(name))
                yield return name;
            start = end + 1;
        }
    }

    private static string PathParameterDescription(string name) =>
        name switch
        {
            "collectionId" => "BASE collection identifier.",
            "id" => "BASE record identifier.",
            _ => "Route parameter."
        };

    private static void AddQueryParameters(OpenApiOperation operation, string operationId)
    {
        switch (operationId)
        {
            case BaseRouteIds.Manifest:
            case BaseHttpRouteNames.AdminManifest:
                AddParameter(operation, "expand", ParameterLocation.Query, required: false, "Comma-separated manifest expansion tokens: schema, capabilities, health, diagnostics, collections.");
                break;
            case BaseRouteIds.RecordsList:
                AddParameter(operation, "filter", ParameterLocation.Query, required: false, "JSON FilterExpression. Cannot be combined with where[...] parameters.");
                AddParameter(operation, "where[field]", ParameterLocation.Query, required: false, "Field filter shortcut. Operators may be supplied as where[field][eq], where[field][lt], where[field][in], and related modifiers.");
                AddParameter(operation, "sort", ParameterLocation.Query, required: false, "Comma-separated sort fields. Prefix a field with '-' for descending order.");
                AddParameter(operation, "nulls[field]", ParameterLocation.Query, required: false, "Null ordering for a sorted field: first or last.");
                AddParameter(operation, "page", ParameterLocation.Query, required: false, "Page number for page-based pagination.");
                AddParameter(operation, "perPage", ParameterLocation.Query, required: false, "Page size for page-based pagination.");
                AddParameter(operation, "offset", ParameterLocation.Query, required: false, "Offset for offset-based pagination.");
                AddParameter(operation, "limit", ParameterLocation.Query, required: false, "Maximum number of records to return.");
                AddParameter(operation, "cursor", ParameterLocation.Query, required: false, "Cursor token for cursor-based pagination.");
                AddParameter(operation, "cursorDir", ParameterLocation.Query, required: false, "Cursor direction: after or before.");
                AddParameter(operation, "select", ParameterLocation.Query, required: false, "Comma-separated field projection.");
                AddParameter(operation, "include", ParameterLocation.Query, required: false, "Comma-separated include paths.");
                AddParameter(operation, "count", ParameterLocation.Query, required: false, "Count mode: none, ifAvailable, exact, estimated, or limited.");
                AddParameter(operation, "ext[module.name]", ParameterLocation.Query, required: false, "Extension query arguments keyed by module and name.");
                break;
        }
    }

    private static void AddModuleQueryParameters(OpenApiOperation operation, string operationId)
    {
        if (operationId != FileObjectsListOperationId)
            return;

        AddParameter(operation, "prefix", ParameterLocation.Query, required: false, "Object key prefix used to filter listed file objects.");
        AddParameter(operation, "limit", ParameterLocation.Query, required: false, "Maximum number of file objects to return.");
        AddParameter(operation, "cursor", ParameterLocation.Query, required: false, "Cursor token for file object pagination when supported.");
    }

    private static void AddModuleRequestHeaders(OpenApiOperation operation, string operationId)
    {
        if (operationId != FileObjectsUploadOperationId)
            return;

        AddRequestHeader(operation, "X-HPD-File-Key", required: true, "Logical object key for the uploaded file object.");
        AddRequestHeader(operation, "X-HPD-File-Name", required: false, "Original or display file name for the uploaded file object.");
        AddRequestHeader(operation, "X-HPD-File-Checksum", required: false, "Optional checksum for upload validation, for example sha256:<hex>.");
    }

    private static void AddRequestHeader(OpenApiOperation operation, string name, bool required, string description)
        => AddParameter(operation, name, ParameterLocation.Header, required, description);

    private static void AddParameter(OpenApiOperation operation, string name, ParameterLocation location, bool required, string description)
    {
        operation.Parameters ??= [];
        if (operation.Parameters.Any(parameter =>
                string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)
                && parameter.In == location))
            return;

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = location,
            Required = required,
            Description = description,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });
    }

    private static void AddResponseHeader(OpenApiOperation operation, string name)
    {
        if (operation.Responses is null)
            return;

        foreach (var response in operation.Responses.Values)
        {
            if (response is not OpenApiResponse concreteResponse)
                continue;

            concreteResponse.Headers ??= new Dictionary<string, IOpenApiHeader>();
            concreteResponse.Headers.TryAdd(name, new OpenApiHeader
            {
                Description = HeaderDescription(name),
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        }
    }

    private static string HeaderDescription(string name) =>
        name switch
        {
            "ETag" => "HTTP entity tag for the returned resource or descriptor.",
            BaseHttpHeaders.Revision => "Compact HPD.BASE revision token.",
            "Last-Modified" => "Last modification timestamp.",
            "Location" => "Location of a created record.",
            BaseHttpHeaders.EventIds => "Compact mutation event ids.",
            BaseHttpHeaders.CorrelationId => "Correlation id associated with the request.",
            BaseHttpHeaders.PreferenceApplied => "Preference tokens applied by the HTTP projection.",
            BaseHttpHeaders.RetryAfter => "Retry hint in seconds.",
            "Cache-Control" => "Cache directive applied to the response.",
            _ => "HPD.BASE response header."
        };

    private static void AddSecurityRequirement(OpenApiOperation operation, string schemeName, OpenApiDocument? document)
    {
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(schemeName, document)] = []
        });
    }

    private static void AddHPDExtensions(OpenApiOperation operation, HPDBaseOpenApiRouteMetadata metadata)
    {
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions["x-hpd-operation-id"] = new JsonNodeExtension(metadata.OperationId);
        operation.Extensions["x-hpd-route-visibility"] = new JsonNodeExtension(metadata.RouteVisibility);
        operation.Extensions["x-hpd-auth-requirement"] = new JsonNodeExtension(metadata.AuthRequirement);
        if (metadata.RequestDtoId is not null)
            operation.Extensions["x-hpd-request-dto-id"] = new JsonNodeExtension(metadata.RequestDtoId);
        operation.Extensions["x-hpd-response-dto-id"] = new JsonNodeExtension(metadata.ResponseDtoId);
        operation.Extensions["x-hpd-error-dto-id"] = new JsonNodeExtension(metadata.ErrorDtoId);
        operation.Extensions["x-hpd-required-feature-ids"] = new JsonNodeExtension(new JsonArray(metadata.RequiredFeatureIds.Select(static featureId => JsonValue.Create(featureId)).ToArray()));
    }

    private static void AddHPDExtensions(OpenApiOperation operation, IHPDBaseModuleOpenApiMetadata metadata)
    {
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions["x-hpd-operation-id"] = new JsonNodeExtension(metadata.OperationId);
        operation.Extensions["x-hpd-route-visibility"] = new JsonNodeExtension("Public");
        operation.Extensions["x-hpd-auth-requirement"] = new JsonNodeExtension("Policy");
        operation.Extensions["x-hpd-required-feature-ids"] = new JsonNodeExtension(new JsonArray(metadata.RequiredFeatureIds.Select(static featureId => JsonValue.Create(featureId)).ToArray()));
    }
}
