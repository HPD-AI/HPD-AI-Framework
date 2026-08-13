using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

internal static class BaseSerializerContract
{
    private const int MaximumTypes = 256;
    private const int MaximumProperties = 4_096;
    private const int MaximumDepth = 32;

    internal static string Checksum(
        JsonTypeInfo root,
        IEnumerable<(string Id, string ApplicationName, string WireName)> bindings,
        IReadOnlyList<BaseSerializerPropertyDeclaration>? declarations = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Type != typeof(JsonElement)) BaseSerializerOptionsContract.Validate(root.Options);
        root.Options.MakeReadOnly();
        var text = new StringBuilder("hpd.base.serializer.v2\n");
        text.Append(root.Type == typeof(JsonElement) ? "loose-json-document-v1" : BaseSerializerOptionsContract.Receipt(root.Options)).Append('\n');
        var bindingInput = bindings.ToArray();
        bool generatedDeclarations = declarations is not null;
        if (declarations is null && root.Kind == JsonTypeInfoKind.Object && root.Type != typeof(JsonElement))
        {
            declarations = root.Properties.Select(property =>
            {
                var binding = bindingInput.SingleOrDefault(candidate => string.Equals(candidate.WireName, property.Name, StringComparison.Ordinal));
                return new BaseSerializerPropertyDeclaration
                {
                    DeclaringType = root.Type,
                    ApplicationName = binding.ApplicationName ?? property.Name,
                    PropertyType = property.PropertyType,
                    ExplicitWireName = property.Name,
                    Required = property.IsRequired,
                    Nullable = property.IsGetNullable,
                    Ignored = property.Get is null && property.Set is null,
                    ExplicitNever = false,
                };
            }).ToArray();
        }
        var nodes = new Dictionary<Type, int>();
        int propertyCount = 0;
        Append(root, root.Type, 0, 0, nodes, ref propertyCount, text, declarations);
        var bindingArray = bindingInput.OrderBy(static binding => binding.ApplicationName, StringComparer.Ordinal)
            .ThenBy(static binding => binding.WireName, StringComparer.Ordinal).ToArray();
        JsonPropertyInfo[] rootProperties = root.Kind == JsonTypeInfoKind.Object
            ? bindingArray.Select(binding => root.Properties.Single(property =>
                string.Equals(property.Name, binding.WireName, StringComparison.Ordinal))).ToArray()
            : [];
        if (generatedDeclarations && root.Type != typeof(JsonElement) &&
            rootProperties.Length != declarations!.Count(item => item.DeclaringType == root.Type && !item.Ignored))
            throw Invalid();
        foreach ((string id, string applicationName, string wireName) in bindingInput.OrderBy(static binding => binding.Id, StringComparer.Ordinal))
        {
            int ordinal = Array.FindIndex(rootProperties, property => string.Equals(property.Name, wireName, StringComparison.Ordinal));
            if (ordinal < 0 && root.Type != typeof(JsonElement))
                throw Invalid();
            JsonPropertyInfo? property = ordinal < 0 ? null : rootProperties[ordinal];
            BaseSerializerPropertyDeclaration? declaration = declarations?.SingleOrDefault(item =>
                item.DeclaringType == root.Type && string.Equals(item.ApplicationName, applicationName, StringComparison.Ordinal));
            text.Append("binding\n").Append(id).Append('\n').Append(ordinal).Append('\n')
                .Append(applicationName).Append('\n').Append(wireName).Append('\n')
                .Append(property is null ? CanonicalType(root.Type) : CanonicalType(property.PropertyType)).Append('\n')
                .Append(property is null ? nodes[root.Type] : nodes[property.PropertyType]).Append('\n');
            if (property is not null)
            {
                if (declaration is null || declaration.PropertyType != property.PropertyType) throw Invalid();
                text.Append("crosscheck:").Append(applicationName).Append(':').Append(property.Name).Append(':')
                    .Append(CanonicalType(property.PropertyType)).Append(':').Append(nodes[property.PropertyType]).Append('\n');
            }
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    internal static string GraphFingerprint(
        JsonTypeInfo root,
        IReadOnlyList<BaseSerializerPropertyDeclaration>? declarations = null)
    {
        BaseSerializerOptionsContract.Validate(root.Options);
        root.Options.MakeReadOnly();
        var text = new StringBuilder("hpd.base.serializer.graph.v1\n");
        text.Append(BaseSerializerOptionsContract.Receipt(root.Options)).Append('\n');
        var nodes = new Dictionary<Type, int>();
        int propertyCount = 0;
        Append(root, root.Type, 0, 0, nodes, ref propertyCount, text, declarations);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    internal static IEnumerable<JsonTypeInfo> Reachable(JsonTypeInfo root)
    {
        var pending = new Queue<JsonTypeInfo>();
        var seen = new HashSet<Type>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out JsonTypeInfo? current))
        {
            if (!seen.Add(current.Type)) continue;
            yield return current;
            Type type = Nullable.GetUnderlyingType(current.Type) ?? current.Type;
            if (current.Kind == JsonTypeInfoKind.Enumerable)
            {
                Type? element = type.IsArray ? type.GetElementType() : type.GetGenericArguments().SingleOrDefault();
                if (element is not null) pending.Enqueue(current.Options.GetTypeInfo(element));
            }
            else if (current.Kind == JsonTypeInfoKind.Object)
            {
                foreach (JsonPropertyInfo property in current.Properties) pending.Enqueue(current.Options.GetTypeInfo(property.PropertyType));
            }
        }
    }

