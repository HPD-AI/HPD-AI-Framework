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

    internal static string Checksum(JsonTypeInfo root, IEnumerable<(string Id, string ApplicationName, string WireName)> bindings)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.Options.MakeReadOnly();
        var text = new StringBuilder("hpd.base.serializer.v2\n");
        var nodes = new Dictionary<Type, int>();
        int propertyCount = 0;
        Append(root, 0, nodes, ref propertyCount, text);
        JsonPropertyInfo[] rootProperties = root.Kind == JsonTypeInfoKind.Object
            ? root.Properties.OrderBy(static property => property.Name, StringComparer.Ordinal).ToArray()
            : [];
        foreach ((string id, string applicationName, string wireName) in bindings.OrderBy(static binding => binding.Id, StringComparer.Ordinal))
        {
            int ordinal = Array.FindIndex(rootProperties, property => string.Equals(property.Name, wireName, StringComparison.Ordinal));
            if (ordinal < 0 && root.Type != typeof(JsonElement))
                throw Invalid();
            text.Append("binding\n").Append(id).Append('\n').Append(ordinal).Append('\n')
                .Append(applicationName).Append('\n').Append(wireName).Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void Append(JsonTypeInfo info, int depth, Dictionary<Type, int> nodes, ref int propertyCount, StringBuilder text)
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
        text.Append("node:").Append(id).Append(':').Append(CanonicalType(info.Type)).Append(':').Append((int)info.Kind).Append('\n');

        Type type = Nullable.GetUnderlyingType(info.Type) ?? info.Type;
        if (Forbidden(type)) throw Invalid();
        if (Scalar(type)) return;
        if (info.Kind == JsonTypeInfoKind.Enumerable)
        {
            Type? element = type.IsArray ? type.GetElementType() : type.GetGenericArguments().SingleOrDefault();
            if (element is null || type.IsArray && type.GetArrayRank() != 1) throw Invalid();
            Append(info.Options.GetTypeInfo(element), depth + 1, nodes, ref propertyCount, text);
            return;
        }
        if (info.Kind != JsonTypeInfoKind.Object || info.Properties.Count > 256) throw Invalid();
        foreach (JsonPropertyInfo property in info.Properties.OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            if (++propertyCount > MaximumProperties || property.IsExtensionData || property.Get is null || property.Order != 0 || property.ShouldSerialize is not null)
                throw Invalid();
            text.Append("property:").Append(property.Name).Append(':').Append(CanonicalType(property.PropertyType)).Append(':')
                .Append(property.IsRequired ? '1' : '0').Append(':').Append(property.Set is not null ? '1' : '0').Append(':')
                .Append(property.CustomConverter?.GetType().FullName ?? string.Empty).Append('\n');
            Append(info.Options.GetTypeInfo(property.PropertyType), depth + 1, nodes, ref propertyCount, text);
        }
    }

    private static bool Forbidden(Type type) => type == typeof(object) || type == typeof(JsonDocument) ||
        type == typeof(JsonNode) || typeof(System.Collections.IDictionary).IsAssignableFrom(type) ||
        type.IsPointer || typeof(Delegate).IsAssignableFrom(type);

    private static bool Scalar(Type type) => type.IsEnum || type == typeof(string) || type == typeof(bool) ||
        type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) ||
        type == typeof(float) || type == typeof(double) || type == typeof(decimal) || type == typeof(Guid) ||
        type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(JsonElement) ||
        type == typeof(BaseBinary) || type == typeof(BaseVector) || type == typeof(RecordId) ||
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BaseRecordId<>);

    private static string CanonicalType(Type type) => type.FullName ?? type.Name;
    private static InvalidOperationException Invalid() => new("base.schema.serializer.metadataInvalid");
}
