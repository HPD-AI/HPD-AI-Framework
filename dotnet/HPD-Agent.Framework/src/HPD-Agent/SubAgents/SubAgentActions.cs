using System.Collections.ObjectModel;
using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Immutable pre-filter catalog of every subagent declaration admitted by an agent build.</summary>
public sealed record SubAgentDeclarationCatalog
{
    /// <summary>Gets the stable revision fingerprint of the declarations.</summary>
    public required string Revision { get; init; }

    /// <summary>Gets declarations keyed by their generated capability identity.</summary>
    public required IReadOnlyDictionary<CapabilityId, SubAgentActionDescriptor> Declarations { get; init; }

    /// <summary>Creates and validates a catalog before any visibility filtering is applied.</summary>
    /// <param name="declarations">All materialized subagent declarations.</param>
    /// <returns>A canonical immutable catalog.</returns>
    public static SubAgentDeclarationCatalog Create(IEnumerable<SubAgentActionDescriptor> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var ordered = declarations
            .OrderBy(static value => value.CapabilityId.Value, StringComparer.Ordinal)
            .ThenBy(static value => value.ParentToolHarness, StringComparer.Ordinal)
            .ThenBy(static value => value.Action, StringComparer.Ordinal)
            .ToArray();
        var map = new Dictionary<CapabilityId, SubAgentActionDescriptor>();
        foreach (var declaration in ordered)
        {
            if (map.TryGetValue(declaration.CapabilityId, out var existing))
                throw new InvalidOperationException(
                    $"Duplicate subagent capability '{declaration.CapabilityId.Value}' is declared by " +
                    $"'{existing.ParentToolHarness}' and '{declaration.ParentToolHarness}'.");
            map.Add(declaration.CapabilityId, declaration);
        }
        var canonical = string.Join("\n", ordered.Select(static value => string.Join('|',
            value.CapabilityId.Value,
            value.ParentToolHarness,
            value.RequiresToolHarnessActivation,
            value.Action,
            value.Definition.AgentId,
            value.Definition.Name,
            value.Description,
            value.InvocationModePolicy,
            value.InvocationModeHandling,
            value.ContextPolicy,
            value.RequiresPermission,
            value.Definition.Availability.MaximumChildDepth)));
        return new SubAgentDeclarationCatalog
        {
            Revision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonical))),
            Declarations = new ReadOnlyDictionary<CapabilityId, SubAgentActionDescriptor>(map)
        };
    }

}

internal sealed record SubAgentDeclarationCatalogPin(
    IReadOnlyList<SubAgentActionDescriptor> Actions,
    SubAgentDeclarationCatalog Catalog);

