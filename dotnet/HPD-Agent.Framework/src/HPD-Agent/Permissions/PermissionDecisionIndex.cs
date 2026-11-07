using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace HPD.Agent.Permissions;

/// <summary>Indexes and validates approval data without owning persistence.</summary>
internal static class PermissionDecisionIndex
{
    internal const int MaximumRuleBytes = 16 * 1024;
    internal static readonly string StateKey = typeof(PermissionDecisionStateData).FullName!;

    internal static PermissionDecisionStateData Available(PermissionDecisionStateData? state)
    {
        if (state is null || state.Decisions.IsDefault) return new();
        var records = ImmutableArray.CreateBuilder<PermissionDecisionRecord>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in state.Decisions)
            if (IsValid(record) && ids.Add(record.RecordId)) records.Add(record);
        return new() { Decisions = records.ToImmutable() };
    }

    internal static PermissionDecisionStateData Apply(
        PermissionDecisionStateData? current, PermissionDecisionRecord record)
    {
        if (!IsValid(record))
            throw new InvalidOperationException("PermissionDecisionStateInvalid: invalid approval candidate.");
        var state = Available(current);
        var replacement = new PermissionDecisionStateData
        {
            Decisions = state.Decisions.Where(existing =>
                    existing.RecordId != record.RecordId &&
                    !SupersedesSessionDecision(record, existing))
                .Append(record).OrderBy(r => r.RecordId, StringComparer.Ordinal).ToImmutableArray()
        };
        return replacement;
    }

    private static bool SupersedesSessionDecision(
        PermissionDecisionRecord incoming,
        PermissionDecisionRecord existing) =>
        incoming.Persistence == PermissionPersistenceKind.Session &&
        existing.Persistence == PermissionPersistenceKind.Session &&
        string.Equals(incoming.Key.AgentId, existing.Key.AgentId, StringComparison.Ordinal) &&
        string.Equals(incoming.Key.Authority, existing.Key.Authority, StringComparison.Ordinal) &&
        string.Equals(incoming.Key.Action, existing.Key.Action, StringComparison.Ordinal) &&
        !string.Equals(incoming.Key.PolicyDescriptorId, existing.Key.PolicyDescriptorId, StringComparison.Ordinal);

    internal static bool IsValid(PermissionDecisionRecord? record)
    {
        try
        {
            if (record is null || string.IsNullOrWhiteSpace(record.PermissionId) ||
                Encoding.UTF8.GetByteCount(record.PermissionId) > 128 || record.Key is null ||
                record.CreatedAt == default) return false;
            if (record.RuleTypeId is { } ruleType && Encoding.UTF8.GetByteCount(ruleType) > 128)
                return false;
            if (record.CanonicalRule is { } rule && PermissionRecordIdentity.Canonicalize(rule).Length > MaximumRuleBytes)
                return false;
            return string.Equals(record.RecordId, PermissionRecordIdentity.Create(
                record.Key, record.Persistence, record.RuleTypeId, record.CanonicalRule), StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }
}
