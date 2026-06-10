using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HPD.Graph.Abstractions.Serialization;

namespace HPD.RAG.Core.Serialization;

/// <summary>Canonical JSON options for MRAG config and runtime DTOs.</summary>
public static class MragJsonUtilities
{
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions(makeReadOnly: true);

    public static JsonSerializerOptions CreateDefaultOptions(bool makeReadOnly = false)
    {
        var options = new JsonSerializerOptions(GraphJsonUtilities.DefaultOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        options.TypeInfoResolverChain.Insert(0, MragJsonSerializerContext.Shared);

        if (RuntimeFeature.IsDynamicCodeSupported)
            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());

        if (makeReadOnly)
            options.MakeReadOnly();

        return options;
    }
}
