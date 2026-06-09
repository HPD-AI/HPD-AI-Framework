using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent.Serialization;

namespace HPD.MultiAgent.Serialization;

/// <summary>Canonical JSON options for HPD multi-agent workflow config and runtime exchange types.</summary>
public static class MultiAgentJsonUtilities
{
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions(makeReadOnly: true);

    public static JsonSerializerOptions CreateDefaultOptions(bool makeReadOnly = false)
    {
        var options = new JsonSerializerOptions(HpdAgentJsonUtilities.DefaultOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        options.TypeInfoResolverChain.Insert(0, MultiAgentGraphConfigJsonContext.Default);

        if (RuntimeFeature.IsDynamicCodeSupported)
            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());

        if (makeReadOnly)
            options.MakeReadOnly();

        return options;
    }
}
