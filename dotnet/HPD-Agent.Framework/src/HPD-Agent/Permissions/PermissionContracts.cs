using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Permissions;

/// <summary>Evaluates the exact validated invocation. The framework owns interaction and execution.</summary>
public interface IPermissionPolicy
{
    ValueTask<PermissionPolicyResult> EvaluateAsync(ValidatedPermissionInput input,
        PermissionEvaluationContext context, CancellationToken cancellationToken);
}

/// <summary>Revalidates a stored rule under the current versioned policy.</summary>
public interface IValidatedPermissionRulePolicy
{
    bool MatchesStoredDecision(ValidatedPermissionInput input,
        PermissionEvaluationContext context, PermissionDecisionRecord record);
}

/// <summary>Generated activation metadata; no reflection or custom interaction protocol.</summary>
public sealed record AIFunctionPermissionDescriptor
{
    public required string DescriptorId { get; init; }
    public required Func<IServiceProvider, IPermissionPolicy> PolicyFactory { get; init; }
}

public abstract record PermissionOutcome;
public sealed record PermissionAllow : PermissionOutcome;
public sealed record PermissionDeny(string Reason,
    PermissionDeniedBehavior DeniedBehavior = PermissionDeniedBehavior.InterruptTurn) : PermissionOutcome;
public sealed record PermissionAsk(string? Reason, PermissionRisk? Risk, PermissionResource? Resource,
    IReadOnlyList<PermissionChoice> Choices) : PermissionOutcome;
public sealed record PermissionChoice(string Id, string Label, PermissionDecisionKind Kind,
    PermissionDeniedBehavior? DeniedBehavior = null);
public sealed record PermissionResource(string Value, string DisplayName);
public sealed record PermissionEvidence(string Kind, string SchemaVersion, JsonElement Payload);
public sealed record PermissionPolicyResult
{
    public required PermissionOutcome Outcome { get; init; }
    public PermissionEvidence? Evidence { get; init; }
    public IReadOnlyDictionary<string, PermissionPersistenceProposal>? PersistenceByChoice { get; init; }
}
public sealed record PermissionPersistenceProposal
{
    public required PermissionPersistenceKind Kind { get; init; }
    public string? RuleTypeId { get; init; }
    public JsonElement? CanonicalRule { get; init; }
}
[JsonConverter(typeof(JsonStringEnumConverter<PermissionRisk>))]
public enum PermissionRisk { Low, Medium, High, Critical }
[JsonConverter(typeof(JsonStringEnumConverter<PermissionDecisionKind>))]
public enum PermissionDecisionKind { Allow, Deny, Feedback }
[JsonConverter(typeof(JsonStringEnumConverter<PermissionPersistenceKind>))]
public enum PermissionPersistenceKind { Session, ValidatedRule }
/// <summary>Standard names for the built-in permission domains.</summary>
public static class PermissionDomains
{
    public const string ToolExecution = "ToolExecution";
    public const string SandboxCapability = "SandboxCapability";
}
[JsonConverter(typeof(JsonStringEnumConverter<PersistenceUnavailableReason>))]
public enum PersistenceUnavailableReason { NoStableAgentId, NoSessionStore }
public enum PermissionGrantSource
{
    PolicyDecision, StoredDecision, HostAutoApprove, HostBypass, UserDecision,
    FrameworkDecision, Cancelled, TimedOut
}
/// <summary>Trusted construction-time authorization mode; never a run override.</summary>
public enum AuthorizationMode { Enforce, Bypass }
public enum PermissionBindingSource { Function, Action, Mcp, Builder, RunOverride }
public sealed record AIFunctionPermissionBinding(string Authority, string? PolicyDescriptorId,
    PermissionBindingSource Source);

/// <summary>The minimal permission prompt exposed to ordinary hosts.</summary>
public sealed record PermissionPrompt(
    string PermissionId,
    string CallId,
    string FunctionName,
    string? Action,
    JsonElement Arguments,
    string? Reason);

/// <summary>The minimal approval or rejection answer expected from ordinary hosts.</summary>
public sealed record PermissionAnswer(
    string PermissionId,
    bool Approved,
    string? Reason = null);

/// <summary>Projects rich permission requests onto the stable simple-host contract.</summary>
public static class PermissionPromptExtensions
{
    /// <summary>Gets the simple prompt fields for a permission request.</summary>
    public static PermissionPrompt GetPrompt(this PermissionRequestEvent request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(
            request.PermissionId,
            request.CallId,
            request.FunctionName ?? request.Authority ?? "unknown",
            request.Action,
            request.Arguments?.Clone() ?? JsonDocument.Parse("{}").RootElement.Clone(),
            request.Reason);
    }

    /// <summary>
    /// Converts the simple answer to the advanced permission response. The resolver requires
    /// <c>allow_once</c> and <c>deny_once</c>; policies offering only custom choices use the
    /// advanced <see cref="PermissionResponseEvent"/> contract directly.
    /// </summary>
    public static PermissionResponseEvent Answer(
        this PermissionRequestEvent request,
        bool approved,
        string? reason = null) =>
        request.Answer(new PermissionAnswer(request.PermissionId, approved, reason));

