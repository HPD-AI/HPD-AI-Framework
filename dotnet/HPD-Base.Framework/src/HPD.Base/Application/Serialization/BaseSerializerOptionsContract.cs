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
        where TContext : JsonSerializerContext
    {
        ArgumentNullException.ThrowIfNull(factory);
        var generated = factory.Method.GetCustomAttributes(typeof(System.CodeDom.Compiler.GeneratedCodeAttribute), false)
            .OfType<System.CodeDom.Compiler.GeneratedCodeAttribute>()
            .SingleOrDefault(attribute => string.Equals(attribute.Tool, "HPD.Base.Generators", StringComparison.Ordinal));
        Type? capabilityType = factory.Method.DeclaringType;
        Type? ownerType = capabilityType?.DeclaringType;
        if (generated is null || !factory.Method.IsStatic || !factory.Method.IsAssembly || factory.Target is not null ||
            capabilityType is null || !capabilityType.IsNestedPrivate || !capabilityType.IsAbstract || !capabilityType.IsSealed ||
            !string.Equals(capabilityType.Name, "__HPDBaseSerializerFactory", StringComparison.Ordinal) || ownerType is null)
            throw new InvalidOperationException("base.schema.serializer.generatedReceiptInvalid");
        return new(typeof(TContext), ownerType, capabilityType, () => factory());
    }

    /// <summary>Computes a provisional name through the selected STJ naming-policy implementation.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static string ProvisionalWireName(JsonNamingPolicy? namingPolicy, string applicationName, string? explicitWireName) =>
        new string((explicitWireName ?? namingPolicy?.ConvertName(applicationName) ?? applicationName).AsSpan());
}

/// <summary>An opaque generated serializer-context registration.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class BaseSerializerContextRegistration
{
    private readonly Func<JsonSerializerContext> _factory;
    internal Type ContextType { get; }
    internal Type OwnerType { get; }
    private Type CapabilityType { get; }

    internal BaseSerializerContextRegistration(Type contextType, Type ownerType, Type capabilityType, Func<JsonSerializerContext> factory)
    {
        ContextType = contextType ?? throw new ArgumentNullException(nameof(contextType));
        OwnerType = ownerType ?? throw new ArgumentNullException(nameof(ownerType));
        CapabilityType = capabilityType ?? throw new ArgumentNullException(nameof(capabilityType));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    internal void AssertOwner(Type ownerType)
    {
        if (ownerType != OwnerType || CapabilityType.DeclaringType != ownerType)
            throw new InvalidOperationException("base.schema.serializer.generatedReceiptInvalid");
    }

    internal JsonSerializerContext CreateOwned()
    {
        JsonSerializerContext context = _factory();
        if (context.GetType() != ContextType) throw new InvalidOperationException("base.schema.serializer.contextContractAmbiguous");
        BaseSerializerOptionsContract.Validate(context.Options);
        return context;
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
