using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

internal sealed class DefaultBaseJsonTypeInfoResolverComposer : IBaseJsonTypeInfoResolverComposer
{
    /// <summary>Executes the compose and freeze operation.</summary>
    public JsonSerializerOptions ComposeAndFreeze(
        IEnumerable<IBaseJsonTypeInfoContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);

        var registry = new JsonTypeInfoRegistry();
        foreach (var contributor in contributors)
        {
            contributor.AddTo(registry);
        }

        var options = new JsonSerializerOptions(HPDBaseJsonSerializerContext.Default.Options)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                [HPDBaseJsonSerializerContext.Default, HPDBaseRuntimeJsonSerializerContext.Default, .. registry.Resolvers])
        };
        options.Converters.Add(new LowerCamelJsonStringEnumConverter<BaseEventPublishFailureMode>());
        options.Converters.Add(new LowerCamelJsonStringEnumConverter<BaseQueryValidationUsage>());
        options.Converters.Add(new LowerCamelJsonStringEnumConverter<BaseRuntimeValidationFailureKind>());
        options.Converters.Add(new LowerCamelJsonStringEnumConverter<BaseRuntimeValidationSeverity>());

        options.MakeReadOnly();
        return options;
    }
}

internal sealed class JsonTypeInfoRegistry : IBaseJsonTypeInfoRegistry
{
    private readonly List<IJsonTypeInfoResolver> _resolvers = [];

    /// <summary>Gets the resolvers.</summary>
    public IJsonTypeInfoResolver[] Resolvers => _resolvers.ToArray();

    /// <summary>Executes the add resolver operation.</summary>
    public void AddResolver(string contributorId, IJsonTypeInfoResolver resolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contributorId);
        ArgumentNullException.ThrowIfNull(resolver);
        _resolvers.Add(resolver);
    }

    /// <summary>Executes the add type info operation.</summary>
    public void AddTypeInfo<T>(string contributorId, JsonTypeInfo<T> typeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contributorId);
        ArgumentNullException.ThrowIfNull(typeInfo);
        _resolvers.Add(new SingleTypeInfoResolver<T>(typeInfo));
    }
}

internal sealed class SingleTypeInfoResolver<T> : IJsonTypeInfoResolver
{
    private readonly JsonTypeInfo<T> _typeInfo;

    /// <summary>Initializes a new instance.</summary>
    public SingleTypeInfoResolver(JsonTypeInfo<T> typeInfo)
    {
        _typeInfo = typeInfo;
    }

    /// <summary>Executes the get type info operation.</summary>
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        _ = options;
        return type == typeof(T) ? _typeInfo : null;
    }
}
