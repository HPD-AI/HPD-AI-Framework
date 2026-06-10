using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Graph.Abstractions.Serialization;

/// <summary>Canonical JSON options for HPD-Graph config, runtime state, and storage payloads.</summary>
public static class GraphJsonUtilities
{
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions(makeReadOnly: true);

    public static JsonSerializerOptions CreateDefaultOptions(bool makeReadOnly = false)
    {
        var options = new JsonSerializerOptions(GraphConfigJsonSerializerContext.Default.Options)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        options.TypeInfoResolverChain.Clear();
        options.TypeInfoResolverChain.Add(GraphConfigJsonSerializerContext.Default);
        options.TypeInfoResolverChain.Add(GraphJsonSerializerContext.Default);

        if (RuntimeFeature.IsDynamicCodeSupported)
            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());

        if (makeReadOnly)
            options.MakeReadOnly();

        return options;
    }
}
