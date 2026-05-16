using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using System.Reflection;
using HPD.Agent.Middleware;

namespace HPD.Agent;

/// <summary>
/// A modern, unified AIFunctionFactory that prioritizes delegate-based invocation
/// for performance and AOT-compatibility.
/// </summary>
public class HPDAIFunctionFactory
{
    private static readonly HPDAIFunctionFactoryOptions _defaultOptions = new();
    private static readonly JsonSerializerOptions _defaultSerializerOptions = HPDToolArgumentBinder.DefaultSerializerOptions;

    public static AIFunction Create(
        Func<AIFunctionArguments, FunctionExecutionContext, CancellationToken, Task<object?>> invocation,
        HPDAIFunctionFactoryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return new HPDAIFunction(
            invocation,
            options ?? _defaultOptions);
    }


    /// <summary>
    /// Modern AIFunction implementation using delegate-based invocation with validation.
    /// </summary>
    public class HPDAIFunction : AIFunction
    {
        private readonly Func<AIFunctionArguments, FunctionExecutionContext, CancellationToken, Task<object?>> _invocationHandler;
        private readonly MethodInfo? _method;

        // Constructor for the modern, delegate-based approach
        public HPDAIFunction(
            Func<AIFunctionArguments, FunctionExecutionContext, CancellationToken, Task<object?>> invocationHandler,
            HPDAIFunctionFactoryOptions options)
        {
            _invocationHandler = invocationHandler ?? throw new ArgumentNullException(nameof(invocationHandler));
            _method = invocationHandler.Method; // For metadata
            HPDOptions = options;

            JsonSchema = options.SchemaProvider?.Invoke() ?? default;
            Name = options.Name ?? _method?.Name ?? "Unknown";
            Description = options.Description ?? "";
        }

        public HPDAIFunctionFactoryOptions HPDOptions { get; }
        public override string Name { get; }
        public override string Description { get; }
        public override JsonElement JsonSchema { get; }
        public override MethodInfo? UnderlyingMethod => _method;
        public override JsonSerializerOptions JsonSerializerOptions => HPDOptions.SerializerOptions ?? _defaultSerializerOptions;

        public ValueTask<object?> InvokeAsync(
            AIFunctionArguments arguments,
            FunctionExecutionContext functionContext,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(functionContext);
            return InvokeCoreAsync(arguments, functionContext, cancellationToken);
        }

        public override IReadOnlyDictionary<string, object?> AdditionalProperties
        {
            get
            {
                if (HPDOptions.AdditionalProperties == null)
                    return base.AdditionalProperties;

                // Return the dictionary as IReadOnlyDictionary
                return HPDOptions.AdditionalProperties;
            }
        }

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
            => throw new InvalidOperationException("HPD functions require FunctionExecutionContext. Invoke them through the agent runtime or call InvokeAsync(arguments, functionContext, cancellationToken).");

