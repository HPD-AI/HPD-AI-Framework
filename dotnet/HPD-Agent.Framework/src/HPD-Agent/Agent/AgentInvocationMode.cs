using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Defines which invocation modes an agent capability allows.
/// </summary>
[JsonConverter(typeof(AgentInvocationModePolicyJsonConverter))]
public enum AgentInvocationModePolicy
{
    /// <summary>
    /// The capability always waits for completion and returns the final result.
    /// </summary>
    SynchronousOnly,

    /// <summary>
    /// The capability always starts background work and returns a launch receipt.
    /// </summary>
    BackgroundOnly,

    /// <summary>
    /// The model may choose whether each call runs synchronously or in the background.
    /// </summary>
    ModelChoice
}

/// <summary>
/// Defines the resolved execution mode for a single agent capability invocation.
/// </summary>
[JsonConverter(typeof(AgentInvocationModeJsonConverter))]
public enum AgentInvocationMode
{
    /// <summary>
    /// Wait for completion and return the final result as the tool result.
    /// </summary>
    Synchronous,

    /// <summary>
    /// Start runtime-owned background work and return a launch receipt as the tool result.
    /// </summary>
    Background
}

/// <summary>
/// Defines where a normal AI function handles invocation mode.
/// </summary>
public enum AgentInvocationModeHandling
{
    /// <summary>
    /// The HPD runtime resolves invocation mode, registers background work, and invokes the function body.
    /// </summary>
    Runtime,

    /// <summary>
    /// The function body receives and handles the model-facing <c>invocationMode</c> argument itself.
    /// </summary>
    ToolBody
}

/// <summary>
/// JSON converter for <see cref="AgentInvocationModePolicy"/> values.
/// </summary>
public sealed class AgentInvocationModePolicyJsonConverter : JsonConverter<AgentInvocationModePolicy>
{
    /// <inheritdoc />
    public override AgentInvocationModePolicy Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return (AgentInvocationModePolicy)reader.GetInt32();

        var value = reader.GetString();
        return Normalize(value) switch
        {
            "synchronousonly" => AgentInvocationModePolicy.SynchronousOnly,
            "backgroundonly" => AgentInvocationModePolicy.BackgroundOnly,
            "modelchoice" => AgentInvocationModePolicy.ModelChoice,
            _ => throw new JsonException($"Unknown invocation mode policy '{value}'.")
        };
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        AgentInvocationModePolicy value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            AgentInvocationModePolicy.SynchronousOnly => "synchronousOnly",
            AgentInvocationModePolicy.BackgroundOnly => "backgroundOnly",
            AgentInvocationModePolicy.ModelChoice => "modelChoice",
            _ => value.ToString()
        });
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}

/// <summary>
/// JSON converter for <see cref="AgentInvocationMode"/> values.
/// </summary>
public sealed class AgentInvocationModeJsonConverter : JsonConverter<AgentInvocationMode>
{
    /// <inheritdoc />
    public override AgentInvocationMode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return (AgentInvocationMode)reader.GetInt32();

        var value = reader.GetString();
        return Normalize(value) switch
        {
            "synchronous" => AgentInvocationMode.Synchronous,
            "background" => AgentInvocationMode.Background,
            _ => throw new JsonException($"Unknown invocation mode '{value}'.")
        };
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        AgentInvocationMode value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            AgentInvocationMode.Synchronous => "synchronous",
            AgentInvocationMode.Background => "background",
            _ => value.ToString()
        });
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}

/// <summary>
/// Structured receipt returned to the model when an agent capability starts background work.
/// </summary>
public sealed record AgentBackgroundInvocationReceipt
{
    /// <summary>
    /// Gets the machine-readable launch status.
    /// </summary>
    public string Status { get; init; } = "background_started";

    /// <summary>
    /// Gets the runtime background task id, when a task was registered.
    /// </summary>
    public string? TaskId { get; init; }

    /// <summary>
    /// Gets the controllable background handle id, when a handle was registered.
    /// </summary>
    public string? HandleId { get; init; }

    /// <summary>
    /// Gets the kind of background handle, when a handle was registered.
    /// </summary>
    public BackgroundHandleKind? HandleKind { get; init; }

