using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.Agent;

/// <summary>One ownership boundary for direct Agent and workflow invocation context.</summary>
internal static class AgentContextSnapshot
{
    internal static AgentContextRunConfig? Capture(AgentContextRunConfig? source)
    {
        if (source is null) return null;
        var active = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return new AgentContextRunConfig
        {
            Properties = source.Properties?.ToDictionary(
                pair => pair.Key,
                pair => source.SharedProperties?.Contains(pair.Key) == true
                    ? pair.Value
                    : Copy(pair.Value, "Context.Properties[" + pair.Key + "]", active)!,
                source.Properties is Dictionary<string, object> properties ? properties.Comparer : StringComparer.Ordinal),
            ToolInstances = source.ToolInstances?.ToDictionary(
                pair => pair.Key,
                pair => source.SharedToolInstances?.Contains(pair.Key) == true
                    ? pair.Value
                    : Copy(pair.Value, "Context.ToolInstances[" + pair.Key + "]", active) as IToolMetadata
                        ?? throw Invalid("Context.ToolInstances[" + pair.Key + "]"),
                source.ToolInstances is Dictionary<string, IToolMetadata> tools ? tools.Comparer : StringComparer.Ordinal),
            SharedProperties = source.SharedProperties is null ? null : new HashSet<string>(source.SharedProperties, StringComparer.Ordinal),
            SharedToolInstances = source.SharedToolInstances is null ? null : new HashSet<string>(source.SharedToolInstances, StringComparer.Ordinal)
        };
    }

    private static object? Copy(object? value, string path, HashSet<object> active)
    {
        if (value is null or string or bool or char or byte or sbyte or short or ushort or int or uint
            or long or ulong or float or double or decimal or DateTime or DateTimeOffset or TimeSpan or Guid
            or DateOnly or TimeOnly or Uri or IAgentImmutableContextValue)
            return value;
        if (value.GetType().IsEnum) return value;
        if (value is JsonElement element) return element.Clone();
        if (value is JsonNode node) return node.DeepClone();
        if (active.Count >= 64)
            throw new AgentRunConfigurationException("ContextValueDepthExceeded", path, "Context data must not exceed 64 nested containers.");
        if (!active.Add(value))
            throw new AgentRunConfigurationException("ContextValueCycle", path, "Context data must not contain cycles.");
        try
        {
            switch (value)
            {
                case IAgentContextSnapshotValue custom:
                    object result;
                    try { result = custom.CaptureContextSnapshot(); }
                    catch (Exception)
                    {
                        // A host exception can contain secrets or evidence. Report only the member path.
                        throw new AgentRunConfigurationException("ContextSnapshotFailed", path, "The custom context snapshot failed.");
                    }
                    if (result is null || ReferenceEquals(result, value) || result.GetType() != value.GetType()
                        || result is IDisposable or IAsyncDisposable)
                        throw Invalid(path);
                    return result;
                case IDictionary<string, object> map:
                    return map.ToDictionary(pair => pair.Key,
                        pair => Copy(pair.Value, path + "[" + pair.Key + "]", active)!,
                        map is Dictionary<string, object> dictionary ? dictionary.Comparer : StringComparer.Ordinal);
                case IReadOnlyDictionary<string, object> map:
                    return map.ToDictionary(pair => pair.Key,
                        pair => Copy(pair.Value, path + "[" + pair.Key + "]", active)!, StringComparer.Ordinal);
                case Dictionary<string, string> strings:
                    return new Dictionary<string, string>(strings, strings.Comparer);
                case Array array when array.Rank == 1 && array.GetLowerBound(0) == 0:
                    var copy = (Array)array.Clone();
                    for (var index = 0; index < array.Length; index++)
                        copy.SetValue(Copy(array.GetValue(index), path + "[" + index + "]", active), index);
                    return copy;
                case List<object> list:
                    return list.Select((item, index) => Copy(item, path + "[" + index + "]", active)!).ToList();
                case List<string> strings:
                    return new List<string>(strings);
                default:
                    throw Invalid(path);
            }
        }
        finally { active.Remove(value); }
    }

    private static AgentRunConfigurationException Invalid(string path) => new(
        "UnsupportedContextOwnership", path,
        "Context values must be supported data, implement an explicit immutable/snapshot contract, or be declared host-shared. " +
        "Snapshot implementations must return a fresh value of the same type without acquiring disposable resources.");
}
