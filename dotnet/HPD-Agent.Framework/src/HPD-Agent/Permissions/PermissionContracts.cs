using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPD.Events;

namespace HPD.Agent.Permissions;

/// <summary>Evaluates one canonically validated function invocation before author CLR binding.</summary>
public interface IPermissionPolicy
{
    /// <summary>Evaluates the protected invocation and returns its server-owned choices.</summary>
    ValueTask<PermissionEvaluation> EvaluateAsync(
        PermissionEvaluationContext context,
        CancellationToken cancellationToken);
}

/// <summary>Validates policy-authored reusable rules against a new constructor-free invocation.</summary>
/// <remarks>
/// A <see cref="PermissionPersistenceKind.ValidatedRule"/> preference is never reusable unless the
/// exact policy revision implements this contract and accepts its generated rule type and payload.
/// </remarks>
public interface IValidatedPermissionRulePolicy
{
    /// <summary>Returns whether one canonical stored rule applies to the current input.</summary>
    bool MatchesValidatedRule(
        ValidatedPermissionInput input,
        PermissionEvaluationContext context,
        string ruleTypeId,
        JsonElement canonicalRule,
        PermissionDecisionKind storedDecision);
}

/// <summary>Base class for a policy that emits one declared presentation type.</summary>
/// <typeparam name="TPresentation">The source-generated permission presentation type.</typeparam>
public abstract class PermissionPolicy<TPresentation> : IPermissionPolicy
{
    async ValueTask<PermissionEvaluation> IPermissionPolicy.EvaluateAsync(
        PermissionEvaluationContext context,
        CancellationToken cancellationToken) =>
        await EvaluateAsync(context.Input, context, cancellationToken).ConfigureAwait(false);

    /// <summary>Evaluates a constructor-free validated permission input.</summary>
    public abstract ValueTask<PermissionEvaluation> EvaluateAsync(
        ValidatedPermissionInput input,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken);
}

/// <summary>Requests one normalized decision for a server-owned permission evaluation.</summary>
public interface IPermissionInteraction
{
    /// <summary>Requests and normalizes a decision.</summary>
    ValueTask<PermissionDecision> RequestAsync(
        PermissionInteractionContext context,
        PermissionEvaluation evaluation,
        CancellationToken cancellationToken);
}

/// <summary>Declares the exact durable request/response event pair owned by a custom interaction.</summary>
/// <typeparam name="TRequest">The correlation-bearing durable request event.</typeparam>
/// <typeparam name="TResponse">The matching durable response event.</typeparam>
public interface IPermissionInteractionEventContract<TRequest, TResponse> : IPermissionInteraction
    where TRequest : AgentEvent, IAgentRequestEvent<TResponse>
    where TResponse : AgentEvent, IAgentResponseEvent;

/// <summary>
/// Contains generated activation authority for one permission policy or interaction descriptor.
/// CLR types alone never authorize reflection-based activation.
/// </summary>
public sealed record AIFunctionPermissionDescriptor
{
    /// <summary>Gets the stable descriptor ID referenced by immutable declarations.</summary>
    public required string DescriptorId { get; init; }

    /// <summary>Gets the generated policy factory when this descriptor provides a policy.</summary>
    public Func<IServiceProvider, IPermissionPolicy>? PolicyFactory { get; init; }

    /// <summary>Gets the generated interaction factory when this descriptor provides an interaction.</summary>
    public Func<IServiceProvider, IPermissionInteraction>? InteractionFactory { get; init; }

    /// <summary>Gets the declared custom request event type, when the interaction owns a custom protocol.</summary>
    public Type? RequestEventType { get; init; }

    /// <summary>Gets the declared custom response event type, when the interaction owns a custom protocol.</summary>
    public Type? ResponseEventType { get; init; }

    /// <summary>Gets the generated descriptor for the policy's one legal presentation type.</summary>
    public PermissionPresentationDescriptor? Presentation { get; init; }
}