        private async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            FunctionExecutionContext functionContext,
            CancellationToken cancellationToken)
        {
            // 1. Robustly get the JSON arguments for validation.
            JsonElement jsonArgs;
            var existingJson = arguments.GetJson();
            if (existingJson.ValueKind != JsonValueKind.Undefined)
            {
                jsonArgs = existingJson;
            }
            else
            {
                // If no raw JSON is available, serialize the arguments dictionary.
                var argumentsDict = arguments
                    .Where(kvp => kvp.Key != AIFunctionArgumentsExtensions.JsonKey && kvp.Key != AIFunctionArgumentsExtensions.JsonSerializerOptionsKey)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                var jsonString = JsonSerializer.Serialize(argumentsDict, HPDJsonContext.Default.DictionaryStringObject);
                jsonArgs = JsonDocument.Parse(jsonString).RootElement;
            }

            var serializerOptions = JsonSerializerOptions;

            // 2. Use the validator.
            var validationErrors = HPDOptions.Validator?.Invoke(jsonArgs, serializerOptions);

            // TODO: Add container-specific parameter validation
            // Edge case: LLMs sometimes try to invoke containers with parameters like Math({function: "Add", a: 5, b: 10})
            // instead of first expanding the container with Math() then calling Add(5, 10).
            // Future work: Detect IsContainer metadata and reject any parameter invocations with helpful retry guidance.

            if (validationErrors != null && validationErrors.Count > 0)
            {
                // 3. Return structured error on failure.
                var errorResponse = new ValidationErrorResponse();
                foreach (var error in validationErrors)
                {
                    if (jsonArgs.TryGetProperty(error.Property, out var propertyNode))
                    {
                        error.AttemptedValue = propertyNode.Clone();
                    }
                    errorResponse.Errors.Add(error);
                }
                return JsonSerializer.SerializeToElement(errorResponse, HPDJsonContext.Default.ValidationErrorResponse);
            }

            // 4. Invoke the function using the delegate approach only.
            arguments.SetJson(jsonArgs);
            arguments.SetJsonSerializerOptions(serializerOptions);
            var result = await _invocationHandler(arguments, functionContext, cancellationToken);
            return await MarshalResultAsync(result, HPDOptions, serializerOptions, cancellationToken);
        }
    }

    private static async ValueTask<object?> MarshalResultAsync(
        object? result,
        HPDAIFunctionFactoryOptions options,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        var declaredResultType = options.ResultType;

        if (options.MarshalResult is not null)
        {
            return await options.MarshalResult(result, declaredResultType, cancellationToken).ConfigureAwait(false);
        }

        if (result is null)
            return null;

        if (IsEventSafeResult(result))
            return result;

        if (result is JsonDocument document)
            return document.RootElement.Clone();

        if (result is JsonNode node)
        {
            return JsonSerializer.SerializeToElement(
                node,
                HPDJsonContext.Default.JsonNode);
        }

        var resultType = declaredResultType ?? result.GetType();
        if (resultType == typeof(void) || resultType == typeof(Task) || resultType == typeof(ValueTask))
            return null;

        var typeInfo = serializerOptions.GetTypeInfo(resultType);
        return await SerializeResultAsync(result, typeInfo, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsEventSafeResult(object result)
    {
        return result is string
            or JsonElement
            or ToolResultPayload
            or ClientTools.IToolResultContent
            or AIContent
            || result is IEnumerable<ClientTools.IToolResultContent>
            || result is IEnumerable<AIContent>;
    }

    private static async ValueTask<JsonElement> SerializeResultAsync(
        object result,
        JsonTypeInfo typeInfo,
        CancellationToken cancellationToken)
    {
        if (typeInfo.Kind is JsonTypeInfoKind.None)
        {
            return JsonSerializer.SerializeToElement(result, typeInfo);
        }

        await using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, result, typeInfo, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return document.RootElement.Clone();
    }
}

/// <summary>
/// Extensions to AIFunctionArguments for JSON handling.
/// </summary>
public static class AIFunctionArgumentsExtensions
{
    internal const string JsonKey = "__raw_json__";
    internal const string JsonSerializerOptionsKey = "__json_serializer_options__";
    
    /// <summary>
    /// Gets the raw JSON element from the arguments.
    /// </summary>
    public static JsonElement GetJson(this AIFunctionArguments arguments)
    {
        if (arguments.TryGetValue(JsonKey, out var value) && value is JsonElement element)
        {
            return element;
        }
        return default;
    }
    
    /// <summary>
    /// Sets the raw JSON element in the arguments.
    /// </summary>
    public static void SetJson(this AIFunctionArguments arguments, JsonElement json)
    {
        arguments[JsonKey] = json;
    }

    public static JsonSerializerOptions GetJsonSerializerOptions(this AIFunctionArguments arguments)
    {
        if (arguments.TryGetValue(JsonSerializerOptionsKey, out var value) && value is JsonSerializerOptions options)
        {
            return options;
        }

        return HPDToolArgumentBinder.DefaultSerializerOptions;
    }

    public static void SetJsonSerializerOptions(this AIFunctionArguments arguments, JsonSerializerOptions options)
    {
        arguments[JsonSerializerOptionsKey] = options;
    }
}

public static class HPDToolArgumentBinder
{
    public static JsonSerializerOptions DefaultSerializerOptions { get; } = CreateDefaultSerializerOptions();

    public static bool TryGetProperty(JsonElement json, string name, out JsonElement value)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        return json.TryGetProperty(name, out value) ||
            json.TryGetProperty(ToCamelCase(name), out value) ||
            json.TryGetProperty(name.ToLowerInvariant(), out value);
    }

    public static T BindRequired<T>(JsonElement json, string name, JsonSerializerOptions serializerOptions)
    {
        if (!TryGetProperty(json, name, out var property))
        {
            throw new HPDToolArgumentException(
                name,
                $"Required property '{name}' is missing.",
                "missing_required_property");
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            throw new HPDToolArgumentException(
                name,
                $"Property '{name}' is required and cannot be null.",
                "null_required_property");
        }

        return BindValue<T>(property, name, serializerOptions);
    }

    public static T BindOptional<T>(JsonElement json, string name, T defaultValue, JsonSerializerOptions serializerOptions)
    {
        return TryGetProperty(json, name, out var property)
            ? BindValue<T>(property, name, serializerOptions)
            : defaultValue;
    }

    public static T BindValue<T>(JsonElement property, string name, JsonSerializerOptions serializerOptions)
    {
        if (property.ValueKind == JsonValueKind.Null)
        {
            var nullableType = Nullable.GetUnderlyingType(typeof(T));
            if (typeof(T).IsValueType && nullableType is null)
            {
                throw new HPDToolArgumentException(
                    name,
                    $"Property '{name}' is required and cannot be null.",
                    "null_required_property");
            }

            return default!;
        }

        var targetType = typeof(T);
        var enumType = Nullable.GetUnderlyingType(targetType) ?? (targetType.IsEnum ? targetType : null);
        if (enumType is not null)
        {
            return BindEnum<T>(property, name, enumType);
        }

        try
        {
            var typeInfo = serializerOptions.GetTypeInfo(typeof(T));
            return (T)JsonSerializer.Deserialize(property, typeInfo)!;
        }
        catch (JsonException ex)
        {
            throw new HPDToolArgumentException(name, ex.Message, "type_conversion_error", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new HPDToolArgumentException(name, ex.Message, "unsupported_parameter_type", ex);
        }
    }

    public static void ValidateNoUnmappedProperties(
        JsonElement json,
        JsonSerializerOptions serializerOptions,
        params string[] expectedNames)
    {
        if (serializerOptions.UnmappedMemberHandling != JsonUnmappedMemberHandling.Disallow ||
            json.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in json.EnumerateObject())
        {
            if (!IsExpectedPropertyName(property.Name, expectedNames))
            {
                throw new HPDToolArgumentException(
                    property.Name,
                    $"Property '{property.Name}' does not correspond to any tool parameter.",
                    "unmapped_property");
            }
        }
    }

    private static T BindEnum<T>(JsonElement property, string name, Type enumType)
    {
        try
        {
            object value = property.ValueKind switch
            {
                JsonValueKind.String => Enum.Parse(enumType, property.GetString() ?? "", ignoreCase: true),
                JsonValueKind.Number => Enum.ToObject(enumType, property.GetInt64()),
                _ => throw new JsonException($"Cannot convert {property.ValueKind} to {enumType.Name}.")
            };

            return (T)value;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or JsonException)
        {
            throw new HPDToolArgumentException(name, ex.Message, "type_conversion_error", ex);
        }
    }

    private static JsonSerializerOptions CreateDefaultSerializerOptions()
    {
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions);
        options.TypeInfoResolverChain.Add(HPDJsonContext.Default);
        options.MakeReadOnly();
        return options;
    }

    private static bool IsExpectedPropertyName(string actualName, string[] expectedNames)
    {
        foreach (var expectedName in expectedNames)
        {
            if (string.Equals(actualName, expectedName, StringComparison.Ordinal) ||
                string.Equals(actualName, ToCamelCase(expectedName), StringComparison.Ordinal) ||
                string.Equals(actualName, expectedName.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ToCamelCase(string value)
    {
        return string.IsNullOrEmpty(value) || char.IsLower(value[0])
            ? value
            : char.ToLowerInvariant(value[0]) + value.Substring(1);
    }
}

public sealed class HPDToolArgumentException : JsonException
{
    public HPDToolArgumentException(string propertyName, string message, string errorCode, Exception? innerException = null)
        : base(message, innerException)
    {
        PropertyName = propertyName;
        ErrorCode = errorCode;
    }

    public string PropertyName { get; }

    public string ErrorCode { get; }
}

public class HPDAIFunctionFactoryOptions
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string>? ParameterDescriptions { get; set; }
    public bool RequiresPermission { get; set; }
    public JsonSerializerOptions? SerializerOptions { get; set; }
    public Type? ResultType { get; set; }
    public Func<object?, Type?, CancellationToken, ValueTask<object?>>? MarshalResult { get; set; }

    // The validator now returns a list of detailed, structured errors.
    public Func<JsonElement, JsonSerializerOptions, List<ValidationError>>? Validator { get; set; }

    public Func<JsonElement>? SchemaProvider { get; set; }

    // Additional metadata properties for Harness Collapsing and other features
    public Dictionary<string, object?>? AdditionalProperties { get; set; }
}

public sealed record HPDToolSerializationOptions(JsonSerializerOptions? SerializerOptions = null);

/// <summary>
/// A structured response sent to the AI when function argument validation fails.
/// </summary>
public class ValidationErrorResponse
{
    [JsonPropertyName("error_type")]
    public string ErrorType { get; set; } = "validation_error";

    [JsonPropertyName("errors")]
    public List<ValidationError> Errors { get; set; } = new();

    [JsonPropertyName("retry_guidance")]
    public string RetryGuidance { get; set; } = "The provided arguments are invalid. Please review the errors, correct the arguments based on the function schema, and try again.";
}

/// <summary>
/// Describes a single validation error for a specific property, matching pydantic-ai's structure.
/// </summary>
public class ValidationError
{
    [JsonPropertyName("property")]
    public string Property { get; set; } = "";

    [JsonPropertyName("attempted_value")]
    public object? AttemptedValue { get; set; }

    [JsonPropertyName("error_message")]
    public string ErrorMessage { get; set; } = "";

    [JsonPropertyName("error_code")]
    public string ErrorCode { get; set; } = "";
}
