using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

/// <summary>Infrastructure entry points used only by BASE-generated code.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class BaseSerializerGeneratedContract
{
    /// <summary>Creates the exact locked serializer options for generated metadata.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static JsonSerializerOptions CreateOptions(JsonNamingPolicy? namingPolicy) => BaseSerializerOptionsContract.Create(namingPolicy);

    /// <summary>Creates an opaque factory registration for one generated context.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseSerializerContextRegistration RegisterContext<TContext>(Func<TContext> factory)
        where TContext : JsonSerializerContext => new(() => factory());

    /// <summary>Returns one verified wire name without exposing serializer metadata.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static string WireName(BaseSerializerContextRegistration registration, Type declaringType, string applicationName, string? explicitWireName) =>
        registration.WireName(declaringType, applicationName, explicitWireName);
}

/// <summary>An opaque generated serializer-context registration.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class BaseSerializerContextRegistration
{
    private readonly Func<JsonSerializerContext> _factory;

    internal BaseSerializerContextRegistration(Func<JsonSerializerContext> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    internal JsonSerializerContext CreateOwned()
    {
        JsonSerializerContext context = _factory();
        BaseSerializerOptionsContract.Validate(context.Options);
        return context;
    }

    internal string WireName(Type declaringType, string applicationName, string? explicitWireName)
    {
        using BaseSerializerContextLease lease = Open();
        JsonTypeInfo info = lease.Context.GetTypeInfo(declaringType) ?? throw new InvalidOperationException("base.schema.serializer.metadataInvalid");
        string expected = explicitWireName ?? info.Options.PropertyNamingPolicy?.ConvertName(applicationName) ?? applicationName;
        JsonPropertyInfo property = info.Properties.SingleOrDefault(candidate => string.Equals(candidate.Name, expected, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("base.schema.serializer.metadataInvalid");
        return new string(property.Name.AsSpan());
    }

    internal BaseSerializerContextLease Open() => new(CreateOwned());
}

internal sealed class BaseSerializerContextLease(JsonSerializerContext context) : IDisposable
{
    internal JsonSerializerContext Context { get; } = context;
    public void Dispose() { }
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
