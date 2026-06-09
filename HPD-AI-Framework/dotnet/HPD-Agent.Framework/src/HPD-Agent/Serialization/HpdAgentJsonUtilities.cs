using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Serialization;

/// <summary>Canonical JSON options for HPD-Agent runtime and config types.</summary>
public static class HpdAgentJsonUtilities
{
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions(makeReadOnly: true);

    public static JsonSerializerOptions CreateDefaultOptions(bool makeReadOnly = false)
    {
        var options = new JsonSerializerOptions(HPDJsonContext.Default.Options)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        options.TypeInfoResolverChain.Clear();
        options.TypeInfoResolverChain.Add(HPDJsonContext.Default);

        foreach (var resolver in AIJsonUtilities.DefaultOptions.TypeInfoResolverChain)
        {
            if (resolver is not null)
                options.TypeInfoResolverChain.Add(resolver);
        }

        if (RuntimeFeature.IsDynamicCodeSupported)
            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());

        if (makeReadOnly)
            options.MakeReadOnly();

        return options;
    }
}
