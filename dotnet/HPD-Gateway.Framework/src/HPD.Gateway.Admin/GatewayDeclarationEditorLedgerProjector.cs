using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace HPD.Gateway.Admin;

internal static class GatewayDeclarationEditorLedgerProjector
{
    private const string Prefix = "#/components/schemas/";
    private const string RootName = "HPD_Gateway_Abstractions_GatewayConfiguration";
    private const string ExpectedOccurrenceCatalogSha256 =
        "5a038440d811821949679bae4806d026de31f53bc2d16d966f228628dd1a1867";

    internal static GatewayDeclarationEditorLedgerEnvelope Project(JsonObject openApi)
    {
        ArgumentNullException.ThrowIfNull(openApi);
        JsonObject schemas = openApi["components"]?["schemas"]?.AsObject() ??
            throw new InvalidOperationException("Gateway editor projection requires component schemas.");
        JsonObject root = schemas[RootName]?.AsObject() ??
            throw new InvalidOperationException("Gateway editor projection requires GatewayConfiguration.");
        var projection = new Projection(schemas);
        projection.WalkObject(root, [], RootName, string.Empty);
        ImmutableArray<GatewayEditorFieldRecord> records = projection.Records
            .Order(Comparer.Instance).ToImmutableArray();
        ValidateOccurrenceCatalog(records);
        return new(1, Prefix + RootName, records);
    }

