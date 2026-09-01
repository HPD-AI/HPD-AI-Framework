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
    /// HPD resolves and sanitizes invocation mode, then the function body handles the resolved mode
    /// through <see cref="FunctionExecutionContext.ResolvedInvocationMode"/>.
    /// </summary>
    ToolBody
}

/// <summary>Defines the immutable effective invocation policy for one function action.</summary>
public sealed record AIFunctionActionPolicy
{
    /// <summary>Gets the fully resolved action policy.</summary>
    public required AgentInvocationModePolicy InvocationModePolicy { get; init; }

    /// <summary>Gets the fully resolved action handling strategy.</summary>
    public required AgentInvocationModeHandling InvocationModeHandling { get; init; }

    /// <summary>Gets the complete permission declaration resolved for this action.</summary>
    public required AIFunctionPermissionDeclaration Permission { get; init; }
}

/// <summary>Identifies where an effective permission declaration originated.</summary>
public enum PermissionDeclarationSource
{
    /// <summary>The framework supplied the unprotected default.</summary>
    FrameworkDefault,
    /// <summary>The containing function supplied the declaration.</summary>
    FunctionAttribute,
    /// <summary>The concrete action atomically replaced the function declaration.</summary>
    ActionOverride
}

/// <summary>Describes one immutable normalized function or action permission declaration.</summary>
public sealed record AIFunctionPermissionDeclaration
{
    /// <summary>Creates the framework-default protected declaration for a function name.</summary>
    public static AIFunctionPermissionDeclaration Required(string functionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        return new AIFunctionPermissionDeclaration
        {
            RequiresPermission = true,
            Authority = $"function/{Uri.EscapeDataString(functionName)}",
            Source = PermissionDeclarationSource.FrameworkDefault
        };
    }

    /// <summary>Gets whether permission mediation is required.</summary>
    public required bool RequiresPermission { get; init; }

    /// <summary>Gets the canonical generated or application-owned permission authority.</summary>
    public required string Authority { get; init; }

    /// <summary>Gets the stable permission-policy descriptor ID, when custom policy is selected.</summary>
    public string? PolicyDescriptorId { get; init; }

    /// <summary>Gets the stable permission-interaction descriptor ID, when custom interaction is selected.</summary>
    public string? InteractionDescriptorId { get; init; }

    /// <summary>Gets the declaration source.</summary>
    public required PermissionDeclarationSource Source { get; init; }
}

/// <summary>Describes the direct closed-union argument owned by a compound AI function.</summary>
public sealed record AIFunctionOperationContract
{
    /// <summary>Gets the serialized name of the direct model-facing union argument.</summary>
    public required string ActionArgumentName { get; init; }

    /// <summary>Gets the union's exact discriminator property name.</summary>
    public required string Discriminator { get; init; }

    /// <summary>Gets every declared discriminator and its effective policy.</summary>
    public required IReadOnlyDictionary<string, AIFunctionActionPolicy> Actions { get; init; }
}

/// <summary>Contains immutable invocation facts resolved for one native function call.</summary>
public sealed record ResolvedFunctionInvocation
{
    /// <summary>Gets the selected compound action, or <see langword="null"/> for an ordinary function.</summary>
    public string? Action { get; init; }

    /// <summary>Gets the mode requested by the model, when supplied.</summary>
    public AgentInvocationMode? RequestedMode { get; init; }

    /// <summary>Gets the effective execution mode.</summary>
    public required AgentInvocationMode Mode { get; init; }

    /// <summary>Gets the effective policy.</summary>
    public required AgentInvocationModePolicy Policy { get; init; }

    /// <summary>Gets the effective handling strategy.</summary>
    public required AgentInvocationModeHandling Handling { get; init; }

    /// <summary>Gets the constructor-free validated action projection, when applicable.</summary>
    public ValidatedFunctionAction? ValidatedAction { get; init; }

    /// <summary>Gets how the authoritative argument document entered HPD.</summary>
    public FunctionArgumentIngressProvenance IngressProvenance { get; init; }
}

/// <summary>Identifies whether function JSON was preserved or canonically reconstructed.</summary>
public enum FunctionArgumentIngressProvenance
{
    /// <summary>The provider supplied the original JSON document.</summary>
    Original,
    /// <summary>HPD reconstructed a canonical document from already-parsed arguments.</summary>
    Canonicalized
}

