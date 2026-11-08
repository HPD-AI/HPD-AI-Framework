using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.Agent.SDK.Contract;

/// <summary>
/// Loads, normalizes, and validates an HPD OpenAPI 3.1 contract.
/// </summary>
public static class OpenApiContractReader
{
    private static readonly string[] HttpMethods =
        ["get", "post", "put", "patch", "delete", "head", "options"];

    /// <summary>
    /// Reads an OpenAPI 3.1 document and produces the generator-facing contract.
    /// </summary>
    public static async Task<OpenApiContract> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var originalSha256 =
            "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var normalizedRoot = JsonNode.Parse(bytes)
            ?? throw new InvalidDataException("The OpenAPI contract was empty.");
        InlineSchemaPromoter.Promote(normalizedRoot);
        var normalizedBytes = JsonSerializer.SerializeToUtf8Bytes(normalizedRoot);
        using var document = JsonDocument.Parse(normalizedBytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The OpenAPI contract must be a JSON object.");

        var openApiVersion = RequiredString(root, "openapi");
        if (!openApiVersion.StartsWith("3.1", StringComparison.Ordinal))
            throw new InvalidDataException($"OpenAPI 3.1 is required, found '{openApiVersion}'.");
        var info = RequiredObject(root, "info");
        var title = RequiredString(info, "title");
        var apiVersion = RequiredString(info, "version");
        var paths = RequiredObject(root, "paths");
        var components = root.TryGetProperty("components", out var componentsElement) &&
                         componentsElement.ValueKind == JsonValueKind.Object
            ? componentsElement
            : throw new InvalidDataException("The OpenAPI contract has no component schemas.");
        var schemaObject = RequiredObject(components, "schemas");
        var schemas = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in schemaObject.EnumerateObject())
            schemas.Add(property.Name, property.Value.Clone());

        var operations = new List<OperationIr>();
        foreach (var pathProperty in paths.EnumerateObject())
        {
            if (pathProperty.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Path '{pathProperty.Name}' must be an object.");
            foreach (var methodProperty in pathProperty.Value.EnumerateObject())
            {
                if (!HttpMethods.Contains(methodProperty.Name, StringComparer.Ordinal))
                    continue;
                operations.Add(ReadOperation(
                    pathProperty.Name,
                    methodProperty.Name,
                    methodProperty.Value,
                    pathProperty.Value));
            }
        }

        ValidateUniqueOperations(operations);
        ValidateReferences(operations.SelectMany(OperationReferences).Distinct(StringComparer.Ordinal), schemas);

        var sseOperation = operations.SingleOrDefault(static operation =>
            string.Equals(operation.OperationId, "ObserveLiveEventsWithSse", StringComparison.Ordinal))
            ?? throw new InvalidDataException("Missing ObserveLiveEventsWithSse operation.");
        var sseSchema = ResolveSchemaReference(
            sseOperation.SuccessSchema!.Value,
            schemas);
        if (sseSchema.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"SSE success schema resolved to {sseSchema.ValueKind} instead of an object.");
        }
        var deliverySchema = ResolveSchemaReference(
            Navigate(sseSchema, "properties", "data", "oneOf", 0),
            schemas);
        var events = ReadUnionVariants(
            Navigate(deliverySchema, "properties", "event"),
            schemas,
            "event");

        var submitOperation = operations.SingleOrDefault(static operation =>
            string.Equals(operation.OperationId, "SubmitAgentInput", StringComparison.Ordinal))
            ?? throw new InvalidDataException("Missing SubmitAgentInput operation.");
        var publicInputs = ReadUnionVariants(
            submitOperation.RequestBody?.Schema
                ?? throw new InvalidDataException("SubmitAgentInput has no request schema."),
            schemas,
            "input");
        var respondOperation = operations.SingleOrDefault(static operation =>
            string.Equals(operation.OperationId, "RespondToAgentRequest", StringComparison.Ordinal));
        var responseInputs = respondOperation is null
            ? []
            : ReadUnionVariants(
                respondOperation.RequestBody?.Schema
                    ?? throw new InvalidDataException("RespondToAgentRequest has no request schema."),
                schemas,
                "response input");
        ValidateUnionVariants(events);
        ValidateUnionVariants(publicInputs);
        if (responseInputs.Count > 0)
            ValidateUnionVariants(responseInputs);