/// <summary>Contains generated type identity and serialization for one permission presentation.</summary>
public sealed record PermissionPresentationDescriptor
{
    /// <summary>Resolves exact non-reflection serializer metadata or fails closed.</summary>
    public static JsonTypeInfo RequireGeneratedJsonTypeInfo(
        JsonSerializerOptions options,
        Type presentationType,
        string presentationId)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(presentationType);
        var typeInfo = options.GetTypeInfo(presentationType);
        if (typeInfo.OriginatingResolver is DefaultJsonTypeInfoResolver)
            throw new InvalidOperationException(
                $"Permission presentation '{presentationId}' requires source-generated JSON metadata for '{presentationType.FullName}'.");
        return typeInfo;
    }

    /// <summary>Gets the stable wire identity declared by the presentation type.</summary>
    public required string PresentationId { get; init; }
    /// <summary>Gets the exact CLR type accepted from the policy.</summary>
    public required Type PresentationType { get; init; }
    /// <summary>Gets the exact source-generated serializer metadata used for this presentation.</summary>
    public required JsonTypeInfo TypeInfo { get; init; }
    /// <summary>Serializes only the exact generated presentation type.</summary>
    public required Func<object, JsonElement> Serialize { get; init; }
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
    /// <summary>Gets the effective permission scope.</summary>
    public required string Scope { get; init; }
    /// <summary>Gets the constructor-free validated input.</summary>
    public required ValidatedPermissionInput Input { get; init; }
    /// <summary>Gets the immutable run configuration.</summary>
    public required AgentRunConfig RunConfig { get; init; }
    /// <summary>Gets invocation-scoped services.</summary>
    public required IServiceProvider Services { get; init; }
}

/// <summary>Describes the interaction services and stable correlation identity.</summary>
public sealed class PermissionInteractionContext
{
    private readonly IPermissionRequestDispatcher _dispatcher;

    internal PermissionInteractionContext(IPermissionRequestDispatcher dispatcher) =>
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    /// <summary>Gets the server-generated permission request ID.</summary>
    public required string PermissionId { get; init; }
    /// <summary>Gets the exact function-call ID.</summary>
    public required string FunctionCallId { get; init; }
    /// <summary>Gets the model-facing function name.</summary>
    public required string FunctionName { get; init; }
    /// <summary>Gets the selected action discriminator.</summary>
    public string? Action { get; init; }
    /// <summary>Gets invocation-scoped services.</summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>Uses the execution-bound durable request-session lifecycle for a typed protocol.</summary>
    public ValueTask<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : AgentEvent, IAgentRequestEvent<TResponse>
        where TResponse : AgentEvent, IAgentResponseEvent =>
        _dispatcher.RequestAsync<TRequest, TResponse>(request, cancellationToken);
}

internal interface IPermissionRequestDispatcher
{
    ValueTask<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : AgentEvent, IAgentRequestEvent<TResponse>
        where TResponse : AgentEvent, IAgentResponseEvent;
}

/// <summary>Classifies the risk communicated by a permission evaluation.</summary>
public enum PermissionRisk
{
    /// <summary>Routine, narrowly bounded operation.</summary>
    Low,
    /// <summary>Operation requiring ordinary user review.</summary>
    Medium,
    /// <summary>Operation with substantial or difficult-to-reverse effects.</summary>
    High,
    /// <summary>Operation with exceptional destructive, financial, or security impact.</summary>
    Critical
}

/// <summary>Classifies one normalized permission decision.</summary>
public enum PermissionDecisionKind
{
    /// <summary>Authorizes the exact invocation represented by the evaluation.</summary>
    Allow,
    /// <summary>Rejects the invocation.</summary>
    Deny,
    /// <summary>Rejects execution and returns guidance to the agent.</summary>
    Feedback
}

/// <summary>Defines how a policy-authored decision may be remembered.</summary>
public enum PermissionPersistenceKind
{
    /// <summary>Does not retain the decision.</summary>
    None,
    /// <summary>Retains the decision for the exact structured session key.</summary>
    SessionKey,
    /// <summary>Retains the decision only for the matching request fingerprint.</summary>
    ExactRequest,
    /// <summary>Retains a generated, canonical, policy-validated rule.</summary>
    ValidatedRule
}

