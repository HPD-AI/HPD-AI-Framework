using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.Gateway.Admin;

public sealed record GatewayClientGenerationManifestV1(
    int SchemaVersion,
    string ApiVersion,
    string OpenApiDocumentName,
    string SecurityScheme,
    ImmutableArray<GatewayClientOperationV1> Operations,
    ImmutableArray<GatewaySchemaConstraintV1> SchemaConstraints);

public sealed record GatewayClientOperationV1(
    string Operation,
    string OpenApiOperationId,
    string Method,
    string Path,
    string Capability,
    string? ResourcePolicy,
    string ResourceKind,
    bool Mutation,
    string Idempotency,
    string DesiredPrecondition,
    bool ProtectedNotFound,
    GatewayClientSuccessV1 Success,
    ImmutableArray<int> DocumentedErrors,
    GatewayClientRequestBodyV1 RequestBody,
    GatewayClientPaginationV1 Pagination,
    ImmutableArray<GatewayParameterConstraintV1> ParameterConstraints);

public sealed record GatewayClientSuccessV1(int Status, string SchemaRef, string Meaning);
public sealed record GatewayClientRequestBodyV1(string Presence, string? SchemaRef, ImmutableArray<string> MediaTypes);
public sealed record GatewayClientPaginationV1(string Kind, int? DefaultMaximum, int? MinimumMaximum, int? MaximumMaximum);
public sealed record GatewayParameterConstraintV1(string Location, string Name, bool Required, string Brand, GatewayConstraintRulesV1 Rules);
public sealed record GatewaySchemaConstraintV1(string SchemaRef, string PropertyPointer, string AppliesTo, string Brand, GatewayConstraintRulesV1 Rules);
public sealed record GatewayConstraintRulesV1(
    int? MinimumUtf8Bytes,
    int? MaximumUtf8Bytes,
    string Normalization,
    string CharacterSet,
    bool RejectUnicodeControls,
    int? CollectionMinimum,
    int? CollectionMaximum,
    string Uniqueness,
    string Ordering,
    string Cardinality);

public sealed record GatewayClientGenerationSnapshotV1(
    int SnapshotVersion,
    string HashAlgorithm,
    string OpenApiSha256,
    string ManifestSha256,
    string SourceSha256,
    JsonObject OpenApi,
    GatewayClientGenerationManifestV1 Manifest);

internal static class GatewayClientGenerationSnapshotFactory
{
    internal static GatewayClientGenerationSnapshotV1 Create(JsonObject openApi, string securityScheme)
    {
        ArgumentNullException.ThrowIfNull(openApi);
        GatewayClientOpenApiJsonValidator.Validate(openApi, securityScheme);
        GatewayClientGenerationManifestV1 manifest = ProjectFromManagedLedger(securityScheme);
        byte[] openApiBytes = GatewayCanonicalJson.Serialize(openApi);
        JsonNode manifestNode = JsonSerializer.SerializeToNode(manifest, GatewayAdminJsonContext.Default.GatewayClientGenerationManifestV1)
            ?? throw new InvalidOperationException("Manifest serialization failed.");
        byte[] manifestBytes = GatewayCanonicalJson.Serialize(manifestNode);
        byte[] openApiDigest = Hash("HPD.Gateway.OpenApi.v1\0", openApiBytes);
        byte[] manifestDigest = Hash("HPD.Gateway.ClientManifest.v1\0", manifestBytes);
        byte[] sourceDigest = HashPair("HPD.Gateway.ClientSnapshot.v1\0", openApiDigest, manifestDigest);
        return new(1, "sha-256", Convert.ToHexStringLower(openApiDigest), Convert.ToHexStringLower(manifestDigest),
            Convert.ToHexStringLower(sourceDigest), openApi, manifest);
    }

