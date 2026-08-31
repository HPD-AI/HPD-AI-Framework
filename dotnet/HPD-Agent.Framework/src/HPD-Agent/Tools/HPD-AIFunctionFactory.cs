using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
            if (options.OperationContract is not null &&
                options.AdditionalProperties?.TryGetValue("Kind", out var kind) == true &&
                string.Equals(kind?.ToString(), "Output", StringComparison.Ordinal))
                throw new InvalidOperationException("Output tools cannot declare an action invocation contract.");
            if (options.OperationContract is { } declaredContract)
                options.OperationContract = NormalizeOperationContract(declaredContract);
            HPDOptions = options;

            var methodSchema = options.SchemaProvider?.Invoke() ?? default;
            JsonSchema = options.OperationContract is { } operationContract && !options.OperationContractSchemaComposed
                ? AgentInvocationModes.CreateActionSchema(methodSchema, operationContract)
                : options.OperationContract is null
                    ? AgentInvocationModes.CreateSchema(methodSchema, options.InvocationModePolicy)
                    : methodSchema.Clone();
            if (options.OperationContractSchemaComposed && options.OperationContract is { } composedContract)
                AgentInvocationModes.ValidateActionSchema(JsonSchema, composedContract);
            Name = options.Name ?? _method?.Name ?? "Unknown";
            Description = options.Description ?? "";
            ContractDescriptor = JsonSchema.ValueKind == JsonValueKind.Undefined
                ? null
                : AIFunctionContractDescriptor.Create(Name, JsonSchema);
            CanonicalInputContract = JsonSchema.ValueKind == JsonValueKind.Undefined
                ? null
                : CanonicalJsonInputContract.Create(JsonSchema);
        }

        private static AIFunctionOperationContract NormalizeOperationContract(
            AIFunctionOperationContract contract)
        {
            if (string.IsNullOrWhiteSpace(contract.ActionArgumentName) ||
                string.IsNullOrWhiteSpace(contract.Discriminator) || contract.Actions.Count == 0)
                throw new InvalidOperationException("The function action contract is incomplete.");
            var actions = new Dictionary<string, AIFunctionActionPolicy>(StringComparer.Ordinal);
            foreach (var (action, policy) in contract.Actions)
            {
                if (string.IsNullOrWhiteSpace(action) || policy is null || !actions.TryAdd(action, policy))
                    throw new InvalidOperationException("Function actions must have unique non-empty discriminators.");
                if (!Enum.IsDefined(policy.InvocationModePolicy) || !Enum.IsDefined(policy.InvocationModeHandling))
                    throw new InvalidOperationException($"Function action '{action}' has an unsupported invocation policy.");
            }
            return contract with
            {
                Actions = new ReadOnlyDictionary<string, AIFunctionActionPolicy>(actions)
            };
        }

        public HPDAIFunctionFactoryOptions HPDOptions { get; }
        public override string Name { get; }
        public override string Description { get; }
        public override JsonElement JsonSchema { get; }
        public override MethodInfo? UnderlyingMethod => _method;
        public override JsonSerializerOptions JsonSerializerOptions => HPDOptions.SerializerOptions ?? _defaultSerializerOptions;

        /// <summary>Gets the immutable composed contract published by this generated function.</summary>
        public AIFunctionContractDescriptor? ContractDescriptor { get; }

        internal IAIInputContract? CanonicalInputContract { get; }

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
                if (HPDOptions.ArgumentBinder is not null)
                {
                    return CreateValidationError(
                        string.Empty,
                        "raw_json_required",
                        "Generated AI functions require the original JSON argument object.");
                }

                // If no raw JSON is available, serialize the arguments dictionary.
                var argumentsDict = arguments
                    .Where(kvp => kvp.Key != AIFunctionArgumentsExtensions.JsonKey &&
                        kvp.Key != AIFunctionArgumentsExtensions.JsonSerializerOptionsKey &&
                        kvp.Key != AIFunctionArgumentsExtensions.BoundArgumentsKey)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                var jsonString = JsonSerializer.Serialize(argumentsDict, HPDJsonContext.Default.DictionaryStringObject);
                jsonArgs = JsonDocument.Parse(jsonString).RootElement;
            }

            arguments.SetJson(jsonArgs);
            arguments.SetJsonSerializerOptions(JsonSerializerOptions);
            var serializerOptions = JsonSerializerOptions;
            var runtimeHandlesInvocationMode =
                (functionContext.InvocationMode?.Handling ?? HPDOptions.InvocationModeHandling) ==
                AgentInvocationModeHandling.Runtime;
            var validationJsonArgs = jsonArgs;
            if (runtimeHandlesInvocationMode)
            {
                var sanitizedArguments = AgentInvocationModes.CreateSanitizedArguments(
                    arguments,
                    out _);
                validationJsonArgs = sanitizedArguments.GetJson();
            }

            // 2. Bind generated contracts once, or use the validator for external/manual functions.
            arguments.Remove(AIFunctionArgumentsExtensions.BoundArgumentsKey);
            IReadOnlyList<ValidationError>? validationErrors;
            if (HPDOptions.ArgumentBinder is not null)
            {
                var binding = HPDOptions.ArgumentBinder(validationJsonArgs);
                validationErrors = binding.Errors;
                if (binding.Value is not null && binding.Errors.Count == 0)
                    arguments.SetBoundInput(new AIFunctionBoundInput(binding.Value, binding.EffectiveJson.Clone()));
            }
            else
            {
                validationErrors = HPDOptions.Validator?.Invoke(validationJsonArgs, serializerOptions);
            }

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
                    if (validationJsonArgs.TryGetProperty(error.Property, out var propertyNode))
                    {
                        error.AttemptedValue = propertyNode.Clone();
                    }
                    errorResponse.Errors.Add(error);
                }
                return JsonSerializer.SerializeToElement(errorResponse, HPDJsonContext.Default.ValidationErrorResponse);
            }

            // 4. Invoke the function using the delegate approach only.
            if (!runtimeHandlesInvocationMode)
            {
                var directResult = await _invocationHandler(
                    arguments,
                    functionContext,
                    cancellationToken).ConfigureAwait(false);
                return await MarshalResultAsync(directResult, HPDOptions, serializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            var invocation = await FunctionInvocationRuntime.InvokeAsync(
                new FunctionInvocationRuntime.FunctionInvocationRequest
                {
                    Name = Name,
                    Arguments = arguments,
                    ParentContext = functionContext,
                    InvocationModePolicy = HPDOptions.InvocationModePolicy,
                    ResolvedInvocation = functionContext.InvocationMode,
                    OperationNotification = HPDOptions.OperationNotification,
                    InvokeFunctionAsync = InvokeFunctionBodyAsync
                },
                cancellationToken).ConfigureAwait(false);

            return invocation.ToToolResult();

            async Task<object?> InvokeFunctionBodyAsync(
                AIFunctionArguments invocationArguments,
                FunctionExecutionContext invocationContext,
                CancellationToken invocationToken)
            {
                var result = await _invocationHandler(
                    invocationArguments,
                    invocationContext,
                    invocationToken).ConfigureAwait(false);
                return await MarshalResultAsync(result, HPDOptions, serializerOptions, invocationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static JsonElement CreateValidationError(string property, string errorCode, string message)
    {
        var response = new ValidationErrorResponse();
        response.Errors.Add(new ValidationError
        {
            Property = property,
            ErrorCode = errorCode,
            ErrorMessage = message
        });
        return JsonSerializer.SerializeToElement(response, HPDJsonContext.Default.ValidationErrorResponse);
    }

    private static async ValueTask<object?> MarshalResultAsync(
        object? result,
        HPDAIFunctionFactoryOptions options,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        var declaredResultType = options.ResultType;

        if (result is null)
            return null;

        if (IsEventSafeResult(result))
            return result;

        if (options.MarshalResult is not null)
        {
            return await options.MarshalResult(result, declaredResultType, cancellationToken).ConfigureAwait(false);
        }

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

/// <summary>Describes the immutable composed JSON contract exposed by an HPD AI function.</summary>
public sealed record AIFunctionContractDescriptor
{
    /// <summary>Gets the published function name.</summary>
    public required string FunctionName { get; init; }

    /// <summary>Gets the lowercase SHA-256 fingerprint of the canonical composed schema.</summary>
    public required string CanonicalSchemaFingerprint { get; init; }

    /// <summary>Gets a detached copy of the canonical composed schema.</summary>
    public required JsonElement CanonicalSchema { get; init; }

    internal static AIFunctionContractDescriptor Create(string functionName, JsonElement schema)
    {
        var canonical = schema.GetRawText();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        return new AIFunctionContractDescriptor
        {
            FunctionName = functionName,
            CanonicalSchemaFingerprint = fingerprint,
            CanonicalSchema = schema.Clone()
        };
    }
}

/// <summary>
/// Extensions to AIFunctionArguments for JSON handling.
/// </summary>
public static class AIFunctionArgumentsExtensions
{
    internal const string JsonKey = "__raw_json__";
    internal const string JsonSerializerOptionsKey = "__json_serializer_options__";
    internal const string BoundArgumentsKey = "__hpd_bound_arguments__";
    
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

    /// <summary>Stores the complete one-shot result produced by an input binder.</summary>
    public static void SetBoundInput(this AIFunctionArguments arguments, AIFunctionBoundInput value) =>
        arguments[BoundArgumentsKey] = value;

    /// <summary>Gets the complete one-shot bound input produced before invocation.</summary>
    public static AIFunctionBoundInput GetBoundInput(this AIFunctionArguments arguments)
    {
        if (arguments.TryGetValue(BoundArgumentsKey, out var value) && value is AIFunctionBoundInput typed)
            return typed;
        throw new InvalidOperationException("AI-function arguments were not bound before invocation.");
    }

    /// <summary>Gets the one-shot result produced by a generated argument binder.</summary>
    public static T GetBoundArguments<T>(this AIFunctionArguments arguments)
    {
        if (arguments.TryGetValue(BoundArgumentsKey, out var value) &&
            value is AIFunctionBoundInput { Value: T typed })
            return typed;
        throw new InvalidOperationException("Generated AI-function arguments were not bound before invocation.");
    }
}

/// <summary>Contains the CLR value and effective canonical JSON produced by successful input binding.</summary>
/// <param name="Value">The generated CLR value or private argument carrier.</param>
/// <param name="EffectiveJson">The detached effective JSON document.</param>
public sealed record AIFunctionBoundInput(object Value, JsonElement EffectiveJson);

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
    public AgentInvocationModePolicy InvocationModePolicy { get; set; } =
        AgentInvocationModePolicy.SynchronousOnly;
    public AgentInvocationModeHandling InvocationModeHandling { get; set; } =
        AgentInvocationModeHandling.Runtime;

    /// <summary>Gets or sets the generated closed-union action contract for this function.</summary>
    public AIFunctionOperationContract? OperationContract { get; set; }

    /// <summary>Gets or sets whether a source-generated schema already contains verified action controls.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool OperationContractSchemaComposed { get; set; }
    public AgentOperationNotificationPolicy OperationNotification { get; set; } =
        new AgentOperationNotificationPolicy();

    // The validator now returns a list of detailed, structured errors.
    public Func<JsonElement, JsonSerializerOptions, List<ValidationError>>? Validator { get; set; }

    /// <summary>
    /// Gets or sets the generated one-shot structural validator and argument binder.
    /// A successful result is retained through invocation so CLR construction is not repeated.
    /// </summary>
    public Func<JsonElement, AIFunctionBindingResult>? ArgumentBinder { get; set; }

    public Func<JsonElement>? SchemaProvider { get; set; }

    // Additional metadata properties for ToolHarness Collapsing and other features
    public Dictionary<string, object?>? AdditionalProperties { get; set; }
}

/// <summary>Contains either one bound input with effective JSON or structural validation errors.</summary>
public sealed record AIFunctionBindingResult(
    object? Value,
    JsonElement EffectiveJson,
    IReadOnlyList<ValidationError> Errors)
{
    /// <summary>Creates a successful binding result.</summary>
    public static AIFunctionBindingResult Success(object value, JsonElement effectiveJson) =>
        new(value, effectiveJson.Clone(), Array.Empty<ValidationError>());

    /// <summary>Creates a failed binding result.</summary>
    public static AIFunctionBindingResult Failure(ValidationError error) =>
        new(null, default, new[] { error });
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
