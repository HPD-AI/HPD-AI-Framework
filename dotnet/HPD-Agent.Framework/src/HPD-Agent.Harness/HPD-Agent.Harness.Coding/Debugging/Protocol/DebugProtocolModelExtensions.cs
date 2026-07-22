using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

public sealed partial class LaunchRequestArguments
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdapterConfiguration { get; set; }
}

public sealed partial class AttachRequestArguments
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdapterConfiguration { get; set; }
}

internal static class DebugProtocolArgumentComposer
{
    private static readonly HashSet<string> LaunchReserved = new(StringComparer.Ordinal) { "noDebug", "__restart" };
    private static readonly HashSet<string> AttachReserved = new(StringComparer.Ordinal) { "__restart" };

    public static LaunchRequestArguments Launch(JsonElement configuration, bool noDebug, JsonElement? restart = null) => new()
    {
        NoDebug = noDebug,
        Restart = restart?.Clone(),
        AdapterConfiguration = Copy(configuration, LaunchReserved)
    };

    public static AttachRequestArguments Attach(JsonElement configuration, JsonElement? restart = null) => new()
    {
        Restart = restart?.Clone(),
        AdapterConfiguration = Copy(configuration, AttachReserved)
    };

    private static Dictionary<string, JsonElement> Copy(JsonElement configuration, HashSet<string> reserved)
    {
        if (configuration.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Adapter configuration must be a JSON object.");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in configuration.EnumerateObject())
        {
            if (reserved.Contains(property.Name))
                throw new InvalidOperationException($"Adapter configuration cannot override HPD-controlled field '{property.Name}'.");
            if (!result.TryAdd(property.Name, property.Value.Clone()))
                throw new InvalidOperationException($"Adapter configuration contains duplicate field '{property.Name}'.");
        }
        return result;
    }
}