    private static GatewayClientGenerationManifestV1 ProjectFromManagedLedger(string securityScheme)
    {
        var operations = ImmutableArray.CreateBuilder<GatewayClientOperationV1>(GatewayAdminEndpointLedger.V1.Length);
        foreach (GatewayAdminEndpointDescriptor endpoint in GatewayAdminEndpointLedger.V1.OrderBy(x => x.Operation, StringComparer.Ordinal))
        {
            GatewayAdminClientOperationSemantics semantic = GatewayAdminClientSemanticLedger.For(endpoint.Operation);
            string? requestRef = semantic.RequestType is null ? null : Ref(semantic.RequestType);
            operations.Add(new(endpoint.Operation, "HpdGatewayAdmin." + endpoint.Operation, endpoint.Method,
                "/management/gateway/v1" + endpoint.Pattern, endpoint.Capability, endpoint.ResourcePolicy,
                Resource(endpoint.ResourceKind), endpoint.Mutation, Kebab(semantic.Idempotency),
                Kebab(semantic.DesiredPrecondition), semantic.ProtectedNotFound,
                new(semantic.SuccessStatus, Ref(semantic.SuccessType), Kebab(semantic.SuccessMeaning)),
                semantic.DocumentedErrors.Order().ToImmutableArray(),
                new(Kebab(semantic.RequestBodyPresence), requestRef,
                    requestRef is null ? [] : ["application/hpd.gateway+json", "application/json"]),
                new(Kebab(semantic.Pagination.Kind), semantic.Pagination.DefaultMaximum,
                    semantic.Pagination.MinimumMaximum, semantic.Pagination.MaximumMaximum),
                semantic.ParameterConstraints.OrderBy(x => (byte)x.Location).ThenBy(x => x.Name, StringComparer.Ordinal)
                    .Select(x => new GatewayParameterConstraintV1(Kebab(x.Location), x.Name, x.Required, Kebab(x.Brand), Rule(x.Rules)))
                    .ToImmutableArray()));
        }
        ImmutableArray<GatewaySchemaConstraintV1> schemas = GatewayAdminClientSchemaConstraintLedger.V1
            .Select(x => new GatewaySchemaConstraintV1(Ref(x.SchemaType), "/properties/" + Pointer(x.PropertyName),
                Kebab(x.AppliesTo), Kebab(x.Brand), Rule(x.Rules)))
            .OrderBy(x => x.SchemaRef, StringComparer.Ordinal).ThenBy(x => x.PropertyPointer, StringComparer.Ordinal)
            .ThenBy(x => x.AppliesTo, StringComparer.Ordinal).ToImmutableArray();
        return new(1, "1.0.0", "hpd-gateway-v1", securityScheme, operations.MoveToImmutable(), schemas);
    }

    private static GatewayConstraintRulesV1 Rule(GatewayAdminClientConstraintRules x) => new(
        x.MinimumUtf8Bytes, x.MaximumUtf8Bytes, Kebab(x.Normalization), Kebab(x.CharacterSet), x.RejectUnicodeControls,
        x.CollectionMinimum, x.CollectionMaximum, Kebab(x.Uniqueness), Kebab(x.Ordering), Kebab(x.Cardinality));
    private static string Ref(Type type) => "#/components/schemas/" + GatewayAdminSchemaReferenceIds.Create(type);
    private static string Pointer(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static string Resource(GatewayAdminResourceKind? x) => x?.ToString().ToLowerInvariant() ?? "none";
    private static string Kebab<T>(T x) where T : struct, Enum
    {
        if (x is GatewayAdminClientNormalization normalization && normalization == GatewayAdminClientNormalization.Nfc)
            return "NFC";
        return string.Concat(x.ToString().Select((c, i) => char.IsUpper(c) && i > 0
            ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }

    private static byte[] Hash(string frame, byte[] value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(frame));
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
        return hash.GetHashAndReset();
    }

    private static byte[] HashPair(string frame, byte[] first, byte[] second)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(frame));
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)first.Length); hash.AppendData(length); hash.AppendData(first);
        BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)second.Length); hash.AppendData(length); hash.AppendData(second);
        return hash.GetHashAndReset();
    }
}

