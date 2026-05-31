using System.Reflection;
using System.Text.Json.Serialization;
using HPD.Agent.Providers;

internal static class HpdosProviderOptionSchema
{
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "apiKey",
        "accessKeyId",
        "secretAccessKey",
        "sessionToken"
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> OptionsByKey =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["responseFormat"] = ["text", "json_object", "json_schema"],
            ["toolChoice"] = ["auto", "none", "required", "any", "tool"],
            ["reasoningEffortLevel"] = ["minimal", "low", "medium", "high"],
            ["serviceTier"] = ["auto", "standard_only"],
            ["extraParametersMode"] = ["pass-through", "error", "drop"],
            ["thinkingLevel"] = ["THINKING_LEVEL_UNSPECIFIED", "LOW", "HIGH"],
            ["mediaResolution"] = ["MEDIA_RESOLUTION_UNSPECIFIED", "MEDIA_RESOLUTION_LOW", "MEDIA_RESOLUTION_MEDIUM", "MEDIA_RESOLUTION_HIGH"],
            ["imageSize"] = ["1K", "2K", "4K"],
            ["imageOutputMimeType"] = ["image/png", "image/jpeg"],
            ["guardrailTrace"] = ["enabled", "disabled"],
            ["responseMimeType"] = ["text/plain", "application/json", "text/x.enum"],
            ["responseModalities"] = ["TEXT", "IMAGE", "AUDIO"],
            ["audioVoice"] = ["alloy", "ash", "ballad", "coral", "echo", "sage", "shimmer", "verse"],
            ["audioOutputFormat"] = ["wav", "mp3", "flac", "opus", "pcm16"],
            ["audioInputFormat"] = ["wav", "mp3"],
            ["verbosity"] = ["low", "medium", "high"]
        };

    public static IReadOnlyList<HpdosProviderConfigField> ForProvider(string providerKey)
    {
        var registration = ProviderDiscovery.GetProviderConfigType(providerKey);
        if (registration is null)
            return [];

        return registration.ConfigType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(ToField)
            .Where(field => field is not null)
            .Select(field => field!)
            .OrderBy(field => field.Kind == "json" ? 1 : 0)
            .ThenBy(field => field.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HpdosProviderConfigField? ToField(PropertyInfo property)
    {
        var key = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? ToCamelCase(property.Name);
        if (SensitiveNames.Contains(key))
            return null;

        var kind = FieldKind(property.PropertyType, key);
        return new HpdosProviderConfigField(
            Key: key,
            Label: Humanize(property.Name),
            Kind: kind,
            Required: false,
            Description: null,
            Options: OptionsByKey.TryGetValue(key, out var options) ? options : null);
    }

    private static string FieldKind(Type propertyType, string key)
    {
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (type == typeof(bool))
            return "boolean";
        if (type == typeof(int)
            || type == typeof(long)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal))
            return "number";
        if (IsStringCollection(type) && OptionsByKey.ContainsKey(key))
            return "multiSelect";
        if (type != typeof(string) && type != typeof(bool))
            return "json";
        if (OptionsByKey.ContainsKey(key))
            return "select";
        if (type == typeof(string) && key.Contains("url", StringComparison.OrdinalIgnoreCase))
            return "url";
        if (type == typeof(string) && (key.Contains("json", StringComparison.OrdinalIgnoreCase)
            || key.Contains("schema", StringComparison.OrdinalIgnoreCase)))
            return "json";
        if (type == typeof(string))
            return "text";

        return "json";
    }

    private static bool IsStringCollection(Type type)
    {
        if (type == typeof(string))
            return false;
        if (type == typeof(string[]))
            return true;
        if (!type.IsGenericType)
            return false;

        var genericDefinition = type.GetGenericTypeDefinition();
        return (genericDefinition == typeof(List<>)
                || genericDefinition == typeof(IReadOnlyList<>)
                || genericDefinition == typeof(IEnumerable<>))
            && type.GetGenericArguments()[0] == typeof(string);
    }

    private static string Humanize(string name)
    {
        var chars = new List<char>(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            if (i > 0 && char.IsUpper(current) && !char.IsUpper(name[i - 1]))
                chars.Add(' ');
            chars.Add(current);
        }

        return new string(chars.ToArray());
    }

    private static string ToCamelCase(string name)
        => string.IsNullOrEmpty(name)
            ? name
            : char.ToLowerInvariant(name[0]) + name[1..];
}
