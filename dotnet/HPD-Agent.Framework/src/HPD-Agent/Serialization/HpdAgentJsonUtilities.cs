using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Serialization;

/// <summary>Canonical JSON options for HPD-Agent runtime and config types.</summary>
public static class HpdAgentJsonUtilities
{
    private static readonly object RegistrationGate = new();
    private static IJsonTypeInfoResolver[] _registeredTypeInfoResolvers = [];

    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions(makeReadOnly: true);

    /// <summary>
    /// Adds a source-generated JSON metadata resolver for integration-specific runtime types.
    /// </summary>
    /// <param name="resolver">The source-generated metadata resolver to register.</param>
    public static void RegisterTypeInfoResolver(IJsonTypeInfoResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        lock (RegistrationGate)
        {
            if (Array.IndexOf(_registeredTypeInfoResolvers, resolver) >= 0)
                return;
            var updated = new IJsonTypeInfoResolver[_registeredTypeInfoResolvers.Length + 1];
            Array.Copy(_registeredTypeInfoResolvers, updated, _registeredTypeInfoResolvers.Length);
            updated[^1] = resolver;
            Volatile.Write(ref _registeredTypeInfoResolvers, updated);
        }
    }

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
        options.TypeInfoResolverChain.Add(RegisteredTypeInfoResolver.Instance);

        foreach (var resolver in AIJsonUtilities.DefaultOptions.TypeInfoResolverChain)
        {
            if (resolver is not null)
                options.TypeInfoResolverChain.Add(resolver);
        }

        if (makeReadOnly)
            options.MakeReadOnly();

        return options;
    }

    private sealed class RegisteredTypeInfoResolver : IJsonTypeInfoResolver
    {
        internal static RegisteredTypeInfoResolver Instance { get; } = new();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            foreach (var resolver in Volatile.Read(ref _registeredTypeInfoResolvers))
            {
                if (resolver.GetTypeInfo(type, options) is { } typeInfo)
                    return typeInfo;
            }
            return null;
        }
    }
}