    private static void Append(JsonTypeInfo info, Type rootType, int depth, int wrappers, Dictionary<Type, int> nodes, ref int propertyCount, StringBuilder text, IReadOnlyList<BaseSerializerPropertyDeclaration>? declarations)
    {
        if (depth > MaximumDepth || nodes.Count >= MaximumTypes)
            throw new InvalidOperationException("base.schema.serializer.graphLimitExceeded");
        if (nodes.TryGetValue(info.Type, out int existing))
        {
            text.Append("ref:").Append(existing).Append('\n');
            return;
        }
        int id = nodes.Count;
        nodes.Add(info.Type, id);
        info.MakeReadOnly();
        string canonicalType = CanonicalType(info.Type);
        if (Encoding.UTF8.GetByteCount(canonicalType) > 512) throw new InvalidOperationException("base.schema.serializer.graphLimitExceeded");
        text.Append("node:").Append(id).Append(':').Append(canonicalType).Append(':').Append((int)info.Kind).Append('\n');

        Type type = Nullable.GetUnderlyingType(info.Type) ?? info.Type;
        if (Forbidden(type) || type == typeof(JsonElement) && type != rootType) throw Invalid();
        if (Scalar(type)) return;
        if (info.Kind == JsonTypeInfoKind.Enumerable)
        {
            if (wrappers >= 16) throw Invalid();
            Type? element = type.IsArray ? type.GetElementType() : type.GetGenericArguments().SingleOrDefault();
            if (element is null || type.IsArray && type.GetArrayRank() != 1) throw Invalid();
            Append(info.Options.GetTypeInfo(element), rootType, depth + 1, wrappers + 1, nodes, ref propertyCount, text, declarations);
            return;
        }
        if (info.Kind != JsonTypeInfoKind.Object || info.Properties.Count > 256) throw Invalid();
        BaseSerializerPropertyDeclaration[]? declared = declarations?.Where(item => item.DeclaringType == info.Type)
            .OrderBy(static item => item.ApplicationName, StringComparer.Ordinal)
            .ThenBy(static item => CanonicalType(item.PropertyType), StringComparer.Ordinal).ToArray();
        if (declared?.Length == 0) declared = null;
        if (declared is not null && declared.Length != info.Properties.Count) throw Invalid();
        IEnumerable<(JsonPropertyInfo Property, BaseSerializerPropertyDeclaration? Declaration)> properties = declared is null
            ? info.Properties.OrderBy(static property => property.Name, StringComparer.Ordinal).Select(static property => (property, (BaseSerializerPropertyDeclaration?)null))
            : declared.Select(declaration =>
            {
                string expected = declaration.ExplicitWireName ?? info.Options.PropertyNamingPolicy?.ConvertName(declaration.ApplicationName) ?? declaration.ApplicationName;
                JsonPropertyInfo property = info.Properties.Single(candidate => string.Equals(candidate.Name, expected, StringComparison.Ordinal));
                return (property, (BaseSerializerPropertyDeclaration?)declaration);
            });
        foreach ((JsonPropertyInfo property, BaseSerializerPropertyDeclaration? declaration) in properties)
        {
            if (++propertyCount > MaximumProperties || property.IsExtensionData || property.Order != 0)
                throw Invalid();
            if (declaration?.Ignored == true)
            {
                if (property.PropertyType != declaration.PropertyType ||
                    property.Get is not null && property.ShouldSerialize is null)
                    throw Invalid();
                text.Append("ignored:").Append(declaration.ApplicationName).Append(':').Append(property.Name).Append(':')
                    .Append(CanonicalType(property.PropertyType)).Append('\n');
                continue;
            }
            if (property.Get is null || property.ShouldSerialize is not null && declaration?.ExplicitNever != true) throw Invalid();
            if (declaration is not null && (property.PropertyType != declaration.PropertyType || property.IsRequired != declaration.Required)) throw Invalid();
            if (declaration is null && property.CustomConverter is not null || declaration is not null &&
                ((declaration.ConverterType is null) != (property.CustomConverter is null) ||
                 declaration.ConverterType is not null && property.CustomConverter?.GetType() != declaration.ConverterType)) throw Invalid();
            text.Append("property:").Append(declaration?.ApplicationName ?? property.Name).Append(':').Append(property.Name).Append(':').Append(CanonicalType(property.PropertyType)).Append(':')
                .Append(property.IsRequired ? '1' : '0').Append(':').Append(property.Set is not null ? '1' : '0').Append(':')
                .Append((declaration?.Nullable ?? property.IsGetNullable) ? '1' : '0').Append(':').Append(declaration?.ConverterIdentity ?? "stj-built-in").Append('\n');
            Append(info.Options.GetTypeInfo(property.PropertyType), rootType, depth + 1, 0, nodes, ref propertyCount, text, declarations);
        }
    }