/// <summary>Describes a ToolBody operation that committed before the tool call completed.</summary>
public sealed record CommittedToolBodyOperation
{
    /// <summary>Gets the authoritative operation receipt.</summary>
    public required AgentOperationReceipt Receipt { get; init; }
    /// <summary>Gets the function name.</summary>
    public required string FunctionName { get; init; }
    /// <summary>Gets the model tool-call identifier.</summary>
    public required string FunctionCallId { get; init; }
}

/// <summary>Provides a bounded durable projection of one resolved function invocation.</summary>
public sealed record FunctionInvocationAuditProjection
{
    /// <summary>Gets the function name.</summary>
    public required string FunctionName { get; init; }
    /// <summary>Gets the model tool-call identifier.</summary>
    public required string FunctionCallId { get; init; }
    /// <summary>Gets the selected action, when the function is action-contracted.</summary>
    public string? Action { get; init; }
    /// <summary>Gets the mode requested by the caller, when supplied.</summary>
    public AgentInvocationMode? RequestedMode { get; init; }
    /// <summary>Gets the effective execution mode.</summary>
    public required AgentInvocationMode ResolvedMode { get; init; }
    /// <summary>Gets the effective invocation policy.</summary>
    public required AgentInvocationModePolicy Policy { get; init; }
    /// <summary>Gets the effective handling strategy.</summary>
    public required AgentInvocationModeHandling Handling { get; init; }
    /// <summary>Gets the authoritative argument ingress provenance.</summary>
    public required FunctionArgumentIngressProvenance IngressProvenance { get; init; }
}

/// <summary>Provides a detached constructor-free view of a structurally validated action.</summary>
public sealed record ValidatedFunctionAction
{
    /// <summary>Gets the exact validated discriminator.</summary>
    public required string Action { get; init; }

    /// <summary>Gets detached canonical JSON for the validated action object.</summary>
    public required JsonElement CanonicalJson { get; init; }

    /// <summary>Attempts to read a validated primitive field without constructing an author DTO.</summary>
    /// <param name="name">The exact serialized property name.</param>
    /// <param name="value">Receives a detached JSON value.</param>
    /// <returns><see langword="true"/> when the field exists.</returns>
    public bool TryGetProperty(string name, out JsonElement value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (CanonicalJson.TryGetProperty(name, out var found))
        {
            value = found.Clone();
            return true;
        }
        value = default;
        return false;
    }
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

    /// <summary>Gets the unified operation receipt for provider-owned or durable work.</summary>
    public AgentOperationReceipt? Operation { get; init; }

    /// <summary>
    /// Converts this runtime result into the object returned by the tool wrapper.
    /// </summary>
    /// <returns>A string for synchronous calls, or a JSON tool payload for background calls.</returns>
    public object? ToToolResult()
    {
        if (Mode == AgentInvocationMode.Synchronous)
            return ToolResult ?? Text ?? string.Empty;

        if (Operation is not null)
        {
            var operationJson = JsonSerializer.SerializeToElement(
                Operation,
                HPDJsonContext.Default.AgentOperationReceipt);
            return new ToolResultPayload(
                Text: operationJson.GetRawText(),
                Json: operationJson,
                ResultType: typeof(AgentOperationReceipt).FullName);
        }

        return Text ?? string.Empty;
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

    /// <summary>Resolves and removes the nested invocation control for a compound function.</summary>
    /// <param name="arguments">The authoritative function arguments.</param>
    /// <param name="contract">The compound-function contract.</param>
    /// <param name="sanitizedArguments">Receives arguments with the framework control removed.</param>
    /// <returns>The immutable resolved invocation facts.</returns>
    public static ResolvedFunctionInvocation ResolveAction(
        AIFunctionArguments arguments,
        AIFunctionOperationContract contract,
        out AIFunctionArguments sanitizedArguments,
        FunctionArgumentIngressProvenance ingressProvenance = FunctionArgumentIngressProvenance.Original)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(contract);
        if (string.IsNullOrWhiteSpace(contract.ActionArgumentName) ||
            string.IsNullOrWhiteSpace(contract.Discriminator) || contract.Actions.Count == 0)
            throw new InvalidOperationException("The function action contract is incomplete.");

