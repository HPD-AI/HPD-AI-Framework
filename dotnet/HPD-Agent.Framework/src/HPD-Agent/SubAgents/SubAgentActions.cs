using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Immutable generated declaration for one model-facing subagent creation action.</summary>
public sealed record SubAgentActionDescriptor
{
    /// <summary>Gets the exact closed-union discriminator.</summary>
    public required string Action { get; init; }
    /// <summary>Gets the model-facing role description.</summary>
    public required string Description { get; init; }
    /// <summary>Gets the stable generated capability identity.</summary>
    public required CapabilityId CapabilityId { get; init; }
    /// <summary>Gets the frozen durable child definition.</summary>
    public required SubAgent Definition { get; init; }
    /// <summary>Gets the effective invocation-mode policy.</summary>
    public required AgentInvocationModePolicy InvocationModePolicy { get; init; }
    /// <summary>Gets the effective invocation-mode handling.</summary>
    public required AgentInvocationModeHandling InvocationModeHandling { get; init; }
    /// <summary>Gets the declaration's child-context policy.</summary>
    public required SubAgentContextPolicy ContextPolicy { get; init; }
    /// <summary>Gets whether permission is required before this branch is bound.</summary>
    public required bool RequiresPermission { get; init; }
}

/// <summary>Builds the one reserved closed <c>SubAgents</c> action function.</summary>
public static class SubAgentsFunctionFactory
{
    /// <summary>The reserved model-facing function name.</summary>
    public const string FunctionName = "SubAgents";

    private static readonly string[] ReservedActions =
        ["continue", "list", "wait", "sendMessage", "cancel"];

    /// <summary>Creates exactly one function from all currently effective role descriptors.</summary>
    public static AIFunction Create(IReadOnlyList<SubAgentActionDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var ordered = descriptors
            .OrderBy(static descriptor => descriptor.CapabilityId.Value, StringComparer.Ordinal)
            .ThenBy(static descriptor => descriptor.Action, StringComparer.Ordinal)
            .ToArray();
        Validate(ordered);
        var schema = ComposeSchema(ordered);
        var policies = new Dictionary<string, AIFunctionActionPolicy>(StringComparer.Ordinal);
        foreach (var descriptor in ordered)
        {
            policies.Add(descriptor.Action, new AIFunctionActionPolicy
            {
                InvocationModePolicy = descriptor.InvocationModePolicy,
                InvocationModeHandling = AgentInvocationModeHandling.ToolBody,
                Permission = new AIFunctionPermissionDeclaration
                {
                    RequiresPermission = descriptor.RequiresPermission,
                    Scope = descriptor.Action,
                    Source = PermissionDeclarationSource.ActionOverride
                }
            });
        }
        foreach (var action in ReservedActions)
        {
            policies.Add(action, new AIFunctionActionPolicy
            {
                InvocationModePolicy = action == "continue"
                    ? AgentInvocationModePolicy.ModelChoice
                    : AgentInvocationModePolicy.SynchronousOnly,
                InvocationModeHandling = AgentInvocationModeHandling.ToolBody,
                Permission = new AIFunctionPermissionDeclaration
                {
                    RequiresPermission = true,
                    Scope = action,
                    Source = PermissionDeclarationSource.ActionOverride
                }
            });
        }
        var contract = new AIFunctionOperationContract
        {
            ActionArgumentName = "request",
            Discriminator = "action",
            Actions = new ReadOnlyDictionary<string, AIFunctionActionPolicy>(policies)
        };
        var composition = new VerifiedAIFunctionActionComposition(schema, contract);
        var roles = ordered.ToDictionary(static descriptor => descriptor.Action, StringComparer.Ordinal);
        return HPDAIFunctionFactory.CreateComposedAction(
            (arguments, context, cancellationToken) =>
                SubAgentOperationDispatcher.DispatchAsync(roles, context, cancellationToken),
            composition,
            new HPDAIFunctionFactoryOptions
            {
                Name = FunctionName,
                Description = "Create and control durable subagents owned by this conversation.",
                FunctionPermission = new AIFunctionPermissionDeclaration
                {
                    RequiresPermission = true,
                    Scope = "function/SubAgent",
                    Source = PermissionDeclarationSource.FunctionAttribute
                },
                InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly,
                InvocationModeHandling = AgentInvocationModeHandling.ToolBody,
                ResultType = typeof(SubAgentOperationResult),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsSubAgent"] = true,
                    ["ExecutionModel"] = "ThreadNative",
                    ["SubAgentActions"] = ordered
                }
            });
    }

    private static void Validate(IReadOnlyList<SubAgentActionDescriptor> descriptors)
    {
        var owners = new Dictionary<string, CapabilityId>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Action);
            if (ReservedActions.Contains(descriptor.Action, StringComparer.Ordinal))
                throw new InvalidOperationException($"Subagent role '{descriptor.Action}' collides with a reserved framework action.");
            if (!owners.TryAdd(descriptor.Action, descriptor.CapabilityId))
                throw new InvalidOperationException(
                    $"Duplicate subagent action '{descriptor.Action}' is owned by '{owners[descriptor.Action]}' and '{descriptor.CapabilityId}'.");
        }
    }

    private static JsonElement ComposeSchema(IReadOnlyList<SubAgentActionDescriptor> descriptors)
    {
        var branches = new JsonArray();
        foreach (var descriptor in descriptors)
        {
            var properties = new JsonObject
            {
                ["action"] = ConstString(descriptor.Action, descriptor.Description),
                ["input"] = StringProperty("The complete task for the subagent.")
            };
            var required = new JsonArray("action", "input");
            if (descriptor.InvocationModePolicy == AgentInvocationModePolicy.ModelChoice)
                properties["invocationMode"] = EnumString("synchronous", "background");
            if (descriptor.ContextPolicy == SubAgentContextPolicy.ModelChoice)
                properties["context"] = EnumString("fork", "fresh", "isolated");
            branches.Add(ObjectBranch(properties, required));
        }
        branches.Add(ControlBranch("continue", includeInput: true, includeMode: true));
        branches.Add(ControlBranch("list"));
        branches.Add(ControlBranch("wait", extra: new JsonObject
        {
            ["children"] = new JsonObject { ["type"] = "array", ["items"] = StringProperty(null) },
            ["mode"] = EnumString("any", "all"),
            ["timeoutSeconds"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 }
        }));
        branches.Add(ControlBranch("sendMessage", includeInput: true));
        branches.Add(ControlBranch("cancel", extra: new JsonObject { ["reason"] = StringProperty(null) }));
        var root = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["request"] = new JsonObject { ["oneOf"] = branches }
            },
            ["required"] = new JsonArray("request"),
            ["additionalProperties"] = false
        };
        return JsonSerializer.SerializeToElement(root);
    }

    private static JsonObject ControlBranch(
        string action,
        bool includeInput = false,
        bool includeMode = false,
        JsonObject? extra = null)
    {
        var properties = new JsonObject { ["action"] = ConstString(action, null) };
        var required = new JsonArray("action");
        if (action != "list" && action != "wait")
        {
            properties["child"] = StringProperty("A parent-local child identifier.");
            required.Add("child");
        }
        if (includeInput)
        {
            properties["input"] = StringProperty("The semantic input to deliver.");
            required.Add("input");
        }
        if (includeMode) properties["invocationMode"] = EnumString("synchronous", "background");
        if (extra is not null)
            foreach (var property in extra) properties[property.Key] = property.Value?.DeepClone();
        return ObjectBranch(properties, required);
    }

    private static JsonObject ObjectBranch(JsonObject properties, JsonArray required) => new()
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = required,
        ["additionalProperties"] = false
    };

    private static JsonObject ConstString(string value, string? description) => new()
    {
        ["type"] = "string",
        ["const"] = value,
        ["description"] = description
    };

    private static JsonObject StringProperty(string? description) => new()
    {
        ["type"] = "string",
        ["description"] = description
    };

    private static JsonObject EnumString(params string[] values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray(values.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray())
    };
}

