using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HPD.Gateway.Admin;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(GatewayClientGenerationManifestV1))]
internal sealed partial class GatewayClientGenerationJsonContext : JsonSerializerContext;

internal sealed record GatewayClientGenerationManifestV1(
    int SchemaVersion,
    string ApiVersion,
    string OpenApiDocumentName,
    string SecurityScheme,
    ImmutableArray<GatewayClientOperationV1> Operations,
    ImmutableArray<GatewaySchemaConstraintV1> SchemaConstraints);

internal sealed record GatewayClientOperationV1(
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

internal sealed record GatewayClientSuccessV1(int Status, string SchemaRef, string Meaning);
internal sealed record GatewayClientRequestBodyV1(string Presence, string? SchemaRef, ImmutableArray<string> MediaTypes);
internal sealed record GatewayClientPaginationV1(string Kind, int? DefaultMaximum, int? MinimumMaximum, int? MaximumMaximum);
internal sealed record GatewayParameterConstraintV1(string Location, string Name, bool Required, string Brand, GatewayConstraintRulesV1 Rules);
internal sealed record GatewaySchemaConstraintV1(string SchemaRef, string PropertyPointer, string AppliesTo, string Brand, GatewayConstraintRulesV1 Rules);
internal sealed record GatewayConstraintRulesV1(
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

internal sealed class GatewayClientGenerationSnapshotV1
{
    private GatewayClientGenerationSnapshotV1(
        string openApiSha256,
        string manifestSha256,
        string sourceSha256,
        GatewayClientGenerationManifestV1 manifest,
        ImmutableArray<byte> snapshotUtf8)
    {
        OpenApiSha256 = openApiSha256;
        ManifestSha256 = manifestSha256;
        SourceSha256 = sourceSha256;
        Manifest = manifest;
        SnapshotUtf8 = snapshotUtf8;
    }

    internal int SnapshotVersion => 1;
    internal string HashAlgorithm => "sha-256";
    internal string OpenApiSha256 { get; }
    internal string ManifestSha256 { get; }
    internal string SourceSha256 { get; }
    internal GatewayClientGenerationManifestV1 Manifest { get; }
    internal ImmutableArray<byte> SnapshotUtf8 { get; }

    internal static GatewayClientGenerationSnapshotV1 Create(ReadOnlySpan<byte> openApiUtf8, string securityScheme)
    {
        JsonObject openApi = GatewayBoundedJson.ParseObject(openApiUtf8);
        return Create(openApi, securityScheme);
    }

    private static GatewayClientGenerationSnapshotV1 Create(JsonObject openApi, string securityScheme)
    {
        ArgumentNullException.ThrowIfNull(openApi);
        GatewayClientOpenApiJsonValidator.Validate(openApi, securityScheme);
        GatewayClientGenerationManifestV1 manifest = ProjectFromManagedLedger(securityScheme);
        byte[] openApiBytes = GatewayCanonicalJson.Serialize(openApi);
        JsonNode manifestNode = JsonSerializer.SerializeToNode(manifest, GatewayClientGenerationJsonContext.Default.GatewayClientGenerationManifestV1)
            ?? throw new InvalidOperationException("Manifest serialization failed.");
        byte[] manifestBytes = GatewayCanonicalJson.Serialize(manifestNode);
        byte[] openApiDigest = Hash("HPD.Gateway.OpenApi.v1\0", openApiBytes);
        byte[] manifestDigest = Hash("HPD.Gateway.ClientManifest.v1\0", manifestBytes);
        byte[] sourceDigest = HashPair("HPD.Gateway.ClientSnapshot.v1\0", openApiDigest, manifestDigest);
        string openApiHash = Convert.ToHexStringLower(openApiDigest);
        string manifestHash = Convert.ToHexStringLower(manifestDigest);
        string sourceHash = Convert.ToHexStringLower(sourceDigest);
        var envelope = new JsonObject
        {
            ["snapshotVersion"] = 1,
            ["hashAlgorithm"] = "sha-256",
            ["openApiSha256"] = openApiHash,
            ["manifestSha256"] = manifestHash,
            ["sourceSha256"] = sourceHash,
            ["openApi"] = openApi.DeepClone(),
            ["manifest"] = manifestNode.DeepClone(),
        };
        ImmutableArray<byte> snapshotBytes = GatewayCanonicalJson.Serialize(envelope).ToImmutableArray();
        if (snapshotBytes.Length > 8 * 1024 * 1024)
            throw new InvalidOperationException("Gateway client generation snapshot exceeds 8 MiB.");
        return new(openApiHash, manifestHash, sourceHash, manifest, snapshotBytes);
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

internal static class GatewayBoundedJson
{
    private const int MaximumBytes = 8 * 1024 * 1024;
    private const int MaximumTokens = 750_000;
    // The schema component catalog has a dedicated 512-entry contract. All
    // other objects are subsequently constrained by their closed validators.
    private const int MaximumProperties = 512;
    private const int MaximumArrayItems = 10_000;
    private const int MaximumStringUtf8Bytes = 16 * 1024;

    internal static JsonObject ParseObject(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length is < 2 or > MaximumBytes)
            throw new InvalidOperationException("Gateway client generation JSON is outside its byte bound.");
        RejectLoneEscapedSurrogates(utf8);
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 64,
        });
        var frames = new Stack<Frame>();
        int tokens = 0;
        while (reader.Read())
        {
            if (++tokens > MaximumTokens)
                throw new InvalidOperationException("Gateway client generation JSON exceeds its token bound.");
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    CountArrayItem(frames); frames.Push(new Frame(true)); break;
                case JsonTokenType.StartArray:
                    CountArrayItem(frames); frames.Push(new Frame(false)); break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    if (frames.Count == 0) throw new InvalidOperationException("Gateway client generation JSON is malformed.");
                    frames.Pop(); break;
                case JsonTokenType.PropertyName:
                    if (frames.Count == 0 || !frames.Peek().IsObject)
                        throw new InvalidOperationException("Gateway client generation property is outside an object.");
                    Frame owner = frames.Peek();
                    if (++owner.Count > MaximumProperties)
                        throw new InvalidOperationException("Gateway client generation object exceeds its property bound.");
                    string name = reader.GetString()!;
                    if (Encoding.UTF8.GetByteCount(name) > MaximumStringUtf8Bytes || !owner.Names!.Add(name))
                        throw new InvalidOperationException("Gateway client generation object has an invalid or duplicate property.");
                    break;
                case JsonTokenType.String:
                    CountArrayItem(frames);
                    long length = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
                    if (length > MaximumStringUtf8Bytes)
                        throw new InvalidOperationException("Gateway client generation string exceeds its UTF-8 bound.");
                    break;
                default:
                    CountArrayItem(frames); break;
            }
        }
        if (frames.Count != 0 || tokens == 0)
            throw new InvalidOperationException("Gateway client generation JSON is incomplete.");
        return JsonNode.Parse(utf8)?.AsObject() ??
            throw new InvalidOperationException("Gateway client generation JSON root must be an object.");
    }

    private static void RejectLoneEscapedSurrogates(ReadOnlySpan<byte> utf8)
    {
        bool inString = false;
        for (int index = 0; index < utf8.Length; index++)
        {
            byte current = utf8[index];
            if (current == (byte)'"') { inString = !inString; continue; }
            if (!inString || current != (byte)'\\') continue;
            if (++index >= utf8.Length) throw new InvalidOperationException("Gateway client generation JSON is malformed.");
            if (utf8[index] != (byte)'u') continue;
            int scalar = ReadHexEscape(utf8, index + 1);
            index += 4;
            if (scalar is >= 0xD800 and <= 0xDBFF)
            {
                if (index + 6 >= utf8.Length || utf8[index + 1] != (byte)'\\' || utf8[index + 2] != (byte)'u')
                    throw new InvalidOperationException("Canonical JSON rejects lone UTF-16 surrogates.");
                int low = ReadHexEscape(utf8, index + 3);
                if (low is < 0xDC00 or > 0xDFFF)
                    throw new InvalidOperationException("Canonical JSON rejects lone UTF-16 surrogates.");
                index += 6;
            }
            else if (scalar is >= 0xDC00 and <= 0xDFFF)
                throw new InvalidOperationException("Canonical JSON rejects lone UTF-16 surrogates.");
        }
    }

    private static int ReadHexEscape(ReadOnlySpan<byte> utf8, int start)
    {
        if (start + 4 > utf8.Length) throw new InvalidOperationException("Gateway client generation JSON is malformed.");
        int result = 0;
        for (int index = start; index < start + 4; index++)
        {
            int digit = utf8[index] switch
            {
                >= (byte)'0' and <= (byte)'9' => utf8[index] - (byte)'0',
                >= (byte)'a' and <= (byte)'f' => utf8[index] - (byte)'a' + 10,
                >= (byte)'A' and <= (byte)'F' => utf8[index] - (byte)'A' + 10,
                _ => throw new InvalidOperationException("Gateway client generation JSON has an invalid Unicode escape."),
            };
            result = (result << 4) | digit;
        }
        return result;
    }

    private static void CountArrayItem(Stack<Frame> frames)
    {
        if (frames.Count == 0 || frames.Peek().IsObject) return;
        if (++frames.Peek().Count > MaximumArrayItems)
            throw new InvalidOperationException("Gateway client generation array exceeds its item bound.");
    }

    private sealed class Frame(bool isObject)
    {
        internal bool IsObject { get; } = isObject;
        internal int Count;
        internal HashSet<string>? Names { get; } = isObject ? new(StringComparer.Ordinal) : null;
    }
}