    /// <summary>
    /// Gets the operations supported by the handle.
    /// </summary>
    public BackgroundHandleOperation SupportedOperations { get; init; } =
        BackgroundHandleOperation.None;

    /// <summary>
    /// Gets the invoked subagent or workflow name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the source kind used by background task events.
    /// </summary>
    public required BackgroundTaskSourceKind SourceKind { get; init; }

    /// <summary>
    /// Gets the parent session id associated with the background task.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the parent thread id associated with the background task.
    /// </summary>
    public string? ThreadId { get; init; }

    /// <summary>
    /// Gets a short human-readable status message.
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// Result returned by mode-aware subagent and multi-agent runtimes.
/// </summary>
public sealed record AgentInvocationResult
{
    /// <summary>
    /// Gets the resolved invocation mode.
    /// </summary>
    public required AgentInvocationMode Mode { get; init; }

    /// <summary>
    /// Gets the final text for synchronous invocations.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets the original model-facing tool result for synchronous invocations when it should not be reduced to text.
    /// </summary>
    public object? ToolResult { get; init; }

    /// <summary>
    /// Gets the launch receipt for background invocations or structured mode errors.
    /// </summary>
    public AgentBackgroundInvocationReceipt? Background { get; init; }

    /// <summary>
    /// Converts this runtime result into the object returned by the tool wrapper.
    /// </summary>
    /// <returns>A string for synchronous calls, or a JSON tool payload for background calls.</returns>
    public object? ToToolResult()
    {
        if (Mode == AgentInvocationMode.Synchronous)
            return ToolResult ?? Text ?? string.Empty;

        if (Background is null)
            return string.Empty;