    private static void ValidateOccurrenceCatalog(ImmutableArray<GatewayEditorFieldRecord> records)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd.gateway.editor-occurrence-catalog.v1\0"u8);
        Span<byte> length = stackalloc byte[8];
        foreach (GatewayEditorFieldRecord record in records)
        {
            byte[] value = Encoding.UTF8.GetBytes(PathKey(record.Target.OccurrencePath));
            BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)value.Length);
            hash.AppendData(length);
            hash.AppendData(value);
        }
        string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!string.Equals(actual, ExpectedOccurrenceCatalogSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Gateway editor occurrence catalog drifted: " + actual);
    }

    private sealed class Projection
    {
        private readonly JsonObject _schemas;
        private readonly Dictionary<string, ImmutableArray<string>> _componentsByShape;
        private readonly Dictionary<string, ImmutableArray<GatewayEditorConstraintTarget>> _constraints;

        internal Projection(JsonObject schemas)
        {
            _schemas = schemas;
            var componentsByShape = new Dictionary<string, ImmutableArray<string>.Builder>(StringComparer.Ordinal);
            foreach ((string name, JsonNode? node) in schemas)
            {
                if (!name.StartsWith("HPD_Gateway_Abstractions_", StringComparison.Ordinal) || node is not JsonObject schema)
                    continue;
                string shape = Convert.ToBase64String(GatewayCanonicalJson.Serialize(schema));
                if (!componentsByShape.TryGetValue(shape, out var names))
                    componentsByShape.Add(shape, names = ImmutableArray.CreateBuilder<string>());
                names.Add(name);
            }
            _componentsByShape = componentsByShape.ToDictionary(static item => item.Key,
                static item => item.Value.Order(StringComparer.Ordinal).ToImmutableArray(), StringComparer.Ordinal);
            _constraints = GatewayAdminClientSchemaConstraintLedger.V1
                .Where(static item => item.SchemaType.Namespace == "HPD.Gateway.Abstractions")
                .Select(static item => new
                {
                    Key = Prefix + GatewayAdminSchemaReferenceIds.Create(item.SchemaType) +
                        "/properties/" + Escape(item.PropertyName),
                    Value = new GatewayEditorConstraintTarget(
                        Prefix + GatewayAdminSchemaReferenceIds.Create(item.SchemaType),
                        "/properties/" + Escape(item.PropertyName),
                        item.AppliesTo switch
                        {
                            GatewayAdminClientSchemaConstraintTarget.Value => GatewayEditorConstraintAppliesTo.Value,
                            GatewayAdminClientSchemaConstraintTarget.Collection => GatewayEditorConstraintAppliesTo.Collection,
                            GatewayAdminClientSchemaConstraintTarget.Items => GatewayEditorConstraintAppliesTo.Items,
                            _ => throw new InvalidOperationException("Unknown schema-constraint target."),
                        }),
                })
                .GroupBy(static item => item.Key, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key,
                    static group => group.Select(static item => item.Value)
                        .OrderBy(static item => item.SchemaRef, StringComparer.Ordinal)
                        .ThenBy(static item => item.PropertyPointer, StringComparer.Ordinal)
                        .ThenBy(static item => item.AppliesTo)
                        .ToImmutableArray(), StringComparer.Ordinal);
        }

        internal List<GatewayEditorFieldRecord> Records { get; } = [];

        internal void WalkObject(JsonObject schema, ImmutableArray<GatewayEditorOccurrenceStep> path,
            string owner, string ownerPointer)
        {
            JsonObject properties = schema["properties"]?.AsObject() ?? new JsonObject();
            HashSet<string> required = schema["required"] is JsonArray requiredArray
                ? requiredArray.Select(static item => item?.GetValue<string>() ?? string.Empty)
                    .ToHashSet(StringComparer.Ordinal)
                : new(StringComparer.Ordinal);
            foreach ((string propertyName, JsonNode? propertyNode) in properties)
            {
                JsonObject property = propertyNode?.AsObject() ??
                    throw new InvalidOperationException("Gateway editor schema property is malformed.");
                ImmutableArray<GatewayEditorOccurrenceStep> propertyPath =
                    [.. path, new(GatewayEditorOccurrenceStepKind.Property, propertyName, null)];
                string propertyPointer = ownerPointer + "/properties/" + Escape(propertyName);
                bool isRequired = required.Contains(propertyName);
                Add(propertyPath, owner, propertyPointer, property, isRequired,
                    property["oneOf"] is JsonArray ? GatewayEditorStructuralReason.UnionBoundary : null);

                if (property["oneOf"] is JsonArray)
                {
                    WalkUnion(property, propertyPath);
                    continue;
                }
                string type = Type(property);
                if (type == "array" && property["items"] is JsonObject items)
                {
                    ImmutableArray<GatewayEditorOccurrenceStep> itemPath =
                        [.. propertyPath, new(GatewayEditorOccurrenceStepKind.Items, null, null)];
                    string itemPointer = propertyPointer + "/items";
                    if (IsScalar(items))
                        Add(itemPath, owner, itemPointer, items, true, null);
                    else
                        WalkNested(items, itemPath, owner, itemPointer);
                }
                else if (type == "object" || property["properties"] is JsonObject || property["$ref"] is not null)
                {
                    WalkNested(property, propertyPath, owner, propertyPointer);
                }
            }
        }

        private void WalkNested(JsonObject schema, ImmutableArray<GatewayEditorOccurrenceStep> path,
            string owner, string ownerPointer)
        {
            if (schema["$ref"]?.GetValue<string>() is { } reference)
            {
                string name = LocalName(reference);
                WalkObject(RequireSchema(name),
                    [.. path, new(GatewayEditorOccurrenceStepKind.Reference, reference, null)], name, string.Empty);
                return;
            }
            string? matched = MatchComponent(schema, path);
            WalkObject(schema, path, matched ?? owner, matched is null ? ownerPointer : string.Empty);
        }

        private void WalkUnion(JsonObject schema, ImmutableArray<GatewayEditorOccurrenceStep> path)
        {
            JsonObject discriminator = schema["discriminator"]?.AsObject() ??
                throw new InvalidOperationException("Gateway editor union requires a discriminator.");
            string propertyName = discriminator["propertyName"]?.GetValue<string>() ??
                throw new InvalidOperationException("Gateway editor union discriminator is missing.");
            JsonObject mapping = discriminator["mapping"]?.AsObject() ??
                throw new InvalidOperationException("Gateway editor union mapping is missing.");
            foreach ((string discriminatorValue, JsonNode? referenceNode) in mapping.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                string reference = referenceNode?.GetValue<string>() ??
                    throw new InvalidOperationException("Gateway editor union reference is missing.");
                string name = LocalName(reference);
                ImmutableArray<GatewayEditorOccurrenceStep> branchPath =
                [
                    .. path,
                    new(GatewayEditorOccurrenceStepKind.UnionBranch, propertyName, discriminatorValue),
                    new(GatewayEditorOccurrenceStepKind.Reference, reference, null),
                ];
                WalkObject(RequireSchema(name), branchPath, name, string.Empty);
            }
        }

        private void Add(ImmutableArray<GatewayEditorOccurrenceStep> path, string owner,
            string pointer, JsonObject schema, bool required, GatewayEditorStructuralReason? forcedReason)
        {
            GatewayEditorStructuralReason reason = forcedReason ?? StructuralReason(schema);
            GatewayEditorFieldDisposition disposition = reason == GatewayEditorStructuralReason.None
                ? GatewayEditorFieldDisposition.Editable
                : GatewayEditorFieldDisposition.StructuralOnly;
            GatewayEditorDeclarationFamily family = Family(path);
            (GatewayEditorOmittedValueKind omittedKind, string? omittedJson) = Omitted(schema, required, path);
            (GatewayEditorInheritanceKind inheritance,
                ImmutableArray<GatewayEditorOccurrenceStep> inheritancePath) = Inheritance(path, family);
            (GatewayEditorCapabilityKind capability, ImmutableArray<string> capabilityPointers) =
                disposition == GatewayEditorFieldDisposition.Editable
                    ? Capability(path, family)
                    : (GatewayEditorCapabilityKind.None, []);
            string constraintPointer = path[^1].Kind == GatewayEditorOccurrenceStepKind.Items &&
                pointer.EndsWith("/items", StringComparison.Ordinal)
                    ? pointer[..^6]
                    : pointer;
            string key = Prefix + owner + constraintPointer;
            ImmutableArray<GatewayEditorConstraintTarget> constraints = _constraints.TryGetValue(key, out var found)
                ? found.Where(item => item.AppliesTo == (path[^1].Kind == GatewayEditorOccurrenceStepKind.Items
                    ? GatewayEditorConstraintAppliesTo.Items
                    : GatewayEditorConstraintAppliesTo.Value)).ToImmutableArray()
                : [];
            string helpCode = "gateway.editor." + Token(family) + "." +
                Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(PathKey(path))))[..12];
            Records.Add(new(
                new(path, Prefix + owner, pointer, constraints), disposition, Scope(path),
                omittedKind, omittedJson, inheritance, inheritancePath, family,
                new(capability, capabilityPointers), Presentation(family), helpCode,
                disposition == GatewayEditorFieldDisposition.Editable
                    ? QuickStep(path, family)
                    : GatewayEditorQuickRouteStep.None, reason));
        }

        private JsonObject RequireSchema(string name) => _schemas[name]?.AsObject() ??
            throw new InvalidOperationException("Gateway editor schema reference is unresolved: " + name);

        private string? MatchComponent(JsonObject schema, ImmutableArray<GatewayEditorOccurrenceStep> path)
        {
            string shape = Convert.ToBase64String(GatewayCanonicalJson.Serialize(schema));
            string key = PropertyKey(path);
            if (path[^1].Kind == GatewayEditorOccurrenceStepKind.Items &&
                (key.EndsWith(".annotations", StringComparison.Ordinal) ||
                 key.EndsWith(".labels", StringComparison.Ordinal) ||
                 key.EndsWith(".attributes", StringComparison.Ordinal)))
                return "HPD_Gateway_Abstractions_MetadataEntry";
            if (!_componentsByShape.TryGetValue(shape, out var candidates)) return null;
            if (candidates.Length == 1) return candidates[0];
            string? expected = Family(path) switch
            {
                GatewayEditorDeclarationFamily.Authorization => "HPD_Gateway_Abstractions_NamedAuthorizationPolicy",
                GatewayEditorDeclarationFamily.Cors => "HPD_Gateway_Abstractions_CorsPolicyBinding",
                GatewayEditorDeclarationFamily.TrafficAdmission => "HPD_Gateway_Abstractions_TrafficAdmissionBinding",
                GatewayEditorDeclarationFamily.OutputCache => "HPD_Gateway_Abstractions_OutputCacheBinding",
                GatewayEditorDeclarationFamily.RequestTimeout => "HPD_Gateway_Abstractions_RequestTimeoutBinding",
                GatewayEditorDeclarationFamily.Inspection => "HPD_Gateway_Abstractions_RequestInspectionBinding",
                GatewayEditorDeclarationFamily.RequestTransform => "HPD_Gateway_Abstractions_RequestHeaderTransform",
                GatewayEditorDeclarationFamily.ResponseTransform => "HPD_Gateway_Abstractions_ResponseHeaderTransform",
                GatewayEditorDeclarationFamily.Discovery when key.Contains("parameters", StringComparison.Ordinal) =>
                    "HPD_Gateway_Abstractions_ProviderParameter",
                _ when key.Contains("match.headers", StringComparison.Ordinal) => "HPD_Gateway_Abstractions_HttpHeaderMatch",
                _ when key.Contains("match.query", StringComparison.Ordinal) => "HPD_Gateway_Abstractions_HttpQueryMatch",
                _ => "HPD_Gateway_Abstractions_MetadataEntry",
            };
            return candidates.Contains(expected, StringComparer.Ordinal) ? expected : candidates[0];
        }
    }

    private static GatewayEditorStructuralReason StructuralReason(JsonObject schema)
    {
        if (schema["oneOf"] is JsonArray) return GatewayEditorStructuralReason.UnionBoundary;
        string type = Type(schema);
        if (type == "array") return GatewayEditorStructuralReason.Collection;
        if (type != "object" && schema["properties"] is not JsonObject) return GatewayEditorStructuralReason.None;
        JsonObject properties = schema["properties"]?.AsObject() ?? new JsonObject();
        return properties.Count == 1 && properties.ContainsKey("value")
            ? GatewayEditorStructuralReason.IdentityWrapper
            : GatewayEditorStructuralReason.Container;
    }

    private static bool IsScalar(JsonObject schema) => schema["$ref"] is null && schema["oneOf"] is null &&
        Type(schema) is not ("object" or "array") && schema["properties"] is null;

    private static string Type(JsonObject schema)
    {
        if (schema["type"] is JsonValue value) return value.GetValue<string>();
        if (schema["type"] is JsonArray array)
            return array.Select(static item => item?.GetValue<string>())
                .FirstOrDefault(static item => item != "null") ?? "null";
        return schema["properties"] is JsonObject ? "object" : string.Empty;
    }

    private static bool IsNullable(JsonObject schema) => schema["type"] is JsonArray array &&
        array.Any(static item => item?.GetValue<string>() == "null");

    private static (GatewayEditorOmittedValueKind, string?) Omitted(JsonObject schema, bool required,
        ImmutableArray<GatewayEditorOccurrenceStep> path)
    {
        if (required) return (GatewayEditorOmittedValueKind.Absent, null);
        string key = PropertyKey(path);
        string? known = key switch
        {
            "routes.enabled" => "true",
            "upstreams.transport.useProxy" => "true",
            "upstreams.request.version" => "\"Http2\"",
            "upstreams.request.versionSelection" => "\"RequestVersionOrLower\"",
            _ => null,
        };
        if (known is not null) return (GatewayEditorOmittedValueKind.CanonicalJson, known);
        if (schema["default"] is JsonNode defaultValue)
            return (GatewayEditorOmittedValueKind.CanonicalJson,
                System.Text.Encoding.UTF8.GetString(GatewayCanonicalJson.Serialize(defaultValue)));
        if (IsNullable(schema)) return (GatewayEditorOmittedValueKind.Absent, null);
        return Type(schema) switch
        {
            "array" => (GatewayEditorOmittedValueKind.CanonicalJson, "[]"),
            "object" => (GatewayEditorOmittedValueKind.CanonicalJson, "{}"),
            "boolean" => (GatewayEditorOmittedValueKind.CanonicalJson, "false"),
            _ => (GatewayEditorOmittedValueKind.Absent, null),
        };
    }

    private static GatewayEditorCompositionScope Scope(ImmutableArray<GatewayEditorOccurrenceStep> path)
    {
        string[] properties = Properties(path);
        if (properties[0] == "rootDefaults") return GatewayEditorCompositionScope.RootDefaults;
        if (properties[0] == "definitions") return GatewayEditorCompositionScope.Definition;
        if (properties[0] == "metadata") return GatewayEditorCompositionScope.Metadata;
        if (properties[0] == "routes")
        {
            if (properties.Contains("match", StringComparer.Ordinal)) return GatewayEditorCompositionScope.RouteMatch;
            if (properties.Any(static item => item is "requestTransforms" or "responseTransforms"))
                return GatewayEditorCompositionScope.Transform;
            return GatewayEditorCompositionScope.Route;
        }
        if (properties[0] == "upstreams")
        {
            if (properties.Contains("destinations", StringComparer.Ordinal)) return GatewayEditorCompositionScope.Destination;
            if (properties.Contains("endpoints", StringComparer.Ordinal)) return GatewayEditorCompositionScope.EndpointSource;
            return GatewayEditorCompositionScope.Upstream;
        }
        return GatewayEditorCompositionScope.Document;
    }

    private static GatewayEditorDeclarationFamily Family(ImmutableArray<GatewayEditorOccurrenceStep> path)
    {
        string key = string.Join('.', path.Select(static step => step.Kind == GatewayEditorOccurrenceStepKind.UnionBranch
            ? step.SecondaryValue
            : step.Value)).ToLowerInvariant();
        (string Text, GatewayEditorDeclarationFamily Family)[] rules =
        [
            ("authorization", GatewayEditorDeclarationFamily.Authorization),
            ("cors", GatewayEditorDeclarationFamily.Cors),
            ("trafficadmission", GatewayEditorDeclarationFamily.TrafficAdmission),
            ("requesttimeout", GatewayEditorDeclarationFamily.RequestTimeout),
            ("outputcache", GatewayEditorDeclarationFamily.OutputCache),
            ("telemetry", GatewayEditorDeclarationFamily.Telemetry),
            ("inspection", GatewayEditorDeclarationFamily.Inspection),
            ("credentialdisposition", GatewayEditorDeclarationFamily.CredentialDisposition),
            ("requesttransforms", GatewayEditorDeclarationFamily.RequestTransform),
            ("responsetransforms", GatewayEditorDeclarationFamily.ResponseTransform),
            ("discovery", GatewayEditorDeclarationFamily.Discovery),
            ("secret", GatewayEditorDeclarationFamily.Secret),
            ("certificate", GatewayEditorDeclarationFamily.Secret),
            ("tls", GatewayEditorDeclarationFamily.Tls),
            ("resilience", GatewayEditorDeclarationFamily.Resilience),
            ("active", GatewayEditorDeclarationFamily.ActiveHealth),
            ("passive", GatewayEditorDeclarationFamily.PassiveHealth),
            ("sessionaffinity", GatewayEditorDeclarationFamily.SessionAffinity),
            ("listener", GatewayEditorDeclarationFamily.Listener),
            ("transport", GatewayEditorDeclarationFamily.Transport),
            ("metadata", GatewayEditorDeclarationFamily.Metadata),
        ];
        foreach ((string text, GatewayEditorDeclarationFamily family) in rules)
            if (key.Contains(text, StringComparison.Ordinal)) return family;
        return GatewayEditorDeclarationFamily.Routing;
    }

    private static (GatewayEditorInheritanceKind, ImmutableArray<GatewayEditorOccurrenceStep>) Inheritance(
        ImmutableArray<GatewayEditorOccurrenceStep> path, GatewayEditorDeclarationFamily family)
    {
        string[] properties = Properties(path);
        if (properties.Length >= 3 && properties[0] == "routes" && properties.Contains("declarations", StringComparer.Ordinal) &&
            family is GatewayEditorDeclarationFamily.Authorization or GatewayEditorDeclarationFamily.Cors or
                GatewayEditorDeclarationFamily.TrafficAdmission or GatewayEditorDeclarationFamily.RequestTimeout or
                GatewayEditorDeclarationFamily.OutputCache or GatewayEditorDeclarationFamily.Telemetry or
                GatewayEditorDeclarationFamily.Inspection or GatewayEditorDeclarationFamily.CredentialDisposition &&
            string.Equals(properties[^1], FamilyProperty(family), StringComparison.Ordinal))
        {
            return (GatewayEditorInheritanceKind.RootInheritedAndRouteReplaced,
            [
                new(GatewayEditorOccurrenceStepKind.Property, "rootDefaults", null),
                new(GatewayEditorOccurrenceStepKind.Property, properties[^1], null),
            ]);
        }
        return (GatewayEditorInheritanceKind.None, []);
    }

    private static (GatewayEditorCapabilityKind, ImmutableArray<string>) Capability(
        ImmutableArray<GatewayEditorOccurrenceStep> path, GatewayEditorDeclarationFamily family)
    {
        string[] properties = Properties(path);
        string property = properties[^1];
        if (property == "policyName") return family switch
        {
            GatewayEditorDeclarationFamily.Authorization => (GatewayEditorCapabilityKind.AuthorizationPolicy, ["/policyName"]),
            GatewayEditorDeclarationFamily.Cors => (GatewayEditorCapabilityKind.CorsPolicy, ["/policyName"]),
            GatewayEditorDeclarationFamily.TrafficAdmission => (GatewayEditorCapabilityKind.TrafficAdmissionPolicy, ["/policyName"]),
            GatewayEditorDeclarationFamily.RequestTimeout => (GatewayEditorCapabilityKind.RequestTimeoutPolicy, ["/policyName"]),
            GatewayEditorDeclarationFamily.OutputCache => (GatewayEditorCapabilityKind.OutputCacheProfile, ["/policyName"]),
            _ => (GatewayEditorCapabilityKind.None, []),
        };
        if (property == "inspectorName") return (GatewayEditorCapabilityKind.RequestInspector, ["/inspectorName"]);
        if (family == GatewayEditorDeclarationFamily.Resilience && property is "profileName" or "profileVersion")
            return (GatewayEditorCapabilityKind.ResilienceProfile, ["/profileName", "/profileVersion"]);
        if (family == GatewayEditorDeclarationFamily.ActiveHealth && property == "policy")
            return (GatewayEditorCapabilityKind.ActiveHealthPolicy, ["/policy"]);
        if (family == GatewayEditorDeclarationFamily.PassiveHealth && property == "policy")
            return (GatewayEditorCapabilityKind.PassiveHealthPolicy, ["/policy"]);
        if (family == GatewayEditorDeclarationFamily.SessionAffinity && property == "policy")
            return (GatewayEditorCapabilityKind.SessionAffinityPolicy, ["/policy"]);
        if (family == GatewayEditorDeclarationFamily.SessionAffinity && property == "failurePolicy")
            return (GatewayEditorCapabilityKind.SessionAffinityFailurePolicy, ["/failurePolicy"]);
        if (family == GatewayEditorDeclarationFamily.Listener && property == "value")
            return (GatewayEditorCapabilityKind.Listener, ["/value"]);
        if (family == GatewayEditorDeclarationFamily.Discovery && property == "value" &&
            properties.Contains("provider", StringComparer.Ordinal))
            return (GatewayEditorCapabilityKind.DiscoveryProvider, ["/value"]);
        if (family == GatewayEditorDeclarationFamily.Secret && property == "value" &&
            properties.Contains("provider", StringComparer.Ordinal))
            return (GatewayEditorCapabilityKind.SecretProvider, ["/value"]);
        if (family == GatewayEditorDeclarationFamily.Inspection && property == "spillPolicy")
            return (GatewayEditorCapabilityKind.InspectionSpill, []);
        return family is GatewayEditorDeclarationFamily.Routing or GatewayEditorDeclarationFamily.Metadata
            ? (GatewayEditorCapabilityKind.None, [])
            : (GatewayEditorCapabilityKind.InstalledFamily, []);
    }

    private static GatewayEditorPresentationGroup Presentation(GatewayEditorDeclarationFamily family) => family switch
    {
        GatewayEditorDeclarationFamily.Authorization or GatewayEditorDeclarationFamily.Cors or
            GatewayEditorDeclarationFamily.CredentialDisposition or GatewayEditorDeclarationFamily.Inspection or
            GatewayEditorDeclarationFamily.Secret or GatewayEditorDeclarationFamily.Tls => GatewayEditorPresentationGroup.Security,
        GatewayEditorDeclarationFamily.Resilience or GatewayEditorDeclarationFamily.ActiveHealth or
            GatewayEditorDeclarationFamily.PassiveHealth or GatewayEditorDeclarationFamily.SessionAffinity or
            GatewayEditorDeclarationFamily.RequestTimeout => GatewayEditorPresentationGroup.Reliability,
        GatewayEditorDeclarationFamily.Transport => GatewayEditorPresentationGroup.Transport,
        GatewayEditorDeclarationFamily.Metadata => GatewayEditorPresentationGroup.Metadata,
        GatewayEditorDeclarationFamily.Routing or GatewayEditorDeclarationFamily.Listener or
            GatewayEditorDeclarationFamily.Discovery => GatewayEditorPresentationGroup.Endpoint,
        _ => GatewayEditorPresentationGroup.Policies,
    };

    private static GatewayEditorQuickRouteStep QuickStep(ImmutableArray<GatewayEditorOccurrenceStep> path,
        GatewayEditorDeclarationFamily family)
    {
        string[] properties = Properties(path);
        if (properties[0] == "routes" && properties.Contains("match", StringComparer.Ordinal))
            return GatewayEditorQuickRouteStep.RequestMatch;
        if (properties[0] == "routes" && properties[^1] == "upstream")
            return GatewayEditorQuickRouteStep.Upstream;
        if (properties[0] == "upstreams" && (properties.Contains("destinations", StringComparer.Ordinal) ||
            properties[^1] is "id" or "address")) return GatewayEditorQuickRouteStep.Destination;
        if (properties[0] == "routes" && properties.Contains("declarations", StringComparer.Ordinal) &&
            family != GatewayEditorDeclarationFamily.Routing) return GatewayEditorQuickRouteStep.OptionalPolicy;
        return GatewayEditorQuickRouteStep.None;
    }

    private static string[] Properties(ImmutableArray<GatewayEditorOccurrenceStep> path) => path
        .Where(static step => step.Kind == GatewayEditorOccurrenceStepKind.Property)
        .Select(static step => step.Value!).ToArray();
    private static string PropertyKey(ImmutableArray<GatewayEditorOccurrenceStep> path) => string.Join('.', Properties(path));
    private static string PathKey(ImmutableArray<GatewayEditorOccurrenceStep> path) => string.Join('/', path.Select(static step =>
        $"{(byte)step.Kind}:{step.Value}:{step.SecondaryValue}"));
    private static string FamilyProperty(GatewayEditorDeclarationFamily family) => family switch
    {
        GatewayEditorDeclarationFamily.TrafficAdmission => "trafficAdmission",
        GatewayEditorDeclarationFamily.RequestTimeout => "requestTimeout",
        GatewayEditorDeclarationFamily.OutputCache => "outputCache",
        GatewayEditorDeclarationFamily.CredentialDisposition => "credentialDisposition",
        _ => char.ToLowerInvariant(family.ToString()[0]) + family.ToString()[1..],
    };
    private static string Token(GatewayEditorDeclarationFamily family) =>
        string.Concat(family.ToString().Select((character, index) => char.IsUpper(character) && index > 0
            ? "-" + char.ToLowerInvariant(character)
            : char.ToLowerInvariant(character).ToString()));
    private static string LocalName(string reference) => reference.StartsWith(Prefix, StringComparison.Ordinal)
        ? reference[Prefix.Length..]
        : throw new InvalidOperationException("Gateway editor references must be local components.");
    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);

    private sealed class Comparer : IComparer<GatewayEditorFieldRecord>
    {
        internal static Comparer Instance { get; } = new();
        public int Compare(GatewayEditorFieldRecord? left, GatewayEditorFieldRecord? right)
        {
            ArgumentNullException.ThrowIfNull(left); ArgumentNullException.ThrowIfNull(right);
            ImmutableArray<GatewayEditorOccurrenceStep> x = left.Target.OccurrencePath;
            ImmutableArray<GatewayEditorOccurrenceStep> y = right.Target.OccurrencePath;
            for (int index = 0; index < Math.Min(x.Length, y.Length); index++)
            {
                int result = x[index].Kind.CompareTo(y[index].Kind);
                if (result == 0) result = string.CompareOrdinal(x[index].Value, y[index].Value);
                if (result == 0) result = string.CompareOrdinal(x[index].SecondaryValue, y[index].SecondaryValue);
                if (result != 0) return result;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