internal static class GatewayClientOpenApiJsonValidator
{
    private static readonly HashSet<string> RootFields = new(["openapi", "info", "paths", "components"], StringComparer.Ordinal);
    private static readonly HashSet<string> InfoFields = new(["title", "version"], StringComparer.Ordinal);
    private static readonly HashSet<string> OperationFields = new(["operationId", "parameters", "requestBody", "responses", "security"], StringComparer.Ordinal);
    private static readonly HashSet<string> ParameterFields = new(["name", "in", "required", "description", "schema"], StringComparer.Ordinal);
    private static readonly HashSet<string> RequestBodyFields = new(["required", "description", "content"], StringComparer.Ordinal);
    private static readonly HashSet<string> ResponseFields = new(["description", "content"], StringComparer.Ordinal);
    private static readonly HashSet<string> MediaTypeFields = new(["schema"], StringComparer.Ordinal);
    private static readonly HashSet<string> SchemaFields = new([
        "$ref", "type", "title", "description", "format", "properties", "required", "additionalProperties",
        "items", "minItems", "maxItems", "uniqueItems", "minLength", "maxLength", "pattern", "minimum",
        "maximum", "default", "enum", "const", "oneOf", "discriminator"], StringComparer.Ordinal);

    internal static void Validate(JsonObject document, string securityScheme)
    {
        RequireFields(document, RootFields, "OpenAPI document");
        RequireFields(document["info"]?.AsObject() ?? throw new InvalidOperationException("OpenAPI info is missing."), InfoFields, "OpenAPI info");
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
            RequireFields(operation, OperationFields, $"OpenAPI operation '{endpoint.Operation}'");
            JsonArray security = operation["security"]?.AsArray() ?? throw new InvalidOperationException("OpenAPI operation security is missing.");
            if (security.Count != 1 || security[0] is not JsonObject requirement || requirement.Count != 1 ||
                requirement[securityScheme] is not JsonArray scopes || scopes.Count != 0)
                throw new InvalidOperationException("OpenAPI operation security does not match the sealed scheme.");
            if (operation["operationId"]?.GetValue<string>() != "HpdGatewayAdmin." + endpoint.Operation)
                throw new InvalidOperationException($"Gateway client OpenAPI operation ID drifted: {endpoint.Operation}.");
            JsonObject responses = operation["responses"]?.AsObject() ?? throw new InvalidOperationException("OpenAPI responses are missing.");
            string[] expectedStatuses = [semantic.SuccessStatus.ToString(System.Globalization.CultureInfo.InvariantCulture),
                .. semantic.DocumentedErrors.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))];
            if (!responses.Select(x => x.Key).Order(StringComparer.Ordinal).SequenceEqual(expectedStatuses.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidOperationException($"Gateway client OpenAPI response statuses drifted: {endpoint.Operation}.");
            RequireRef(responses[semantic.SuccessStatus.ToString(System.Globalization.CultureInfo.InvariantCulture)]?["content"]?["application/json"]?["schema"], semantic.SuccessType, schemas);
            foreach ((string _, JsonNode? responseNode) in responses)
            {
                JsonObject response = responseNode?.AsObject() ?? throw new InvalidOperationException("OpenAPI response must be an object.");
                RequireFields(response, ResponseFields, "OpenAPI response");
                ValidateContent(response["content"]?.AsObject());
            }
            JsonArray parameterNodes = operation["parameters"]?.AsArray() ?? [];
            string[] actualParameters = parameterNodes.Select(node =>
                $"{node!["in"]!.GetValue<string>()}:{node["name"]!.GetValue<string>()}").Order(StringComparer.Ordinal).ToArray();
            string[] expectedParameters = semantic.ParameterConstraints.Select(x =>
                $"{x.Location.ToString().ToLowerInvariant()}:{x.Name}").Order(StringComparer.Ordinal).ToArray();
            if (!actualParameters.SequenceEqual(expectedParameters, StringComparer.Ordinal))
                throw new InvalidOperationException($"OpenAPI parameters drifted: {endpoint.Operation}.");
            foreach (JsonNode? parameterNode in parameterNodes)
            {
                JsonObject parameter = parameterNode?.AsObject() ?? throw new InvalidOperationException("OpenAPI parameter must be an object.");
                RequireFields(parameter, ParameterFields, "OpenAPI parameter");
                ValidateSchema(parameter["schema"]?.AsObject() ?? throw new InvalidOperationException("OpenAPI parameter schema is missing."), schemas, new());
            }
            if (semantic.RequestType is { } request)
            {
                JsonObject body = operation["requestBody"]?.AsObject() ?? throw new InvalidOperationException("OpenAPI request body is missing.");
                RequireFields(body, RequestBodyFields, "OpenAPI request body");
                ValidateContent(body["content"]?.AsObject());
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
            if (schemas[id]?["properties"]?[target.PropertyName] is not JsonObject property)
                throw new InvalidOperationException($"Gateway client OpenAPI schema target is missing: {id}/{target.PropertyName}.");
            JsonObject projected = target.AppliesTo == GatewayAdminClientSchemaConstraintTarget.Items
                ? property["items"]?.AsObject() ?? throw new InvalidOperationException("OpenAPI item constraint target is missing.")
                : property;
            CorrelateRules(target, projected);
        }
        foreach ((string id, JsonNode? schema) in schemas)
            ValidateSchema(schema?.AsObject() ?? throw new InvalidOperationException($"OpenAPI schema '{id}' is invalid."), schemas, new());
    }

    private static void CorrelateRules(GatewayAdminClientSchemaConstraint target, JsonObject schema)
    {
        GatewayAdminClientConstraintRules rules = target.Rules;
        if (target.AppliesTo == GatewayAdminClientSchemaConstraintTarget.Collection)
        {
            if (schema["minItems"]?.GetValue<int>() != rules.CollectionMinimum ||
                schema["maxItems"]?.GetValue<int>() != rules.CollectionMaximum)
                throw new InvalidOperationException("OpenAPI collection constraint drifted from managed semantics.");
            return;
        }
        if (target.Brand == GatewayAdminClientStringBrand.None && rules == new GatewayAdminClientConstraintRules()) return;
        int expectedMinimum = rules.CharacterSet is GatewayAdminClientCharacterSet.VisibleAscii or
            GatewayAdminClientCharacterSet.LowercaseAsciiName or GatewayAdminClientCharacterSet.AsciiArtifactLabel or
            GatewayAdminClientCharacterSet.StrongEntityTag ? rules.MinimumUtf8Bytes ?? 0 : rules.MinimumUtf8Bytes > 0 ? 1 : 0;
        int actualMinimum = schema["minLength"]?.GetValue<int>() ?? 0;
        if (actualMinimum != expectedMinimum || schema["maxLength"]?.GetValue<int>() != rules.MaximumUtf8Bytes)
            throw new InvalidOperationException("OpenAPI string constraint drifted from managed semantics.");
    }

    private static void ValidateContent(JsonObject? content)
    {
        if (content is null || content.Count == 0) throw new InvalidOperationException("OpenAPI content is missing.");
        foreach ((string mediaType, JsonNode? mediaNode) in content)
        {
            if (mediaType is not ("application/json" or "application/hpd.gateway+json"))
                throw new InvalidOperationException("OpenAPI contains an unsupported media type.");
            JsonObject media = mediaNode?.AsObject() ?? throw new InvalidOperationException("OpenAPI media type is invalid.");
            RequireFields(media, MediaTypeFields, "OpenAPI media type");
        }
    }

    private static void ValidateSchema(JsonObject schema, JsonObject components, HashSet<string> path)
    {
        RequireFields(schema, SchemaFields, "OpenAPI schema");
        if (schema["$ref"] is JsonValue reference)
        {
            string value = reference.GetValue<string>();
            const string prefix = "#/components/schemas/";
            if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length == prefix.Length || components[value[prefix.Length..]] is null)
                throw new InvalidOperationException("OpenAPI contains a non-local or unresolved schema reference.");
            return;
        }
        if (schema["properties"] is JsonObject properties)
            foreach ((string _, JsonNode? child) in properties)
                ValidateSchema(child?.AsObject() ?? throw new InvalidOperationException("OpenAPI property schema is invalid."), components, path);
        if (schema["items"] is JsonObject items) ValidateSchema(items, components, path);
        if (schema["additionalProperties"] is JsonObject additional) ValidateSchema(additional, components, path);
        if (schema["oneOf"] is JsonArray branches)
            foreach (JsonNode? branch in branches)
                ValidateSchema(branch?.AsObject() ?? throw new InvalidOperationException("OpenAPI union branch is invalid."), components, path);
        if (schema["format"]?.GetValue<string>() is { } format && format is not ("int32" or "int64" or "uint16" or "uint64" or "date-time" or "uri" or "uuid"))
            throw new InvalidOperationException($"OpenAPI schema contains unsupported format '{format}'.");
    }

    private static void RequireFields(JsonObject value, HashSet<string> allowed, string scope)
    {
        string? unknown = value.Select(x => x.Key).FirstOrDefault(key => !allowed.Contains(key));
        if (unknown is not null) throw new InvalidOperationException($"{scope} contains unsupported field '{unknown}'.");
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
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            Write(writer, node, 0, "");
        return stream.ToArray();
    }

    private static void Write(Utf8JsonWriter writer, JsonNode? node, int depth, string path)
    {
        if (depth > 64) throw new InvalidOperationException("Canonical JSON exceeds maximum depth.");
        switch (node)
        {
            case null: writer.WriteNullValue(); break;
            case JsonObject value:
                int maximumProperties = path is "/components/schemas" or "/openApi/components/schemas" ? 512 : 256;
                if (value.Count > maximumProperties)
                    throw new InvalidOperationException($"Canonical JSON object exceeds {maximumProperties} properties.");
                foreach ((string name, JsonNode? _) in value) ValidateUnicode(name);
                writer.WriteStartObject();
                foreach ((string name, JsonNode? child) in value.OrderBy(x => x.Key, UnicodeScalarComparer.Instance))
                {
                    writer.WritePropertyName(name);
                    Write(writer, child, depth + 1, path + "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
                }
                writer.WriteEndObject();
                break;
            case JsonArray value:
                if (value.Count > 10_000) throw new InvalidOperationException("Canonical JSON array exceeds its bound.");
                writer.WriteStartArray(); foreach (JsonNode? child in value) Write(writer, child, depth + 1, path); writer.WriteEndArray();
                break;
            case JsonValue value:
                if (value.TryGetValue<string>(out string? text)) { ValidateUnicode(text); writer.WriteStringValue(text); break; }
                if (value.TryGetValue<bool>(out bool boolean)) { writer.WriteBooleanValue(boolean); break; }
                if (value.TryGetValue<int>(out int integer)) { writer.WriteNumberValue(integer); break; }
                if (value.TryGetValue<long>(out long longInteger)) { writer.WriteNumberValue(longInteger); break; }
                if (value.TryGetValue<uint>(out uint unsignedInteger)) { writer.WriteNumberValue(unsignedInteger); break; }
                if (value.TryGetValue<ulong>(out ulong unsignedLong)) { writer.WriteNumberValue(unsignedLong); break; }
                JsonElement element = value.GetValue<JsonElement>();
                switch (element.ValueKind)
                {
                    case JsonValueKind.String:
                        string? elementText = element.GetString();
                        ValidateUnicode(elementText);
                        writer.WriteStringValue(elementText);
                        break;
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

    private static void ValidateUnicode(string? value)
    {
        if (value is null) return;
        ReadOnlySpan<char> remaining = value;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out int consumed) != System.Buffers.OperationStatus.Done)
                throw new InvalidOperationException("Canonical JSON rejects lone UTF-16 surrogates.");
            remaining = remaining[consumed..];
        }
    }

    private sealed class UnicodeScalarComparer : IComparer<string>
    {
        internal static UnicodeScalarComparer Instance { get; } = new();
        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            ValidateUnicode(left);
            ValidateUnicode(right);
            ReadOnlySpan<char> leftSpan = left;
            ReadOnlySpan<char> rightSpan = right;
            while (!leftSpan.IsEmpty && !rightSpan.IsEmpty)
            {
                Rune.DecodeFromUtf16(leftSpan, out Rune leftRune, out int leftConsumed);
                Rune.DecodeFromUtf16(rightSpan, out Rune rightRune, out int rightConsumed);
                int comparison = leftRune.Value.CompareTo(rightRune.Value);
                if (comparison != 0) return comparison;
                leftSpan = leftSpan[leftConsumed..];
                rightSpan = rightSpan[rightConsumed..];
            }
            return leftSpan.IsEmpty ? rightSpan.IsEmpty ? 0 : -1 : 1;
        }
    }
}
