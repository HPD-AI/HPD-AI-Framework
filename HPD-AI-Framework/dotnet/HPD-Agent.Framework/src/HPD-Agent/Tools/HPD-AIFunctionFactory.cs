using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using System.Reflection;

namespace HPD.Agent;

/// <summary>
/// A modern, unified AIFunctionFactory that prioritizes delegate-based invocation
/// for performance and AOT-compatibility.
/// </summary>
public class HPDAIFunctionFactory
{
    private static readonly HPDAIFunctionFactoryOptions _defaultOptions = new();

    /// <summary>
    /// Creates an AIFunction using a pre-compiled invocation delegate.
    /// This is the preferred method for source-generated Harneses and adapters.
    /// </summary>
    public static AIFunction Create(
        Func<AIFunctionArguments, CancellationToken, Task<object?>> invocation, 
        HPDAIFunctionFactoryOptions? options = null)
    {
        return new HPDAIFunction(invocation, options ?? _defaultOptions);
    }


    /// <summary>
    /// Modern AIFunction implementation using delegate-based invocation with validation.
    /// </summary>
    public class HPDAIFunction : AIFunction
    {
        private readonly Func<AIFunctionArguments, CancellationToken, Task<object?>> _invocationHandler;
        private readonly MethodInfo? _method;

        // Constructor for the modern, delegate-based approach
        public HPDAIFunction(Func<AIFunctionArguments, CancellationToken, Task<object?>> invocationHandler, HPDAIFunctionFactoryOptions options)
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

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
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
                var argumentsDict = arguments.Where(kvp => kvp.Key != "__raw_json__").ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                var jsonString = JsonSerializer.Serialize(argumentsDict, HPDJsonContext.Default.DictionaryStringObject);
                jsonArgs = JsonDocument.Parse(jsonString).RootElement;
            }

            // 2. Use the validator.
            var validationErrors = HPDOptions.Validator?.Invoke(jsonArgs);

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
                return errorResponse;
            }

            // 4. Invoke the function using the delegate approach only.
            // NOTE: Must NOT use ConfigureAwait(false) here to preserve ExecutionContext flow for AsyncLocal (e.g., ConversationContext)
            arguments.SetJson(jsonArgs);
            return await _invocationHandler(arguments, cancellationToken);
        }
    }
}

/// <summary>
/// Extensions to AIFunctionArguments for JSON handling.
/// </summary>
public static class AIFunctionArgumentsExtensions
{
    private static readonly string JsonKey = "__raw_json__";
    
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
}

public class HPDAIFunctionFactoryOptions
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string>? ParameterDescriptions { get; set; }
    public bool RequiresPermission { get; set; }

    // The validator now returns a list of detailed, structured errors.
    public Func<JsonElement, List<ValidationError>>? Validator { get; set; }

    public Func<JsonElement>? SchemaProvider { get; set; }

    // Additional metadata properties for Harness Collapsing and other features
    public Dictionary<string, object?>? AdditionalProperties { get; set; }
}

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