/// <summary>Stable model-facing status returned by unified subagent operations.</summary>
public enum SubAgentOperationStatus { Running, Completed, Failed, Cancelled, Unavailable }

/// <summary>Stable structured subagent operation error.</summary>
public sealed record SubAgentOperationError(string Code, string Message);

/// <summary>Structured result returned by every unified subagent action.</summary>
public sealed record SubAgentOperationResult
{
    /// <summary>Gets the current operation status.</summary>
    public required SubAgentOperationStatus Status { get; init; }
    /// <summary>Gets the parent-local child identifier, when registration succeeded.</summary>
    public string? Child { get; init; }
    /// <summary>Gets the semantic invocation identifier.</summary>
    public string? InvocationId { get; init; }
    /// <summary>Gets the exclusive child execution identifier.</summary>
    public string? ThreadExecutionId { get; init; }
    /// <summary>Gets the background operation identifier.</summary>
    public string? AgentOperationId { get; init; }
    /// <summary>Gets bounded child output for completed synchronous work.</summary>
    public string? Output { get; init; }
    /// <summary>Gets a stable actionable error.</summary>
    public SubAgentOperationError? Error { get; init; }
}

/// <summary>One bounded child summary returned by the <c>list</c> action.</summary>
public sealed record SubAgentListItem(
    string Child,
    string Role,
    SubAgentChildAvailability Availability,
    DateTimeOffset CreatedAt,
    string? Reason);

/// <summary>Structured result returned by the <c>list</c> action.</summary>
public sealed record SubAgentListResult(IReadOnlyList<SubAgentListItem> Children);

internal static class SubAgentOperationDispatcher
{
    internal static Task<object?> DispatchAsync(
        IReadOnlyDictionary<string, SubAgentActionDescriptor> roles,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        var validated = context.InvocationMode?.ValidatedAction
            ?? throw new InvalidOperationException("SubAgents requires one validated action.");
        var branch = ProjectBranch(validated.CanonicalJson);
        return roles.TryGetValue(validated.Action, out var descriptor)
            ? DispatchStartAsync(descriptor, branch, context, cancellationToken)
            : DispatchControlAsync(validated.Action, branch, context, cancellationToken);
    }

    private static async Task<object?> DispatchStartAsync(
        SubAgentActionDescriptor descriptor,
        JsonElement branch,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        var input = branch.GetProperty("input").GetString() ?? string.Empty;
        var requestedContext = branch.TryGetProperty("context", out var contextValue)
            ? SubAgentContexts.ReadRequestedContext(branch)
            : null;
        var result = await SubAgentRuntime.InvokeAsync(new SubAgentRuntime.SubAgentInvocationRequest
        {
            Definition = descriptor.Definition,
            Input = input,
            ParentContext = context,
            RequestedMode = context.InvocationMode?.RequestedMode,
            RequestedContext = requestedContext,
            CapabilityId = descriptor.CapabilityId
        }, cancellationToken).ConfigureAwait(false);
        return result.ToToolResult();
    }

    private static Task<object?> DispatchControlAsync(
        string action,
        JsonElement branch,
        FunctionExecutionContext context,
        CancellationToken cancellationToken) =>
        SubAgentRuntime.ControlAsync(action, branch, context, cancellationToken);

    private static JsonElement ProjectBranch(JsonElement action)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in action.EnumerateObject())
                if (!string.Equals(property.Name, "action", StringComparison.Ordinal)) property.WriteTo(writer);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
}