/// <summary>Immutable generated declaration for one model-facing subagent creation action.</summary>
public sealed record SubAgentActionDescriptor
{
    /// <summary>Gets the declaring ToolHarness model identity used for creation visibility.</summary>
    public required string ParentToolHarness { get; init; }
    /// <summary>Gets whether creation requires the declaring ToolHarness to be expanded.</summary>
    public required bool RequiresToolHarnessActivation { get; init; }
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
    /// <summary>Gets the generated exact final binder for this admitted role branch.</summary>
    public Func<JsonElement, AIFunctionBindingResult>? BranchBinder { get; init; }
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
                    Authority = $"function/{FunctionName}/action/{Uri.EscapeDataString(descriptor.Action)}",
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
                    Authority = $"function/{FunctionName}/action/{Uri.EscapeDataString(action)}",
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
        var roles = ordered.ToDictionary(static descriptor => descriptor.Action, StringComparer.Ordinal);
        var composition = new VerifiedAIFunctionActionComposition(
            schema, contract, root => BindFinalBranch(root, roles));
        return HPDAIFunctionFactory.CreateComposedAction(
            (arguments, context, cancellationToken) =>
                SubAgentOperationDispatcher.DispatchAsync(roles, arguments, context, cancellationToken),
            composition,
            new HPDAIFunctionFactoryOptions
            {
                Name = FunctionName,
                Description = "Create and control durable subagents owned by this conversation.",
                FunctionPermission = new AIFunctionPermissionDeclaration
                {
                    RequiresPermission = true,
                    Authority = $"function/{FunctionName}",
                    Source = PermissionDeclarationSource.FunctionAttribute
                },
                InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly,
                InvocationModeHandling = AgentInvocationModeHandling.ToolBody,
                ResultType = typeof(SubAgentActionResult),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsSubAgent"] = true,
                    ["ExecutionModel"] = "ThreadNative",
                    ["SubAgentActions"] = ordered
                }
            });
    }

    private static AIFunctionBindingResult BindFinalBranch(
        JsonElement root,
        IReadOnlyDictionary<string, SubAgentActionDescriptor> roles)
    {
        var actionJson = root.GetProperty("request");
        var action = actionJson.GetProperty("action").GetString()
            ?? throw new InvalidOperationException("SubAgents action discriminator is missing.");
        var branch = ProjectBranch(actionJson);
        if (roles.TryGetValue(action, out var role))
        {
            var bound = (role.BranchBinder ?? (json => SubAgentGeneratedBranchBinder.Bind(
                json, role.ContextPolicy == SubAgentContextPolicy.ModelChoice)))(branch);
            if (bound.Errors.Count > 0) return bound;
            return AIFunctionBindingResult.Success(new BoundSubAgentAction(action, bound.Value, branch), branch);
        }
        return AIFunctionBindingResult.Success(new BoundSubAgentAction(action, null, branch), branch);
    }

    internal static JsonElement ProjectBranch(JsonElement action)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in action.EnumerateObject())
            {
                if (!string.Equals(property.Name, "action", StringComparison.Ordinal) &&
                    !string.Equals(property.Name, "invocationMode", StringComparison.Ordinal))
                    property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
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
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WritePropertyName("properties"); writer.WriteStartObject();
            writer.WritePropertyName("request"); writer.WriteStartObject();
            writer.WritePropertyName("oneOf"); writer.WriteStartArray();
            foreach (var descriptor in descriptors)
                WriteRoleBranch(writer, descriptor);
            WriteControlBranch(writer, "continue", includeInput: true, includeMode: true);
            WriteControlBranch(writer, "list");
            WriteControlBranch(writer, "wait", wait: true);
            WriteControlBranch(writer, "sendMessage", includeInput: true);
            WriteControlBranch(writer, "cancel", cancel: true);
            writer.WriteEndArray(); writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WritePropertyName("required"); writer.WriteStartArray(); writer.WriteStringValue("request"); writer.WriteEndArray();
            writer.WriteBoolean("additionalProperties", false);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteRoleBranch(Utf8JsonWriter writer, SubAgentActionDescriptor descriptor)
    {
        WriteBranchStart(writer, descriptor.Action, descriptor.Description);
        WriteStringSchema(writer, "input", "The complete task for the subagent.");
        if (descriptor.InvocationModePolicy == AgentInvocationModePolicy.ModelChoice)
            WriteEnumSchema(writer, "invocationMode", "synchronous", "background");
        if (descriptor.ContextPolicy == SubAgentContextPolicy.ModelChoice)
            WriteEnumSchema(writer, "context", "fork", "fresh", "isolated");
        WriteBranchEnd(writer, "action", "input");
    }

    private static void WriteControlBranch(
        Utf8JsonWriter writer, string action, bool includeInput = false, bool includeMode = false,
        bool wait = false, bool cancel = false)
    {
        WriteBranchStart(writer, action, null);
        if (action is not "list" and not "wait") WriteStringSchema(writer, "child", "A parent-local child identifier.");
        if (includeInput) WriteStringSchema(writer, "input", "The semantic input to deliver.");
        if (includeMode) WriteEnumSchema(writer, "invocationMode", "synchronous", "background");
        if (wait)
        {
            writer.WritePropertyName("children"); writer.WriteStartObject(); writer.WriteString("type", "array");
            writer.WritePropertyName("items"); writer.WriteStartObject(); writer.WriteString("type", "string"); writer.WriteEndObject(); writer.WriteEndObject();
            WriteEnumSchema(writer, "mode", "any", "all");
            writer.WritePropertyName("timeoutSeconds"); writer.WriteStartObject(); writer.WriteString("type", "integer"); writer.WriteNumber("minimum", 0); writer.WriteEndObject();
        }
        if (cancel) WriteStringSchema(writer, "reason", null);
        var required = new List<string> { "action" };
        if (action is not "list" and not "wait") required.Add("child");
        if (includeInput) required.Add("input");
        WriteBranchEnd(writer, required.ToArray());
    }

    private static void WriteBranchStart(Utf8JsonWriter writer, string action, string? description)
    {
        writer.WriteStartObject(); writer.WriteString("type", "object");
        writer.WritePropertyName("properties"); writer.WriteStartObject();
        writer.WritePropertyName("action"); writer.WriteStartObject(); writer.WriteString("type", "string");
        writer.WriteString("const", action); if (description is not null) writer.WriteString("description", description); writer.WriteEndObject();
    }

    private static void WriteBranchEnd(Utf8JsonWriter writer, params string[] required)
    {
        writer.WriteEndObject(); writer.WritePropertyName("required"); writer.WriteStartArray();
        foreach (var value in required) writer.WriteStringValue(value);
        writer.WriteEndArray(); writer.WriteBoolean("additionalProperties", false); writer.WriteEndObject();
    }

    private static void WriteStringSchema(Utf8JsonWriter writer, string name, string? description)
    {
        writer.WritePropertyName(name); writer.WriteStartObject(); writer.WriteString("type", "string");
        if (description is not null) writer.WriteString("description", description); writer.WriteEndObject();
    }

    private static void WriteEnumSchema(Utf8JsonWriter writer, string name, params string[] values)
    {
        writer.WritePropertyName(name); writer.WriteStartObject(); writer.WriteString("type", "string");
        writer.WritePropertyName("enum"); writer.WriteStartArray(); foreach (var value in values) writer.WriteStringValue(value);
        writer.WriteEndArray(); writer.WriteEndObject();
    }

}