    private static bool Forbidden(Type type) => type == typeof(object) || type == typeof(JsonDocument) ||
        type == typeof(JsonNode) || typeof(System.Collections.IDictionary).IsAssignableFrom(type) ||
        type.IsGenericType && type.GetGenericTypeDefinition() is var definition &&
            (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)) ||
        type.IsPointer || typeof(Delegate).IsAssignableFrom(type);

    private static bool Scalar(Type type) => type.IsEnum || type == typeof(string) || type == typeof(bool) ||
        type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) ||
        type == typeof(float) || type == typeof(double) || type == typeof(decimal) || type == typeof(Guid) ||
        type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(JsonElement) ||
        type == typeof(BaseBinary) || type == typeof(BaseVector) || type == typeof(RecordId) ||
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BaseRecordId<>);

    private static string CanonicalType(Type type)
    {
        if (type.IsArray) return CanonicalType(type.GetElementType()!) + "[]";
        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return CanonicalType(nullable) + "?";
        if (!type.IsGenericType) return type.FullName ?? type.Name;
        Type definition = type.GetGenericTypeDefinition();
        return (definition.FullName ?? definition.Name) + "[" +
            string.Join(",", type.GetGenericArguments().Select(CanonicalType)) + "]";
    }
    private static InvalidOperationException Invalid() => new("base.schema.serializer.metadataInvalid");
}