        var eventCount = sseOperation.EventCount;
        var eventDigest = sseOperation.EventDigest;
        if (eventCount is not null && eventCount != events.Count)
        {
            throw new InvalidDataException(
                $"SSE extension declares {eventCount} events but exposes {events.Count} branches.");
        }

        return new OpenApiContract(
            openApiVersion,
            title,
            apiVersion,
            originalSha256,
            eventDigest,
            eventCount,
            schemas,
            operations.OrderBy(static operation => operation.OperationId, StringComparer.Ordinal).ToArray(),
            publicInputs,
            responseInputs,
            events);
    }

    private static OperationIr ReadOperation(
        string path,
        string method,
        JsonElement operation,
        JsonElement pathItem)
    {
        if (operation.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Operation '{method} {path}' must be an object.");
        var operationId = RequiredString(operation, "operationId");
        var tag = operation.TryGetProperty("tags", out var tags) &&
                  tags.ValueKind == JsonValueKind.Array &&
                  tags.GetArrayLength() > 0 &&
                  tags[0].GetString() is { Length: > 0 } firstTag
            ? firstTag
            : "Agent Runtime";
        var parameters = new Dictionary<(string Name, string Location), ParameterIr>();
        if (pathItem.TryGetProperty("parameters", out var pathParameters))
            AddParameters(parameters, pathParameters);
        if (operation.TryGetProperty("parameters", out var operationParameters))
            AddParameters(parameters, operationParameters);

        RequestBodyIr? requestBody = null;
        if (operation.TryGetProperty("requestBody", out var requestBodyElement) &&
            requestBodyElement.ValueKind == JsonValueKind.Object)
        {
            var required = requestBodyElement.TryGetProperty("required", out var requiredElement) &&
                           requiredElement.ValueKind == JsonValueKind.True;
            var content = RequiredObject(requestBodyElement, "content");
            var mediaType = SelectRequestMediaType(content);
            requestBody = new RequestBodyIr(
                required,
                mediaType,
                RequiredObject(content, mediaType).GetProperty("schema").Clone());
        }

        var responses = RequiredObject(operation, "responses");
        var success = responses.EnumerateObject()
            .Where(static response => response.Name.Length == 3 &&
                                      (response.Name[0] == '2' || response.Name == "101") &&
                                      response.Name.All(char.IsAsciiDigit))
            .OrderBy(static response => response.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (success.Value.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException($"Operation '{operationId}' has no 2xx response.");

        string? successMediaType = null;
        JsonElement? successSchema = null;
        if (success.Value.TryGetProperty("content", out var responseContent) &&
            responseContent.ValueKind == JsonValueKind.Object)
        {
            successMediaType = SelectResponseMediaType(responseContent);
            var media = RequiredObject(responseContent, successMediaType);
            if (media.TryGetProperty("schema", out var schema))
                successSchema = schema.Clone();
        }

        var summary = operation.TryGetProperty("summary", out var summaryElement)
            ? summaryElement.GetString() ?? string.Empty
            : operation.TryGetProperty("description", out var descriptionElement)
                ? FirstLine(descriptionElement.GetString())
                : string.Empty;
        var isWebSocketUpgrade =
            string.Equals(success.Name, "101", StringComparison.Ordinal) ||
            operation.TryGetProperty("x-hpd-websocket-protocol", out _);
        return new OperationIr(
            operationId,
            method.ToUpperInvariant(),
            path,
            tag,
            summary,
            parameters.Values
                .OrderBy(static parameter => parameter.Location, StringComparer.Ordinal)
                .ThenBy(static parameter => parameter.Name, StringComparer.Ordinal)
                .ToArray(),
            requestBody,
            success.Name,
            successMediaType,
            successSchema,
            responses.TryGetProperty("404", out _),
            string.Equals(successMediaType, "text/event-stream", StringComparison.Ordinal),
            isWebSocketUpgrade,
            TryIntegerExtension(operation, "x-hpd-event-count"),
            TryStringExtension(operation, "x-hpd-event-digest"),
            operation.TryGetProperty("x-hpd-replay-safe-post", out var replaySafe) &&
            replaySafe.ValueKind == JsonValueKind.True);
    }

    private static void AddParameters(
        Dictionary<(string Name, string Location), ParameterIr> destination,
        JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Array)
            return;
        foreach (var parameter in parameters.EnumerateArray())
        {
            var name = RequiredString(parameter, "name");
            var location = RequiredString(parameter, "in");
            var required = parameter.TryGetProperty("required", out var requiredElement) &&
                           requiredElement.ValueKind == JsonValueKind.True;
            var schema = RequiredObject(parameter, "schema").Clone();
            destination[(name, location)] = new ParameterIr(name, location, required, schema);
        }
    }

    private static string SelectRequestMediaType(JsonElement content)
    {
        if (content.TryGetProperty("application/json", out _))
            return "application/json";
        if (content.TryGetProperty("multipart/form-data", out _))
            return "multipart/form-data";
        return content.EnumerateObject().FirstOrDefault().Name
            ?? throw new InvalidDataException("Request body has no content type.");
    }

    private static string SelectResponseMediaType(JsonElement content)
    {
        foreach (var mediaType in new[]
                 {
                     "application/json",
                     "application/problem+json",
                     "text/event-stream",
                     "application/octet-stream",
                     "text/plain"
                 })
        {
            if (content.TryGetProperty(mediaType, out _))
                return mediaType;
        }
        return content.EnumerateObject().FirstOrDefault().Name
            ?? throw new InvalidDataException("Response has no content type.");
    }

    private static IReadOnlyList<UnionVariant> ReadUnionVariants(
        JsonElement union,
        IReadOnlyDictionary<string, JsonElement> schemas,
        string kind)
    {
        if (union.ValueKind != JsonValueKind.Object ||
            !union.TryGetProperty("oneOf", out var oneOf) ||
            oneOf.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"The {kind} union has no oneOf branches.");
        }

        var result = new List<UnionVariant>(oneOf.GetArrayLength());
        foreach (var branch in oneOf.EnumerateArray())
        {
            var componentId = ReferenceId(RequiredString(branch, "$ref"));
            if (!schemas.TryGetValue(componentId, out var schema))
                throw new InvalidDataException($"Unresolved {kind} schema reference '{componentId}'.");
            var discriminator = Discriminator(schema, schemas, componentId);
            result.Add(new UnionVariant(componentId, discriminator));
        }
        return result;
    }

    private static string Discriminator(
        JsonElement schema,
        IReadOnlyDictionary<string, JsonElement> schemas,
        string componentId)
    {
        if (schema.TryGetProperty("x-hpd-event-discriminator", out var eventDiscriminator) &&
            eventDiscriminator.GetString() is { Length: > 0 } value)
        {
            return value;
        }

        var typeSchema = Navigate(schema, "properties", "type");
        if (typeSchema.TryGetProperty("const", out var constant) &&
            constant.GetString() is { Length: > 0 } constantValue)
        {
            return constantValue;
        }
        if (typeSchema.TryGetProperty("enum", out var enumValues) &&
            enumValues.ValueKind == JsonValueKind.Array &&
            enumValues.GetArrayLength() > 0 &&
            enumValues[0].GetString() is { Length: > 0 } enumValue)
        {
            return enumValue;
        }

        if (schema.TryGetProperty("$ref", out var reference))
        {
            var target = ReferenceId(reference.GetString()!);
            if (schemas.TryGetValue(target, out var targetSchema))
                return Discriminator(targetSchema, schemas, target);
        }
        throw new InvalidDataException($"Schema '{componentId}' has no discriminator.");
    }

    private static IEnumerable<string> OperationReferences(OperationIr operation)
    {
        if (operation.RequestBody is { } request)
        {
            foreach (var reference in SchemaReferences(request.Schema))
                yield return reference;
        }
        if (operation.SuccessSchema is { } response)
        {
            foreach (var reference in SchemaReferences(response))
                yield return reference;
        }
        foreach (var parameter in operation.Parameters)
        foreach (var reference in SchemaReferences(parameter.Schema))
            yield return reference;
    }

    private static IEnumerable<string> SchemaReferences(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("$ref", out var reference) &&
                reference.GetString() is { } path)
            {
                yield return ReferenceId(path);
            }
            foreach (var property in schema.EnumerateObject())
            foreach (var nested in SchemaReferences(property.Value))
                yield return nested;
        }
        else if (schema.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in schema.EnumerateArray())
            foreach (var nested in SchemaReferences(item))
                yield return nested;
        }
    }

    private static void ValidateReferences(
        IEnumerable<string> references,
        IReadOnlyDictionary<string, JsonElement> schemas)
    {
        foreach (var reference in references)
        {
            if (!schemas.ContainsKey(reference))
                throw new InvalidDataException($"Unresolved component reference '{reference}'.");
        }
    }

    private static void ValidateUnionVariants(IReadOnlyList<UnionVariant> variants)
    {
        if (variants.Count == 0)
            throw new InvalidDataException("A generated union cannot be empty.");
        var componentIds = new HashSet<string>(StringComparer.Ordinal);
        var discriminators = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in variants)
        {
            if (!componentIds.Add(variant.ComponentId))
                throw new InvalidDataException($"Duplicate union component '{variant.ComponentId}'.");
            if (!discriminators.Add(variant.Discriminator))
                throw new InvalidDataException($"Duplicate union discriminator '{variant.Discriminator}'.");
        }
    }

    private static void ValidateUniqueOperations(IReadOnlyList<OperationIr> operations)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (string.IsNullOrWhiteSpace(operation.OperationId))
                throw new InvalidDataException($"Operation '{operation.Method} {operation.Path}' has no operationId.");
            if (!ids.Add(operation.OperationId))
                throw new InvalidDataException($"Duplicate operationId '{operation.OperationId}'.");
        }
    }

    private static string ReferenceId(string reference)
    {
        const string prefix = "#/components/schemas/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported schema reference '{reference}'.");
        return reference[prefix.Length..]
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
    }

    private static int? TryIntegerExtension(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;

    private static string? TryStringExtension(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static JsonElement Navigate(JsonElement element, params object[] segments)
    {
        var current = element;
        foreach (var segment in segments)
        {
            if (segment is int index)
            {
                if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                    throw new InvalidDataException("OpenAPI navigation index is out of range.");
                current = current[index];
            }
            else
            {
                var name = (string)segment;
                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(name, out var next))
                {
                    throw new InvalidDataException($"OpenAPI navigation property '{name}' is missing.");
                }
                current = next;
            }
        }
        return current;
    }

    private static JsonElement ResolveSchemaReference(
        JsonElement schema,
        IReadOnlyDictionary<string, JsonElement> schemas)
    {
        var current = schema;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current.ValueKind == JsonValueKind.Object &&
               current.TryGetProperty("$ref", out var reference) &&
               reference.GetString() is { } referenceValue)
        {
            var componentId = ReferenceId(referenceValue);
            if (!visited.Add(componentId) ||
                !schemas.TryGetValue(componentId, out current))
            {
                throw new InvalidDataException(
                    $"Unresolved or recursive schema reference '{componentId}'.");
            }
        }
        return current;
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Required object property '{name}' is missing.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            value.GetString() is not { Length: > 0 } result)
        {
            throw new InvalidDataException($"Required string property '{name}' is missing.");
        }
        return result;
    }

    private static string FirstLine(string? value) =>
        value?.Split('\n', 2)[0].Trim() ?? string.Empty;
}