/// <summary>Contains the immutable server-owned result of policy evaluation.</summary>
public sealed record PermissionEvaluation
{
    /// <summary>Gets the stable policy ID.</summary>
    public required string PolicyId { get; init; }
    /// <summary>Gets the effective permission scope.</summary>
    public required string Scope { get; init; }
    /// <summary>Gets the safe request title.</summary>
    public required string Title { get; init; }
    /// <summary>Gets an optional safe request summary.</summary>
    public string? Summary { get; init; }
    /// <summary>Gets the evaluated risk.</summary>
    public required PermissionRisk Risk { get; init; }
    /// <summary>Gets the exact legal choice set.</summary>
    public required PermissionChoiceSet Choices { get; init; }
    /// <summary>Gets an optional exact-request fingerprint.</summary>
    public string? RequestFingerprint { get; init; }
    /// <summary>Gets the stable policy revision.</summary>
    public required string PolicyRevision { get; init; }
    /// <summary>Gets the descriptor-constrained typed presentation.</summary>
    public object? Presentation { get; init; }
}

/// <summary>Contains the exact legal choices for one permission evaluation.</summary>
public sealed record PermissionChoiceSet
{
    /// <summary>Gets the ordered immutable choice descriptors.</summary>
    public required IReadOnlyList<PermissionChoiceDescriptor> Items { get; init; }
}

/// <summary>Describes one server-authored permission choice.</summary>
public sealed record PermissionChoiceDescriptor
{
    /// <summary>Gets the stable choice ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the display label.</summary>
    public required string Label { get; init; }
    /// <summary>Gets the normalized decision represented by the choice.</summary>
    public required PermissionDecisionKind Decision { get; init; }
    /// <summary>Gets denial behavior when the choice denies execution.</summary>
    public PermissionDeniedBehavior DeniedBehavior { get; init; } = PermissionDeniedBehavior.InterruptTurn;
    /// <summary>Gets the policy-authored persistence proposal.</summary>
    public PermissionPersistenceProposal? Persistence { get; init; }
}

/// <summary>Describes one validated policy-authored persistence proposal.</summary>
public sealed record PermissionPersistenceProposal
{
    /// <summary>Gets the persistence kind.</summary>
    public required PermissionPersistenceKind Kind { get; init; }
    /// <summary>Gets optional invocation-specific resource narrowing.</summary>
    public string? ResourceScope { get; init; }
    /// <summary>Gets the exact request fingerprint for exact-request persistence.</summary>
    public string? RequestFingerprint { get; init; }
    /// <summary>Gets the optional expiration.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
    /// <summary>Gets the generated validated-rule type ID.</summary>
    public string? RuleTypeId { get; init; }
    /// <summary>Gets the canonical generated rule payload.</summary>
    public JsonElement? CanonicalRule { get; init; }
}

/// <summary>Contains the normalized interaction result.</summary>
public sealed record PermissionDecision
{
    /// <summary>Gets the normalized decision kind.</summary>
    public required PermissionDecisionKind Kind { get; init; }
    /// <summary>Gets the selected server-authored choice ID.</summary>
    public required string ChoiceId { get; init; }
    /// <summary>Gets an optional bounded reason.</summary>
    public string? Reason { get; init; }
    /// <summary>Gets optional user feedback.</summary>
    public string? Feedback { get; init; }
}

/// <summary>Contains one descriptor-constrained typed presentation payload.</summary>
public sealed record PermissionPresentationEnvelope(string PresentationId, JsonElement Payload);