internal static class GatewayClientOpenApiJsonValidator
{
    internal static void Validate(JsonObject document, string securityScheme)
    {
        if (document["openapi"]?.GetValue<string>() is not { } version || !version.StartsWith("3.1.", StringComparison.Ordinal))
            throw new InvalidOperationException("Gateway client OpenAPI must be version 3.1.x.");
        JsonObject schemes = document["components"]?["securitySchemes"]?.AsObject() ??
            throw new InvalidOperationException("Gateway client OpenAPI has no security scheme catalog.");
        if (schemes.Count != 1 || schemes[securityScheme] is not JsonObject scheme ||
            scheme["type"]?.GetValue<string>() != "http" || scheme["scheme"]?.GetValue<string>() != "bearer" ||
            scheme["bearerFormat"]?.GetValue<string>() != "JWT" || scheme.Count != 3)
            throw new InvalidOperationException("Gateway client OpenAPI security contract drifted.");
        JsonObject schemas = document["components"]?["schemas"]?.AsObject() ??
            throw new InvalidOperationException("Gateway client OpenAPI has no component schemas.");
        if (schemas.Count > 512) throw new InvalidOperationException("Gateway client OpenAPI schema bound exceeded.");

        JsonObject paths = document["paths"]?.AsObject() ?? throw new InvalidOperationException("Gateway client OpenAPI has no paths.");
        if (paths.Count != GatewayAdminEndpointLedger.V1.Select(x => x.Pattern).Distinct(StringComparer.Ordinal).Count())
            throw new InvalidOperationException("Gateway client OpenAPI path count drifted.");
        foreach (GatewayAdminEndpointDescriptor endpoint in GatewayAdminEndpointLedger.V1)
        {
            GatewayAdminClientOperationSemantics semantic = GatewayAdminClientSemanticLedger.For(endpoint.Operation);
            JsonObject operation = paths["/management/gateway/v1" + endpoint.Pattern]?[endpoint.Method.ToLowerInvariant()]?.AsObject() ??
                throw new InvalidOperationException($"Gateway client OpenAPI operation is missing: {endpoint.Operation}.");
            if (operation["operationId"]?.GetValue<string>() != "HpdGatewayAdmin." + endpoint.Operation)
                throw new InvalidOperationException($"Gateway client OpenAPI operation ID drifted: {endpoint.Operation}.");
            JsonObject responses = operation["responses"]?.AsObject() ?? throw new InvalidOperationException("OpenAPI responses are missing.");
            string[] expectedStatuses = [semantic.SuccessStatus.ToString(System.Globalization.CultureInfo.InvariantCulture),
                .. semantic.DocumentedErrors.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))];
            if (!responses.Select(x => x.Key).Order(StringComparer.Ordinal).SequenceEqual(expectedStatuses.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidOperationException($"Gateway client OpenAPI response statuses drifted: {endpoint.Operation}.");
            RequireRef(responses[semantic.SuccessStatus.ToString(System.Globalization.CultureInfo.InvariantCulture)]?["content"]?["application/json"]?["schema"], semantic.SuccessType, schemas);
            if (semantic.RequestType is { } request)
            {
                JsonObject body = operation["requestBody"]?.AsObject() ?? throw new InvalidOperationException("OpenAPI request body is missing.");
                bool required = body["required"]?.GetValue<bool>() ?? false;
                if (required != (semantic.RequestBodyPresence == GatewayAdminClientRequestBodyPresence.Required))
                    throw new InvalidOperationException("OpenAPI request-body presence drifted.");
                RequireRef(body["content"]?["application/json"]?["schema"], request, schemas);
            }
            else if (operation["requestBody"] is not null) throw new InvalidOperationException("OpenAPI added an unexpected request body.");
        }
        foreach (GatewayAdminClientSchemaConstraint target in GatewayAdminClientSchemaConstraintLedger.V1)
        {
            string id = GatewayAdminSchemaReferenceIds.Create(target.SchemaType)!;
            if (schemas[id]?["properties"]?[target.PropertyName] is null)
                throw new InvalidOperationException($"Gateway client OpenAPI schema target is missing: {id}/{target.PropertyName}.");
        }
    }

    private static void RequireRef(JsonNode? node, Type expected, JsonObject schemas)
    {
        string id = GatewayAdminSchemaReferenceIds.Create(expected)!;
        if (node?["$ref"]?.GetValue<string>() != "#/components/schemas/" + id || schemas[id] is null)
            throw new InvalidOperationException($"Gateway client OpenAPI schema reference drifted: {expected.FullName}.");
    }
}

internal static class GatewayCanonicalJson
{
    internal static byte[] Serialize(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default }))
            Write(writer, node, 0);
        return stream.ToArray();
    }

    private static void Write(Utf8JsonWriter writer, JsonNode? node, int depth)
    {
        if (depth > 64) throw new InvalidOperationException("Canonical JSON exceeds maximum depth.");
        switch (node)
        {
            case null: writer.WriteNullValue(); break;
            case JsonObject value:
                writer.WriteStartObject();
                foreach ((string name, JsonNode? child) in value.OrderBy(x => x.Key, StringComparer.Ordinal))
                { writer.WritePropertyName(name); Write(writer, child, depth + 1); }
                writer.WriteEndObject();
                break;
            case JsonArray value:
                if (value.Count > 10_000) throw new InvalidOperationException("Canonical JSON array exceeds its bound.");
                writer.WriteStartArray(); foreach (JsonNode? child in value) Write(writer, child, depth + 1); writer.WriteEndArray();
                break;
            case JsonValue value:
                if (value.TryGetValue<string>(out string? text)) { writer.WriteStringValue(text); break; }
                if (value.TryGetValue<bool>(out bool boolean)) { writer.WriteBooleanValue(boolean); break; }
                if (value.TryGetValue<int>(out int integer)) { writer.WriteNumberValue(integer); break; }
                if (value.TryGetValue<long>(out long longInteger)) { writer.WriteNumberValue(longInteger); break; }
                if (value.TryGetValue<uint>(out uint unsignedInteger)) { writer.WriteNumberValue(unsignedInteger); break; }
                if (value.TryGetValue<ulong>(out ulong unsignedLong)) { writer.WriteNumberValue(unsignedLong); break; }
                JsonElement element = value.GetValue<JsonElement>();
                switch (element.ValueKind)
                {
                    case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
                    case JsonValueKind.Number:
                        if (!element.TryGetInt64(out long number)) throw new InvalidOperationException("Canonical JSON permits integers only.");
                        writer.WriteNumberValue(number); break;
                    case JsonValueKind.True: writer.WriteBooleanValue(true); break;
                    case JsonValueKind.False: writer.WriteBooleanValue(false); break;
                    case JsonValueKind.Null: writer.WriteNullValue(); break;
                    default: throw new InvalidOperationException("Unsupported canonical JSON value.");
                }
                break;
            default: throw new InvalidOperationException("Unsupported canonical JSON node.");
        }
    }
}