        var json = JsonSerializer.SerializeToElement(
            Background,
            HPDJsonContext.Default.AgentBackgroundInvocationReceipt);
        return new ToolResultPayload(
            Text: json.GetRawText(),
            Json: json,
            ResultType: typeof(AgentBackgroundInvocationReceipt).FullName);
    }
}

/// <summary>
/// Shared helpers for model-facing agent invocation mode arguments.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AgentInvocationModes
{
    private const string RawJsonArgumentKey = "__raw_json__";
    private const string JsonSerializerOptionsArgumentKey = "__json_serializer_options__";

    /// <summary>
    /// Resolves the effective invocation mode for a policy and optional caller request.
    /// </summary>
    /// <param name="policy">The capability's author-defined invocation policy.</param>
    /// <param name="requestedMode">The model-requested mode, when exposed by the schema.</param>
    /// <returns>The resolved invocation mode.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the request conflicts with the policy.</exception>
    public static AgentInvocationMode Resolve(
        AgentInvocationModePolicy policy,
        AgentInvocationMode? requestedMode)
        => policy switch
        {
            AgentInvocationModePolicy.SynchronousOnly when requestedMode == AgentInvocationMode.Background =>
                throw new InvalidOperationException("This capability only supports synchronous invocation."),
            AgentInvocationModePolicy.SynchronousOnly => AgentInvocationMode.Synchronous,
            AgentInvocationModePolicy.BackgroundOnly when requestedMode == AgentInvocationMode.Synchronous =>
                throw new InvalidOperationException("This capability only supports background invocation."),
            AgentInvocationModePolicy.BackgroundOnly => AgentInvocationMode.Background,
            AgentInvocationModePolicy.ModelChoice => requestedMode ?? AgentInvocationMode.Synchronous,
            _ => AgentInvocationMode.Synchronous
        };

    /// <summary>
    /// Reads the optional model-facing <c>invocationMode</c> argument from a JSON object.
    /// </summary>
    /// <param name="json">The model-provided JSON arguments.</param>
    /// <returns>The parsed invocation mode, or <see langword="null"/> when none was supplied.</returns>
    public static AgentInvocationMode? ReadRequestedMode(JsonElement json)
    {
        if (!json.TryGetProperty("invocationMode", out var property))
            return null;

        if (property.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("invocationMode must be either 'synchronous' or 'background'.");

        var value = property.GetString();
        if (string.Equals(value, "synchronous", StringComparison.OrdinalIgnoreCase))
            return AgentInvocationMode.Synchronous;
        if (string.Equals(value, "background", StringComparison.OrdinalIgnoreCase))
            return AgentInvocationMode.Background;

        throw new InvalidOperationException("invocationMode must be either 'synchronous' or 'background'.");
    }

    /// <summary>
    /// Copies arguments while removing the model-facing <c>invocationMode</c> control.
    /// </summary>
    /// <param name="arguments">The original tool arguments.</param>
    /// <param name="requestedMode">The parsed invocation mode, when provided.</param>
    /// <returns>Arguments safe to pass to the underlying tool implementation.</returns>
    public static AIFunctionArguments CreateSanitizedArguments(
        AIFunctionArguments arguments,
        out AgentInvocationMode? requestedMode)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var json = arguments.GetJson();
        requestedMode = json.ValueKind == JsonValueKind.Undefined
            ? null
            : ReadRequestedMode(json);

        var sanitized = new AIFunctionArguments();
        foreach (var (key, value) in arguments)
        {
            if (string.Equals(key, "invocationMode", StringComparison.Ordinal) ||
                string.Equals(key, RawJsonArgumentKey, StringComparison.Ordinal) ||
                string.Equals(key, JsonSerializerOptionsArgumentKey, StringComparison.Ordinal))
            {
                continue;
            }

            sanitized[key] = value;
        }

        sanitized.SetJsonSerializerOptions(arguments.GetJsonSerializerOptions());
        if (json.ValueKind != JsonValueKind.Undefined)
            sanitized.SetJson(RemoveInvocationMode(json));

        return sanitized;
    }

    /// <summary>
    /// Removes the model-facing <c>invocationMode</c> control from a JSON argument object.
    /// </summary>
    /// <param name="json">The original JSON arguments.</param>
    /// <returns>A cloned JSON element without <c>invocationMode</c>.</returns>
    public static JsonElement RemoveInvocationMode(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object ||
            !json.TryGetProperty("invocationMode", out _))
        {
            return json.Clone();
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in json.EnumerateObject())
            {
                if (string.Equals(property.Name, "invocationMode", StringComparison.Ordinal))
                    continue;

                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Adds the model-facing <c>invocationMode</c> argument to a tool schema when the policy allows model choice.
    /// </summary>
    /// <param name="originalSchema">The original tool JSON schema.</param>
    /// <param name="invocationModePolicy">The invocation mode policy.</param>
    /// <returns>The original schema or a cloned schema with <c>invocationMode</c>.</returns>
    public static JsonElement CreateSchema(
        JsonElement originalSchema,
        AgentInvocationModePolicy invocationModePolicy)
    {
        if (originalSchema.ValueKind == JsonValueKind.Undefined)
            return default;

        if (invocationModePolicy != AgentInvocationModePolicy.ModelChoice)
            return originalSchema.Clone();

        JsonObject schema;
        if (originalSchema.ValueKind == JsonValueKind.Object)
        {
            schema = JsonNode.Parse(originalSchema.GetRawText()) as JsonObject ?? new JsonObject();
        }
        else
        {
            schema = new JsonObject
            {
                ["type"] = "object"
            };
        }

        schema["type"] ??= "object";

        if (schema["properties"] is not JsonObject properties)
        {
            properties = new JsonObject();
            schema["properties"] = properties;
        }

        properties["invocationMode"] = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray("synchronous", "background"),
            ["description"] = "Whether to wait for the result now or run it in the background. Use synchronous unless the work can continue independently."
        };

        return JsonSerializer.SerializeToElement(
            schema,
            HPDJsonContext.Default.JsonObject);
    }

    /// <summary>
    /// Creates a model-facing receipt for a background invocation that could not be started.
    /// </summary>
    /// <param name="name">The subagent or workflow name.</param>
    /// <param name="sourceKind">The background task source kind.</param>
    /// <param name="message">The reason background work could not be started.</param>
    /// <param name="status">The machine-readable receipt status.</param>
    /// <returns>A structured background invocation result.</returns>
    public static AgentInvocationResult CreateReceiptResult(
        string name,
        BackgroundTaskSourceKind sourceKind,
        string message,
        string status = "background_unavailable")
        => new()
        {
            Mode = AgentInvocationMode.Background,
            Background = new AgentBackgroundInvocationReceipt
            {
                Status = status,
                Name = name,
                SourceKind = sourceKind,
                Message = message
            }
        };
}