        var root = arguments.GetJson();
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Function arguments must be a JSON object.");
        var actionObject = GetSingleProperty(root, contract.ActionArgumentName, required: true);
        if (actionObject.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"'{contract.ActionArgumentName}' must be a JSON object.");
        var discriminator = GetSingleProperty(actionObject, contract.Discriminator, required: true);
        if (discriminator.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(discriminator.GetString()))
            throw new InvalidOperationException($"'{contract.Discriminator}' must be a non-empty string.");
        var action = discriminator.GetString()!;
        if (!contract.Actions.TryGetValue(action, out var policy))
            throw new InvalidOperationException($"Unknown function action '{action}'.");

        var requestedElement = GetSingleProperty(actionObject, "invocationMode", required: false);
        AgentInvocationMode? requested = requestedElement.ValueKind == JsonValueKind.Undefined
            ? null
            : ReadRequestedModeFromValue(requestedElement);
        var mode = Resolve(policy.InvocationModePolicy, requested);
        sanitizedArguments = CloneWithNestedControlRemoved(arguments, root, contract.ActionArgumentName);
        var sanitizedAction = sanitizedArguments.GetJson().GetProperty(contract.ActionArgumentName);
        return new ResolvedFunctionInvocation
        {
            Action = action,
            RequestedMode = requested,
            Mode = mode,
            Policy = policy.InvocationModePolicy,
            Handling = policy.InvocationModeHandling,
            IngressProvenance = ingressProvenance,
            ValidatedAction = new ValidatedFunctionAction
            {
                Action = action,
                CanonicalJson = sanitizedAction.Clone()
            }
        };
    }

