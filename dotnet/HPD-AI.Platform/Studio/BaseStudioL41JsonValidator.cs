using System.Text;
using System.Text.Json;

namespace HPD.AI.Platform.Studio;

/// <summary>Validates Studio values against the exact bootstrap-disclosed L41 graph.</summary>
public static class BaseStudioL41JsonValidator
{
    /// <summary>Requires one JSON document to match the named graph node exactly.</summary>
    public static void Require(BaseStudioCanonicalJson value, string typeId,
        IReadOnlyCollection<BaseStudioNamedTypeContract> types)
    {
        ArgumentNullException.ThrowIfNull(value); BaseStudioNamedTypeContract.RequireL41Id(typeId);
        var map = types.ToDictionary(static item => item.TypeId, StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse(value.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        Validate(document.RootElement, Required(typeId), 1);

        BaseStudioNamedTypeContract Required(string id) => map.TryGetValue(id, out BaseStudioNamedTypeContract? type)
            ? type : throw new ArgumentException("The Studio value references an unavailable L41 node.", nameof(types));
        void Validate(JsonElement element, BaseStudioNamedTypeContract type, int depth)
        {
            if (depth > 32) Invalid(); using JsonDocument descriptor = JsonDocument.Parse(type.GetCanonicalDescriptor());
            JsonElement node = descriptor.RootElement; string kind = node.GetProperty("kind").GetString()!;
            switch (kind)
            {
                case "object":
                    if (element.ValueKind != JsonValueKind.Object) Invalid();
                    JsonElement[] properties = node.GetProperty("properties").EnumerateArray().ToArray();
                    string[] admitted = properties.Select(static property => property.GetProperty("wireName").GetString()!).ToArray();
                    string[] supplied = element.EnumerateObject().Select(static property => property.Name).ToArray();
                    if (supplied.Any(property => !admitted.Contains(property, StringComparer.Ordinal)) ||
                        !supplied.SequenceEqual(admitted.Where(name => supplied.Contains(name, StringComparer.Ordinal)))) Invalid();
                    foreach (JsonElement property in properties)
                    {
                        string wireName = property.GetProperty("wireName").GetString()!;
                        bool present = element.TryGetProperty(wireName, out JsonElement member);
                        if (!present && property.GetProperty("required").GetBoolean()) Invalid();
                        if (!present) continue;
                        if (member.ValueKind == JsonValueKind.Null)
                        { if (!property.GetProperty("nullable").GetBoolean()) Invalid(); continue; }
                        Validate(member, Required(property.GetProperty("typeId").GetString()!), depth + 1);
                    }
                    break;
                case "array":
                    if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < node.GetProperty("minItems").GetInt32() ||
                        element.GetArrayLength() > node.GetProperty("maxItems").GetInt32()) Invalid();
                    BaseStudioNamedTypeContract item = Required(node.GetProperty("elementTypeId").GetString()!);
                    foreach (JsonElement child in element.EnumerateArray()) Validate(child, item, depth + 1);
                    break;
                case "string":
                    if (element.ValueKind != JsonValueKind.String) Invalid(); string text = element.GetString()!;
                    int bytes = Encoding.UTF8.GetByteCount(text);
                    if (bytes < node.GetProperty("minLength").GetInt32() || bytes > node.GetProperty("maxLength").GetInt32()) Invalid();
                    break;
                case "boolean": if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Invalid(); break;
                case "enum":
                    if (element.ValueKind != JsonValueKind.String || !node.GetProperty("values").EnumerateArray()
                        .Any(itemValue => StringComparer.Ordinal.Equals(itemValue.GetString(), element.GetString()))) Invalid();
                    break;
                case "literal": if (!StringComparer.Ordinal.Equals(element.GetRawText(), node.GetProperty("value").GetRawText())) Invalid(); break;
                case "integer":
                    string wire = node.GetProperty("wire").GetString()!;
                    string integer = wire == "decimal-string" && element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
                    if ((wire == "number" && element.ValueKind != JsonValueKind.Number) ||
                        !System.Numerics.BigInteger.TryParse(integer, System.Globalization.NumberStyles.AllowLeadingSign,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) != integer ||
                        parsed < System.Numerics.BigInteger.Parse(node.GetProperty("minimum").GetString()!, System.Globalization.CultureInfo.InvariantCulture) ||
                        parsed > System.Numerics.BigInteger.Parse(node.GetProperty("maximum").GetString()!, System.Globalization.CultureInfo.InvariantCulture)) Invalid();
                    break;
                default: throw new NotSupportedException($"Studio value validation for L41 kind '{kind}' is not installed.");
            }
        }
        static void Invalid() => throw new ArgumentException("The Studio JSON value does not match its L41 contract.");
    }
}