/// <summary>Contains the safe serializable subset of a server-owned permission evaluation.</summary>
public sealed record PermissionEvaluationEnvelope
{
    /// <summary>Gets the stable policy ID.</summary>
    public required string PolicyId { get; init; }
    /// <summary>Gets the stable policy revision.</summary>
    public required string PolicyRevision { get; init; }
    /// <summary>Gets the structured permission identity.</summary>
    public required PermissionKey Key { get; init; }
    /// <summary>Gets the safe title.</summary>
    public required string Title { get; init; }
    /// <summary>Gets the optional safe summary.</summary>
    public string? Summary { get; init; }
    /// <summary>Gets the evaluated risk.</summary>
    public required PermissionRisk Risk { get; init; }
    /// <summary>Gets the legal choices.</summary>
    public required PermissionChoiceSet Choices { get; init; }
    /// <summary>Gets the exact request fingerprint when safe to disclose.</summary>
    public string? RequestFingerprint { get; init; }
    /// <summary>Gets an optional typed presentation envelope.</summary>
    public PermissionPresentationEnvelope? Presentation { get; init; }
}

/// <summary>Declares a stable wire identity for a permission presentation.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class PermissionPresentationAttribute(string id, Type? serializerContextType = null) : Attribute
{
    /// <summary>Gets the stable presentation ID.</summary>
    public string Id { get; } = string.IsNullOrWhiteSpace(id)
        ? throw new ArgumentException("A presentation ID is required.", nameof(id))
        : id;

    /// <summary>Gets the source-generated <see cref="JsonSerializerContext"/> that owns this presentation.</summary>
    public Type? SerializerContextType { get; } = serializerContextType;
}

/// <summary>Framework presentation used by the default permission policy.</summary>
[PermissionPresentation("hpd.permission.default")]
public sealed record DefaultPermissionPresentation;

/// <summary>Identifies the authority that issued an invocation-bound permission grant.</summary>
public enum PermissionGrantSource
{
    /// <summary>The user selected an allow choice for this invocation.</summary>
    UserDecision,
    /// <summary>A legal stored preference matched this invocation.</summary>
    StoredPreference,
    /// <summary>The host explicitly configured automatic approval.</summary>
    HostAutoApprove
}

/// <summary>Provides a structured immutable identity for permission lookup and persistence.</summary>
public sealed record PermissionKey(
    string FunctionName,
    string? Action,
    string Scope,
    string PolicyId,
    string PolicyRevision,
    string? ResourceScope = null);

/// <summary>Freezes the complete policy-visible authority for a protected invocation.</summary>
public sealed record PermissionAuthorityStamp
{
    /// <summary>Gets the detached canonical arguments approved by the policy.</summary>
    public required JsonElement CanonicalArguments { get; init; }
    /// <summary>Gets the effective immutable declaration.</summary>
    public required AIFunctionPermissionDeclaration Declaration { get; init; }
    /// <summary>Gets the policy ID.</summary>
    public required string PolicyId { get; init; }
    /// <summary>Gets the policy revision.</summary>
    public required string PolicyRevision { get; init; }
    /// <summary>Gets the evaluated request fingerprint.</summary>
    public string? RequestFingerprint { get; init; }
}

/// <summary>Represents permission approval bound to one exact function invocation.</summary>
public sealed record FunctionPermissionGrant
{
    /// <summary>Gets the request correlation ID, when interaction occurred.</summary>
    public string? PermissionId { get; init; }
    /// <summary>Gets the exact function-call ID.</summary>
    public required string FunctionCallId { get; init; }
    /// <summary>Gets the model-facing function name.</summary>
    public required string FunctionName { get; init; }
    /// <summary>Gets the selected compound action.</summary>
    public string? Action { get; init; }
    /// <summary>Gets the structured permission key.</summary>
    public required PermissionKey Key { get; init; }
    /// <summary>Gets the exact evaluated request fingerprint.</summary>
    public string? RequestFingerprint { get; init; }
    /// <summary>Gets the selected choice ID.</summary>
    public required string ChoiceId { get; init; }
    /// <summary>Gets the grant timestamp.</summary>
    public required DateTimeOffset GrantedAt { get; init; }
    /// <summary>Gets the grant source.</summary>
    public required PermissionGrantSource Source { get; init; }
    /// <summary>Gets the complete protected-input authority stamp.</summary>
    public required PermissionAuthorityStamp Authority { get; init; }
}