    private static JsonElement GetSingleProperty(JsonElement value, string name, bool required)
    {
        JsonElement found = default;
        var count = 0;
        foreach (var property in value.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.Ordinal)) continue;
            found = property.Value;
            count++;
        }
        if (count > 1) throw new InvalidOperationException($"'{name}' must occur at most once.");
        if (count == 0 && required) throw new InvalidOperationException($"Required property '{name}' is missing.");
        return found;
    }

    private static AgentInvocationMode ReadRequestedModeFromValue(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("invocationMode must be either 'synchronous' or 'background'.");
        return value.GetString() switch
        {
            "synchronous" => AgentInvocationMode.Synchronous,
            "background" => AgentInvocationMode.Background,
            _ => throw new InvalidOperationException("invocationMode must be either 'synchronous' or 'background'.")
        };
    }

    private static AIFunctionArguments CloneWithNestedControlRemoved(
        AIFunctionArguments arguments, JsonElement root, string actionArgumentName)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (!string.Equals(property.Name, actionArgumentName, StringComparison.Ordinal))
                {
                    property.Value.WriteTo(writer);
                    continue;
                }
                writer.WriteStartObject();
                foreach (var member in property.Value.EnumerateObject())
                    if (!string.Equals(member.Name, "invocationMode", StringComparison.Ordinal)) member.WriteTo(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        var sanitizedJson = document.RootElement.Clone();
        var sanitized = new AIFunctionArguments();
        foreach (var (key, value) in arguments)
        {
            if (key is RawJsonArgumentKey or JsonSerializerOptionsArgumentKey) continue;
            sanitized[key] = string.Equals(key, actionArgumentName, StringComparison.Ordinal)
                ? sanitizedJson.GetProperty(actionArgumentName).Clone()
                : value;
        }
        sanitized.SetJsonSerializerOptions(arguments.GetJsonSerializerOptions());
        sanitized.SetJson(sanitizedJson);
        return sanitized;
    }

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
        if (json.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Function arguments must be a JSON object.");

        JsonElement property = default;
        var occurrences = 0;
        foreach (var candidate in json.EnumerateObject())
        {
            if (!string.Equals(candidate.Name, "invocationMode", StringComparison.Ordinal))
                continue;
            occurrences++;
            property = candidate.Value;
        }

        if (occurrences == 0)
            return null;
        if (occurrences > 1)
            throw new InvalidOperationException("invocationMode must occur at most once.");

        if (property.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("invocationMode must be either 'synchronous' or 'background'.");

        var value = property.GetString();
        if (string.Equals(value, "synchronous", StringComparison.Ordinal))
            return AgentInvocationMode.Synchronous;
        if (string.Equals(value, "background", StringComparison.Ordinal))
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

    /// <summary>Copies a root argument dictionary while resolving and removing invocationMode.</summary>
    /// <param name="arguments">The authoritative argument dictionary.</param>
    /// <param name="requestedMode">Receives the optional requested mode.</param>
    /// <returns>A detached dictionary containing only domain arguments.</returns>
    public static IReadOnlyDictionary<string, object?> CreateSanitizedArgumentDictionary(
        IReadOnlyDictionary<string, object?> arguments,
        out AgentInvocationMode? requestedMode)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        requestedMode = null;
        var sanitized = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
        {
            if (string.Equals(key, "invocationMode", StringComparison.Ordinal))
            {
                if (requestedMode is not null)
                    throw new InvalidOperationException("invocationMode must occur at most once.");
                requestedMode = ParseRequestedMode(value);
            }
            else
            {
                sanitized[key] = value;
            }
        }
        return sanitized;
    }

    /// <summary>Reads an exact string discriminator from an argument dictionary.</summary>
    /// <param name="arguments">The authoritative domain arguments.</param>
    /// <param name="discriminator">The exact discriminator property name.</param>
    /// <returns>The non-empty discriminator value.</returns>
    public static string ResolveDiscriminator(
        IReadOnlyDictionary<string, object?> arguments,
        string discriminator)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(discriminator);
        if (!arguments.TryGetValue(discriminator, out var raw) ||
            raw is not string && raw is not JsonElement)
            throw new ArgumentException($"Compound tool requires string discriminator '{discriminator}'.", nameof(arguments));
        var value = raw is string text
            ? text
            : ((JsonElement)raw).ValueKind == JsonValueKind.String ? ((JsonElement)raw).GetString() : null;
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Compound tool requires string discriminator '{discriminator}'.", nameof(arguments));
        return value;
    }

    private static AgentInvocationMode ParseRequestedMode(object? value)
    {
        if (value is AgentInvocationMode mode)
            return mode;
        var text = value switch
        {
            string candidate => candidate,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            _ => null
        };
        return text?.ToLowerInvariant() switch
        {
            "synchronous" => AgentInvocationMode.Synchronous,
            "background" => AgentInvocationMode.Background,
            _ => throw new InvalidOperationException("invocationMode must be either 'synchronous' or 'background'.")
        };
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
        AgentInvocationModePolicy invocationModePolicy,
        string? discriminator = null,
        IReadOnlySet<string>? modelChoiceActions = null)
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

        if (modelChoiceActions is not null &&
            !string.IsNullOrWhiteSpace(discriminator) &&
            schema["oneOf"] is JsonArray branches)
        {
            foreach (var branchNode in branches)
            {
                if (branchNode is not JsonObject branch ||
                    branch["properties"] is not JsonObject branchProperties ||
                    branchProperties[discriminator] is not JsonObject discriminatorSchema ||
                    discriminatorSchema["const"]?.GetValue<string>() is not { } action ||
                    !modelChoiceActions.Contains(action))
                {
                    continue;
                }

                branchProperties["invocationMode"] = CreateInvocationModeSchema();
            }
        }
        else
        {
            if (schema["properties"] is not JsonObject properties)
            {
                properties = new JsonObject();
                schema["properties"] = properties;
            }

            properties["invocationMode"] = CreateInvocationModeSchema();
        }

        return JsonSerializer.SerializeToElement(
            schema,
            HPDJsonContext.Default.JsonObject);
    }

    /// <summary>Composes action-scoped invocation controls into a direct union parameter schema.</summary>
    /// <param name="originalSchema">The unmodified function schema.</param>
    /// <param name="contract">The complete action contract.</param>
    /// <returns>A cloned schema with controls only on model-choice branches.</returns>
    public static JsonElement CreateActionSchema(
        JsonElement originalSchema,
        AIFunctionOperationContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (originalSchema.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("An action-contracted function requires an object schema.");
        var parsedRoot = JsonNode.Parse(originalSchema.GetRawText()) as JsonObject
            ?? throw new InvalidOperationException("The function schema is invalid.");
        var root = ExpandLocalSchemaReferences(parsedRoot);
        if (root["properties"] is not JsonObject properties ||
            properties[contract.ActionArgumentName] is not JsonObject actionSchemaNode)
            throw new InvalidOperationException("The action union must be a direct model-facing parameter.");
        var actionSchema = ResolveLocalSchemaReference(root, actionSchemaNode);
        if (
            actionSchema["oneOf"] is not JsonArray branches)
            throw new InvalidOperationException("The action union must be a direct model-facing parameter.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in branches)
        {
            if (node is not JsonObject branchNode)
                throw new InvalidOperationException("Every action branch must be an object schema.");
            var branch = ResolveLocalSchemaReference(root, branchNode);
            if (branch["properties"] is not JsonObject branchProperties ||
                branchProperties[contract.Discriminator] is not JsonObject discriminator ||
                discriminator["const"]?.GetValue<string>() is not { Length: > 0 } action ||
                !contract.Actions.TryGetValue(action, out var policy) || !seen.Add(action))
                throw new InvalidOperationException("Every action branch must have one unique declared discriminator.");
            if (branchProperties.ContainsKey("invocationMode"))
                throw new InvalidOperationException("Action branches cannot declare a domain property named 'invocationMode'.");
            if (branch["required"] is JsonArray required &&
                required.Any(item => item is JsonValue value && value.TryGetValue<string>(out var name) && name == "invocationMode"))
                throw new InvalidOperationException($"Action '{action}' must keep invocationMode optional.");
            if (policy.InvocationModePolicy == AgentInvocationModePolicy.ModelChoice)
                branchProperties["invocationMode"] = CreateInvocationModeSchema();
        }
        if (seen.Count != contract.Actions.Count)
            throw new InvalidOperationException("The action schema and action contract contain different branches.");
        return JsonSerializer.SerializeToElement(root, HPDJsonContext.Default.JsonObject);
    }

    /// <summary>Verifies that a precomposed generated schema exactly exposes its declared action controls.</summary>
    /// <param name="schema">The generated composed schema.</param>
    /// <param name="contract">The effective action contract.</param>
    public static void ValidateActionSchema(JsonElement schema, AIFunctionOperationContract contract)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The generated action schema is not a direct closed union.");
        var root = JsonNode.Parse(schema.GetRawText()) as JsonObject
            ?? throw new InvalidOperationException("The generated action schema is invalid.");
        if (root["properties"] is not JsonObject properties ||
            properties[contract.ActionArgumentName] is not JsonObject actionSchemaNode)
            throw new InvalidOperationException("The generated action schema is not a direct closed union.");
        var actionSchema = ResolveLocalSchemaReference(root, actionSchemaNode);
        if (actionSchema["oneOf"] is not JsonArray branches)
            throw new InvalidOperationException("The generated action schema is not a direct closed union.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in branches)
        {
            if (node is not JsonObject branchNode)
                throw new InvalidOperationException("The generated action schema contains a non-object branch.");
            var branch = ResolveLocalSchemaReference(root, branchNode);
            if (branch["properties"] is not JsonObject branchProperties ||
                branchProperties[contract.Discriminator] is not JsonObject discriminator ||
                discriminator["const"]?.GetValue<string>() is not { Length: > 0 } action ||
                !contract.Actions.TryGetValue(action, out var policy) || !seen.Add(action))
                throw new InvalidOperationException("The generated action schema does not match its action contract.");
            var control = branchProperties["invocationMode"] as JsonObject;
            var exposesControl = control is not null;
            if (exposesControl != (policy.InvocationModePolicy == AgentInvocationModePolicy.ModelChoice))
                throw new InvalidOperationException($"Action '{action}' exposes an invocation control inconsistent with its policy.");
            if (exposesControl && (control!["enum"] is not JsonArray values || values.Count != 2 ||
                values[0] is not JsonValue synchronous || !synchronous.TryGetValue<string>(out var synchronousValue) || synchronousValue != "synchronous" ||
                values[1] is not JsonValue background || !background.TryGetValue<string>(out var backgroundValue) || backgroundValue != "background" ||
                control["type"]?.GetValue<string>() != "string"))
                throw new InvalidOperationException($"Action '{action}' has an invalid invocation control.");
            if (branch["required"] is JsonArray required &&
                required.Any(item => item is JsonValue value && value.TryGetValue<string>(out var name) && name == "invocationMode"))
                throw new InvalidOperationException($"Action '{action}' must keep invocationMode optional.");
            if (branch["required"] is not JsonArray requiredProperties ||
                !requiredProperties.Any(item => item is JsonValue value && value.TryGetValue<string>(out var name) && name == contract.Discriminator))
                throw new InvalidOperationException($"Action '{action}' must require discriminator '{contract.Discriminator}'.");
        }
        if (seen.Count != contract.Actions.Count)
            throw new InvalidOperationException("The generated action schema omits a declared action.");
    }

    private static JsonObject ResolveLocalSchemaReference(JsonObject root, JsonObject schema)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var depth = 0; depth < 16 && schema["$ref"] is JsonValue referenceValue; depth++)
        {
            var reference = referenceValue.GetValue<string>();
            if (!reference.StartsWith("#/", StringComparison.Ordinal) || !visited.Add(reference))
                throw new InvalidOperationException("Action schemas may use only acyclic document-local references.");
            JsonNode? target = root;
            foreach (var encodedSegment in reference.AsSpan(2).ToString().Split('/'))
            {
                var segment = encodedSegment.Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
                target = target is JsonObject targetObject ? targetObject[segment] : null;
            }
            schema = target as JsonObject
                ?? throw new InvalidOperationException($"Action schema reference '{reference}' does not resolve to an object.");
        }
        if (schema["$ref"] is not null)
            throw new InvalidOperationException("Action schema reference depth exceeds the supported limit.");
        return schema;
    }

    private static JsonObject ExpandLocalSchemaReferences(JsonObject root)
        => ExpandNode(root, root, new HashSet<string>(StringComparer.Ordinal), 0) as JsonObject
            ?? throw new InvalidOperationException("The expanded function schema is invalid.");

    private static JsonNode? ExpandNode(
        JsonNode? node,
        JsonObject root,
        HashSet<string> activeReferences,
        int depth)
    {
        if (depth > 32)
            throw new InvalidOperationException("Action schema reference expansion exceeds the supported depth.");
        if (node is JsonObject objectNode && objectNode["$ref"] is JsonValue referenceValue)
        {
            if (objectNode.Count != 1)
                throw new InvalidOperationException("Action schema references cannot have sibling keywords.");
            var reference = referenceValue.GetValue<string>();
            if (!reference.StartsWith("#/", StringComparison.Ordinal) || !activeReferences.Add(reference))
                throw new InvalidOperationException("Action schemas may use only acyclic document-local references.");
            JsonNode? target = root;
            foreach (var encodedSegment in reference.AsSpan(2).ToString().Split('/'))
            {
                var segment = encodedSegment.Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
                target = target is JsonObject targetObject ? targetObject[segment] : null;
            }
            if (target is null)
                throw new InvalidOperationException($"Action schema reference '{reference}' cannot be resolved.");
            var expanded = ExpandNode(target, root, activeReferences, depth + 1);
            activeReferences.Remove(reference);
            return expanded;
        }
        if (node is JsonObject sourceObject)
        {
            var result = new JsonObject();
            foreach (var property in sourceObject)
            {
                if (property.Key is "$defs" or "definitions")
                    continue;
                result[property.Key] = ExpandNode(property.Value, root, activeReferences, depth + 1);
            }
            return result;
        }
        if (node is JsonArray sourceArray)
        {
            var result = new JsonArray();
            foreach (var item in sourceArray)
                result.Add(ExpandNode(item, root, activeReferences, depth + 1));
            return result;
        }
        return node?.DeepClone();
    }

    private static JsonObject CreateInvocationModeSchema() =>
        new()
        {
            ["type"] = "string",
            ["enum"] = new JsonArray("synchronous", "background"),
            ["description"] = "Whether to wait for the result now or run it in the background. Use synchronous unless the work can continue independently."
        };

    /// <summary>
    /// Creates a model-facing receipt for a background invocation that could not be started.
    /// </summary>
    /// <param name="name">The subagent or workflow name.</param>
    /// <param name="sourceKind">The background task source kind.</param>
    /// <param name="message">The reason background work could not be started.</param>
    /// <param name="status">The machine-readable receipt status.</param>
    /// <returns>A structured background invocation result.</returns>
    public static AgentInvocationResult CreateFailureResult(
        string name,
        AgentOperationSourceKind sourceKind,
        string message,
        string status = "background_unavailable")
        => new()
        {
            Mode = AgentInvocationMode.Synchronous,
            Text = $"{status}: {message}",
            ToolResult = $"{status}: {message}"
        };
}
