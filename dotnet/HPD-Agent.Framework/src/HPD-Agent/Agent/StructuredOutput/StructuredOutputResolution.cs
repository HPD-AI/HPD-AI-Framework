using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace HPD.Agent.StructuredOutput;

/// <summary>Resolves explicit output identity before applying defaults, for direct and workflow runs.</summary>
internal static class StructuredOutputResolution
{
    internal static StructuredOutputOptions Resolve(
        StructuredOutputOptions? requested, Type type, string? nodeMode = null, Type[]? unionTypes = null)
    {
        var result = AgentRunConfigSnapshot.CloneStructuredOutput(requested) ?? new StructuredOutputOptions();
        var mode = result.Mode?.Trim().ToLowerInvariant();
        if (mode is not null and not "native" and not "tool")
            throw Conflict("mode");
        if (nodeMode is not null && mode is not null && mode != nodeMode)
            throw Conflict("mode");
        result.Mode = nodeMode ?? mode ?? "native";
        if (nodeMode is not null && result.SchemaName is not null && result.SchemaName != type.Name)
            throw Conflict("schemaName");
        if (unionTypes is not null && result.UnionTypes is not null && !result.UnionTypes.SequenceEqual(unionTypes))
            throw Conflict("unionTypes");
        if (nodeMode is not null && unionTypes is null && result.UnionTypes is { Length: > 0 })
            throw Conflict("unionTypes");
        result.UnionTypes = unionTypes?.ToArray() ?? result.UnionTypes;
        result.SchemaName ??= type.Name;
        if (result.PartialDebounceMs < 0) throw Conflict("partialDebounceMs");
        if (result.Schema is not null)
        {
            // Duplicate union schemas are deliberately unsupported: equality of a name is insufficient.
            if (result.UnionTypes is { Length: > 0 }) throw Conflict("schema");
            var generated = AIJsonUtilities.CreateJsonSchema(type,
                serializerOptions: result.SerializerOptions ?? AIJsonUtilities.DefaultOptions);
            if (!JsonNode.DeepEquals(JsonNode.Parse(result.Schema.Value.GetRawText()), JsonNode.Parse(generated.GetRawText())))
                throw Conflict("schema");
        }
        return result;
    }

    private static AgentRunConfigurationException Conflict(string member) => new(
        "StructuredOutputConflict", "StructuredOutput." + member,
        "The structured output declaration conflicts with the effective output identity or is invalid.");
}