    /// <summary>Converts the simple answer to its exact declared permission choice.</summary>
    public static PermissionResponseEvent Answer(
        this PermissionRequestEvent request,
        PermissionAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(answer);
        if (!string.Equals(request.PermissionId, answer.PermissionId, StringComparison.Ordinal))
            throw new InvalidOperationException("PermissionAnswerUnbound: permission ID does not match.");

        var requiredId = answer.Approved ? "allow_once" : "deny_once";
        var requiredKind = answer.Approved
            ? PermissionDecisionKind.Allow
            : PermissionDecisionKind.Deny;
        var choice = request.Choices.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, requiredId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Simple permission answers require a declared '{requiredId}' {requiredKind} choice.");
        return new PermissionResponseEvent(
            request.PermissionId,
            request.SourceName,
            choice.Id,
            answer.Reason);
    }
}

/// <summary>Provides constructor-free access to the complete sanitized argument document.</summary>
public sealed class ValidatedPermissionInput
{
    internal ValidatedPermissionInput(
        JsonElement canonicalArguments,
        ResolvedFunctionInvocation invocation)
    {
        CanonicalArguments = canonicalArguments.Clone();
        Invocation = invocation ?? throw new ArgumentNullException(nameof(invocation));
        Action = invocation.ValidatedAction;
    }

    /// <summary>Gets the detached canonical model-facing argument document.</summary>
    public JsonElement CanonicalArguments { get; }

    /// <summary>Gets the immutable resolved invocation facts.</summary>
    public ResolvedFunctionInvocation Invocation { get; }

    /// <summary>Gets the validated action projection for a compound function.</summary>
    public ValidatedFunctionAction? Action { get; }

    /// <summary>Reads one required top-level string parameter without constructing an author DTO.</summary>
    public string GetRequiredString(string jsonParameterName)
    {
        var value = GetRequiredValue(jsonParameterName);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidOperationException($"Permission input '{jsonParameterName}' is not a string.");
    }

    /// <summary>Reads one required top-level Boolean parameter without constructing an author DTO.</summary>
    public bool GetBoolean(string jsonParameterName)
    {
        var value = GetRequiredValue(jsonParameterName);
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new InvalidOperationException($"Permission input '{jsonParameterName}' is not a Boolean.");
    }

    /// <summary>Reads one required top-level canonical parameter value.</summary>
    public JsonElement GetRequiredValue(string jsonParameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonParameterName);
        if (CanonicalArguments.ValueKind != JsonValueKind.Object ||
            !CanonicalArguments.TryGetProperty(jsonParameterName, out var value))
            throw new InvalidOperationException($"Permission input '{jsonParameterName}' is missing.");
        return value.Clone();
    }

    /// <summary>Returns the compound action projection or fails for an ordinary function.</summary>
    public ValidatedFunctionAction RequireAction() => Action ??
        throw new InvalidOperationException("This function has no action input.");
}

/// <summary>Provides immutable invocation facts and scoped services to a permission policy.</summary>
public sealed class PermissionEvaluationContext
{
    /// <summary>Gets the model-facing function name.</summary>
    public required string FunctionName { get; init; }
    /// <summary>Gets the selected action discriminator.</summary>
    public string? Action { get; init; }
    /// <summary>Gets the unique function-call ID.</summary>
    public required string FunctionCallId { get; init; }
    /// <summary>Gets the effective permission authority.</summary>
    public required string Authority { get; init; }
    /// <summary>Gets the constructor-free validated input.</summary>
    public required ValidatedPermissionInput Input { get; init; }
    /// <summary>Gets the immutable run configuration.</summary>
    public required AgentRunConfig RunConfig { get; init; }
    /// <summary>Gets invocation-scoped services.</summary>
    public required IServiceProvider Services { get; init; }
}


internal sealed record ResolvedFunction(HPDAIFunctionFactory.HPDAIFunction Function,
    AIFunctionPermissionBinding? Permission);

/// <summary>Freezes the declaration and arguments admitted by the call gate.</summary>
public sealed record PermissionAuthorityStamp
{
    public required JsonElement CanonicalArguments { get; init; }
    public required AIFunctionPermissionDeclaration? Declaration { get; init; }
    public required string PolicyDescriptorId { get; init; }
}
/// <summary>Authorization for one exact call, never a reusable permission record.</summary>
public sealed record FunctionPermissionGrant
{
    public required string PermissionId { get; init; }
    public required string FunctionCallId { get; init; }
    public required string FunctionName { get; init; }
    public string? Action { get; init; }
    public required PermissionDecisionKey Key { get; init; }
    public string? ChoiceId { get; init; }
    public required DateTimeOffset GrantedAt { get; init; }
    public required PermissionGrantSource Source { get; init; }
    public required PermissionAuthorityStamp Authority { get; init; }
}
internal abstract record CallDisposition;
internal sealed record Invoke(FunctionPermissionGrant? Grant) : CallDisposition;
internal sealed record Reject(string Result, string ReasonCode) : CallDisposition;
internal sealed record ContinueAfterFeedback(string Result, string ReasonCode) : CallDisposition;
