using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Permissions;

namespace HPD.Agent;

/// <summary>Reusable approvals shared by the threads of one session.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[MiddlewareState(Persistent = true, Scope = StateScope.Session, Version = 1)]
public sealed record PermissionDecisionStateData
{
    /// <summary>Gets the approvals; denial and feedback are never stored.</summary>
    public ImmutableArray<PermissionDecisionRecord> Decisions { get; init; } = [];
}

/// <summary>Identifies an agent operation under one versioned policy.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PermissionDecisionKey(
    string AgentId,
    string Authority,
    string? Action,
    string PolicyDescriptorId);

/// <summary>An explicit user approval eligible for reuse after policy validation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PermissionDecisionRecord
{
    /// <summary>Gets the deterministic identity of this approval or rule.</summary>
    public required string RecordId { get; init; }
    /// <summary>Gets the user interaction that produced the approval.</summary>
    public required string PermissionId { get; init; }
    /// <summary>Gets the operation and policy approved by the user.</summary>
    public required PermissionDecisionKey Key { get; init; }
    /// <summary>Gets session or validated-rule persistence.</summary>
    public required PermissionPersistenceKind Persistence { get; init; }
    /// <summary>Gets the policy-owned validated rule type.</summary>
    public string? RuleTypeId { get; init; }
    /// <summary>Gets the detached validated rule document.</summary>
    public JsonElement? CanonicalRule { get; init; }
    /// <summary>Gets candidate creation time before prompting, not commit time.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