internal sealed record BoundSubAgentAction(string Action, object? Value, JsonElement Branch);

/// <summary>Strongly typed admitted input for one generated subagent role branch.</summary>
public sealed record BoundSubAgentStartAction(string Input, string? Context);

/// <summary>Native-AOT-safe binder used by generated subagent role descriptors.</summary>
public static class SubAgentGeneratedBranchBinder
{
    /// <summary>Binds one discriminator-free role projection exactly once after permission admission.</summary>
    public static AIFunctionBindingResult Bind(JsonElement json, bool allowContext)
    {
        if (allowContext)
            HPDGeneratedToolArgumentBinder.ValidateProperties(json, "", "input", "context");
        else
            HPDGeneratedToolArgumentBinder.ValidateProperties(json, "", "input");
        var input = HPDGeneratedToolArgumentBinder.BindString(
            HPDGeneratedToolArgumentBinder.GetRequiredProperty(json, "input", ""), "input");
        string? context = null;
        if (allowContext && HPDGeneratedToolArgumentBinder.TryGetOptionalProperty(json, "context", "", out var value))
            context = HPDGeneratedToolArgumentBinder.BindString(value, "context");
        return AIFunctionBindingResult.Success(new BoundSubAgentStartAction(input, context), json);
    }
}

/// <summary>Stable model-facing status returned by unified subagent operations.</summary>
public enum SubAgentOperationStatus { Running, Completed, Failed, Cancelled, Unavailable }

/// <summary>Stable structured subagent operation error.</summary>
public sealed record SubAgentOperationError(string Code, string Message);

/// <summary>Closed structured result union returned by the unified <c>SubAgents</c> function.</summary>
[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "$result")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SubAgentOperationResult), "operation")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SubAgentListResult), "list")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SubAgentWaitResult), "wait")]
public abstract record SubAgentActionResult;

/// <summary>Structured result returned by every unified subagent action.</summary>
public sealed record SubAgentOperationResult : SubAgentActionResult
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
public sealed record SubAgentListResult(IReadOnlyList<SubAgentListItem> Children) : SubAgentActionResult;

/// <summary>One exact execution observed by the <c>wait</c> action.</summary>
public sealed record SubAgentWaitItem(string Child, string? ThreadExecutionId, string Status);

/// <summary>Structured observational result returned by the <c>wait</c> action.</summary>
public sealed record SubAgentWaitResult(
    bool TimedOut,
    IReadOnlyList<SubAgentWaitItem> Children) : SubAgentActionResult;

internal static class SubAgentOperationDispatcher
{
    internal static Task<object?> DispatchAsync(
        IReadOnlyDictionary<string, SubAgentActionDescriptor> roles,
        AIFunctionArguments arguments,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        var validated = context.InvocationMode?.ValidatedAction
            ?? throw new InvalidOperationException("SubAgents requires one validated action.");
        var bound = arguments.GetBoundArguments<BoundSubAgentAction>();
        if (!string.Equals(bound.Action, validated.Action, StringComparison.Ordinal))
            throw new InvalidOperationException("SubAgents bound action does not match admitted action authority.");
        return roles.TryGetValue(bound.Action, out var descriptor)
            ? DispatchStartAsync(descriptor, (BoundSubAgentStartAction)bound.Value!, context, cancellationToken)
            : DispatchControlAsync(bound.Action, bound.Branch, context, cancellationToken);
    }

    private static async Task<object?> DispatchStartAsync(
        SubAgentActionDescriptor descriptor,
        BoundSubAgentStartAction branch,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        var input = branch.Input;
        SubAgentContext? requestedContext = branch.Context is null
            ? null
            : Enum.Parse<SubAgentContext>(branch.Context, ignoreCase: true);
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

}
