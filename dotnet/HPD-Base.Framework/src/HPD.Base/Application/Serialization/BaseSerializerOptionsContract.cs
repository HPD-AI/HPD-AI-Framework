using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Infrastructure entry points used only by BASE-generated code.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class BaseSerializerGeneratedContract
{
    /// <summary>Creates the exact locked serializer options for generated metadata.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static JsonSerializerOptions CreateOptions(JsonNamingPolicy? namingPolicy) => BaseSerializerOptionsContract.Create(namingPolicy);

    /// <summary>Gets the one privately owned generated context for its exact context type.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static TContext GetContext<TContext>(Func<TContext> factory)
        where TContext : JsonSerializerContext => BaseGeneratedContextOwner<TContext>.Get(factory);
}

internal static class BaseGeneratedContextOwner<TContext> where TContext : JsonSerializerContext
{
    private static readonly object Gate = new();
    private static TContext? _context;

    internal static TContext Get(Func<TContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (Gate)
        {
            if (_context is not null) return _context;
            TContext context = factory();
            BaseSerializerOptionsContract.Validate(context.Options);
            _context = context;
            return context;
        }
    }
}

internal static class BaseSerializerOptionsContract
{
    internal static JsonSerializerOptions Create(JsonNamingPolicy? namingPolicy) => new()
    {
        PropertyNamingPolicy = namingPolicy,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        IgnoreReadOnlyProperties = false,
        IgnoreReadOnlyFields = false,
        IncludeFields = false,
        WriteIndented = false,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Replace,
        AllowDuplicateProperties = false,
        AllowOutOfOrderMetadataProperties = false,
        DefaultBufferSize = 16_384,
    };

    internal static void Validate(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.DictionaryKeyPolicy is not null || options.Converters.Count != 0 ||
            options.PropertyNameCaseInsensitive || options.NumberHandling != JsonNumberHandling.Strict ||
            options.UnmappedMemberHandling != JsonUnmappedMemberHandling.Disallow || options.ReferenceHandler is not null ||
            options.MaxDepth != 64 || options.Encoder is not null || options.DefaultIgnoreCondition != JsonIgnoreCondition.Never ||
            options.IgnoreReadOnlyProperties || options.IgnoreReadOnlyFields || options.IncludeFields || options.WriteIndented ||
            !options.RespectNullableAnnotations || !options.RespectRequiredConstructorParameters || options.AllowTrailingCommas ||
            options.ReadCommentHandling != JsonCommentHandling.Disallow || options.PreferredObjectCreationHandling != JsonObjectCreationHandling.Replace ||
            options.AllowDuplicateProperties || options.AllowOutOfOrderMetadataProperties || options.DefaultBufferSize != 16_384)
            throw new InvalidOperationException("base.schema.serializer.optionsMismatch");
    }

    internal static string Receipt(JsonSerializerOptions options)
    {
        Validate(options);
        string naming = ReferenceEquals(options.PropertyNamingPolicy, JsonNamingPolicy.CamelCase) ? "camel" :
            ReferenceEquals(options.PropertyNamingPolicy, JsonNamingPolicy.SnakeCaseLower) ? "snake-lower" :
            ReferenceEquals(options.PropertyNamingPolicy, JsonNamingPolicy.SnakeCaseUpper) ? "snake-upper" :
            ReferenceEquals(options.PropertyNamingPolicy, JsonNamingPolicy.KebabCaseLower) ? "kebab-lower" :
            ReferenceEquals(options.PropertyNamingPolicy, JsonNamingPolicy.KebabCaseUpper) ? "kebab-upper" :
            options.PropertyNamingPolicy is null ? "none" : throw new InvalidOperationException("base.schema.serializer.optionsMismatch");
        return $"options-v1:{naming}:strict:disallow:64:never:replace:16384";
    }
}
