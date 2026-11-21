using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Permissions;

/// <summary>Evaluates the exact validated invocation. The framework owns interaction and execution.</summary>
public interface IPermissionPolicy
{
    ValueTask<PermissionPolicyResult> EvaluateAsync(PreparedPermissionInput input,
        PermissionEvaluationContext context, CancellationToken cancellationToken);
}

/// <summary>Revalidates a stored rule under the current versioned policy.</summary>
public interface IValidatedPermissionRulePolicy
{
    bool MatchesStoredDecision(PreparedPermissionInput input,
        PermissionEvaluationContext context, PermissionDecisionRecord record);
}

/// <summary>Generated activation metadata; no reflection or custom interaction protocol.</summary>
public sealed record AIFunctionPermissionDescriptor
{
    public required string DescriptorId { get; init; }
    /// <summary>Gets the policy author's stable semantic version, required for reusable approvals.</summary>
    public string? SemanticVersion { get; init; }
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
public enum PersistenceUnavailableReason { NoStableAgentId, NoSessionStore, NoPolicyVersion, NoContractIdentity }
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

/// <summary>Execution and input-preparation facts presented to a permission approver.</summary>
/// <param name="Mode">The resolved execution mode.</param>
/// <param name="InputContractKind">The owner of the input contract.</param>
/// <param name="Validation">The checks completed before authorization.</param>
/// <param name="IngressProvenance">Whether the original JSON was retained.</param>
public sealed record PermissionInvocationFacts(
    [property: JsonConverter(typeof(AgentInvocationModeJsonConverter))] AgentInvocationMode Mode,
    [property: JsonConverter(typeof(JsonStringEnumConverter<FunctionInputContractKind>))] FunctionInputContractKind InputContractKind,
    [property: JsonConverter(typeof(JsonStringEnumConverter<PermissionInputValidation>))] PermissionInputValidation Validation,
    [property: JsonConverter(typeof(JsonStringEnumConverter<FunctionArgumentIngressProvenance>))] FunctionArgumentIngressProvenance IngressProvenance);

/// <summary>The minimal permission prompt exposed to ordinary hosts.</summary>
public sealed record PermissionPrompt(
    string PermissionId,
    string CallId,
    string FunctionName,
    string? Action,
    JsonElement Arguments,
    string? Reason,
    PermissionInvocationFacts? Invocation);

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
            request.Reason,
            request.Invocation);
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

/// <summary>Describes the level of input checking performed before permission evaluation.</summary>
public enum PermissionInputValidation
{
    /// <summary>No input-validation guarantee was established.</summary>
    None,
    /// <summary>HPD validated the input against its canonical contract.</summary>
    HpdCanonical,
    /// <summary>HPD checked structural bounds without validating the source schema.</summary>
    StructuralOnly
}

/// <summary>Provides a detached view of the effective arguments and their preparation provenance.</summary>
public sealed class PreparedPermissionInput
{
    internal PreparedPermissionInput(
        JsonElement effectiveArguments,
        ResolvedFunctionInvocation invocation)
    {
        EffectiveArguments = effectiveArguments.Clone();
        Invocation = invocation ?? throw new ArgumentNullException(nameof(invocation));
        Action = invocation.ValidatedAction;
    }

    /// <summary>Gets the detached effective argument document presented for authorization.</summary>
    public JsonElement EffectiveArguments { get; }

    /// <summary>Gets the authority that owns the input contract.</summary>
    public FunctionInputContractKind InputContractKind => Invocation.InputContractKind;

    /// <summary>Gets the level of input validation completed before policy evaluation.</summary>
    public PermissionInputValidation Validation => Invocation.InputValidation;

    /// <summary>Gets whether arguments came from original JSON or an already-parsed representation.</summary>
    public FunctionArgumentIngressProvenance IngressProvenance => Invocation.IngressProvenance;

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

    /// <summary>Reads one required top-level effective parameter value.</summary>
    public JsonElement GetRequiredValue(string jsonParameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonParameterName);
        if (EffectiveArguments.ValueKind != JsonValueKind.Object ||
            !EffectiveArguments.TryGetProperty(jsonParameterName, out var value))
            throw new InvalidOperationException($"Permission input '{jsonParameterName}' is missing.");
        return value.Clone();
    }

    /// <summary>Returns the compound action projection or fails for an ordinary function.</summary>
    public ValidatedFunctionAction RequireAction() => Action ??
        throw new InvalidOperationException("This function has no action input.");
}

/// <summary>Provides immutable invocation facts and scoped services to a permission policy.</summary>
public sealed class PermissionEvaluationContext : IHarnessConfigurationContext
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
    public required PreparedPermissionInput Input { get; init; }
    /// <summary>Gets the immutable run configuration.</summary>
    public required AgentRunConfig RunConfig { get; init; }
    /// <summary>Gets the capabilities resolved for the current turn.</summary>
    public Middleware.IRuntimeCapabilityRegistry? RuntimeCapabilities { get; init; }
    /// <summary>Gets settings for the harness owning the function.</summary>
    public HarnessConfigurationBinding? HarnessConfiguration { get; init; }

    /// <summary>Gets the effective settings for the harness owning the function, when available.</summary>
    public T? GetHarnessSettings<T>() where T : class =>
        HarnessConfiguration is { } binding &&
        binding.Execution.Values.TryGetValue(binding.Owner, out var value)
            ? value as T
            : null;
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
    /// <summary>Gets the execution mode authorized for this invocation.</summary>
    public required AgentInvocationMode Mode { get; init; }
    /// <summary>Gets the effective invocation policy authorized for this invocation.</summary>
    public required AgentInvocationModePolicy InvocationPolicy { get; init; }
    /// <summary>Gets the execution owner authorized for this invocation.</summary>
    public required AgentInvocationModeHandling InvocationHandling { get; init; }
    /// <summary>Gets the fingerprint of the registered function contract, when available.</summary>
    public required string? ContractFingerprint { get; init; }
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
