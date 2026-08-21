using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal sealed record BaseInstalledSubjectRetirementConsumer(
    BaseSubjectRetirementConsumerDefinition Definition,
    string Checksum,
    BaseInstalledSubjectLifecycleConsumer Lifecycle);

internal sealed record BaseInstalledSubjectRetirementPolicy(
    BaseSubjectRetirementPolicy Definition,
    ImmutableArray<BaseInstalledSubjectRetirementConsumer> RequiredConsumers);

internal sealed class BaseSubjectRetirementRegistry
{
    private readonly Dictionary<(string Id, int Version), BaseInstalledSubjectRetirementConsumer> _consumers = [];
    private readonly Dictionary<(string Id, int Version), BaseInstalledSubjectRetirementPolicy> _policies = [];

    internal BaseSubjectRetirementRegistry(
        IEnumerable<BaseSubjectRetirementConsumerDefinition> consumers,
        IEnumerable<BaseSubjectRetirementPolicy> policies,
        BaseSubjectLifecycleRegistry lifecycle)
    {
        foreach (BaseSubjectRetirementConsumerDefinition candidate in consumers)
        {
            BaseSubjectRetirementConsumerDefinition normalized = Normalize(candidate);
            BaseInstalledSubjectLifecycleConsumer installedLifecycle = lifecycle.All.SingleOrDefault(value =>
                value.Definition.Id == normalized.ConsumerId && value.Definition.Version == normalized.ConsumerVersion)
                ?? throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.RegistrationConflict);
            if (installedLifecycle.Definition.OwningModuleId != normalized.OwningModuleId
                || installedLifecycle.Definition.Audience != normalized.Audience
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(installedLifecycle.Checksum),
                    Encoding.ASCII.GetBytes(normalized.LifecycleConsumerChecksum)))
                throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.RegistrationConflict);
            string checksum = ConsumerChecksum(normalized);
            if (!_consumers.TryAdd((normalized.ConsumerId, normalized.ConsumerVersion), new(normalized, checksum, installedLifecycle)))
                throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.RegistrationConflict);
        }

        foreach (BaseSubjectRetirementPolicy candidate in policies)
        {
            BaseSubjectRetirementPolicy normalized = NormalizePolicy(candidate);
            var accepted = ImmutableArray.CreateBuilder<BaseInstalledSubjectRetirementConsumer>(normalized.AcceptedConsumers.Length);
            foreach (BaseAcceptedRetirementConsumer declaration in normalized.AcceptedConsumers)
            {
                if (!_consumers.TryGetValue((declaration.ConsumerId, declaration.ConsumerVersion), out BaseInstalledSubjectRetirementConsumer? consumer)
                    || consumer.Definition.Participation != BaseSubjectRetirementParticipation.RequiredBeforePurge
                    || !AcceptedMatches(declaration, consumer))
                    throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.RegistrationConflict);
                accepted.Add(consumer);
            }
            string checksum = PolicyChecksum(normalized with { PolicyChecksum = string.Empty });
            if (!ChecksumEquals(normalized.PolicyChecksum, checksum))
                throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ContractInvalid);
            if (!_policies.TryAdd((normalized.ContractId, normalized.ContractVersion), new(normalized, accepted.MoveToImmutable())))
                throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.RegistrationConflict);
        }
    }

    internal IReadOnlyCollection<BaseInstalledSubjectRetirementConsumer> Consumers => _consumers.Values;
    internal IReadOnlyCollection<BaseInstalledSubjectRetirementPolicy> Policies => _policies.Values;
    internal BaseInstalledSubjectRetirementPolicy? FindPolicy(string id, int version) => _policies.GetValueOrDefault((id, version));
    internal BaseInstalledSubjectRetirementConsumer? FindConsumer(string id, int version) => _consumers.GetValueOrDefault((id, version));

    internal static BaseSubjectRetirementConsumerDefinition Normalize(BaseSubjectRetirementConsumerDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateId(value.ConsumerId); ValidateId(value.OwningModuleId); ValidateId(value.RetirementProfileId); ValidateId(value.AcknowledgementGrantId);
        ValidateChecksum(value.LifecycleConsumerChecksum); ValidateChecksum(value.RetirementProfileChecksum);
        BaseSubjectRetirementConsumerLimits limits = value.Limits ?? throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ContractInvalid);
        if (value.ConsumerVersion < 1 || value.RetirementProfileVersion < 1 || !Enum.IsDefined(value.Audience)
            || value.Participation is BaseSubjectRetirementParticipation.ObserveOnly || !Enum.IsDefined(value.Participation)
            || limits.MaximumAcknowledgementsPerCommit is < 1 or > 256
            || limits.MaximumAcknowledgementRequestBytes is < 1 or > 1_048_576
            || limits.MaximumReceiptBytes is < 1 or > 1_048_576
            || limits.AcknowledgementTimeout < TimeSpan.FromMilliseconds(1) || limits.AcknowledgementTimeout > TimeSpan.FromSeconds(30)
            || limits.ReceiptResolutionTimeout < TimeSpan.FromMilliseconds(1) || limits.ReceiptResolutionTimeout > TimeSpan.FromSeconds(30))
            throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ContractInvalid);
        return value with
        {
            ConsumerId = Copy(value.ConsumerId),
            OwningModuleId = Copy(value.OwningModuleId),
            LifecycleConsumerChecksum = Copy(value.LifecycleConsumerChecksum),
            RetirementProfileId = Copy(value.RetirementProfileId),
            RetirementProfileChecksum = Copy(value.RetirementProfileChecksum),
            AcknowledgementGrantId = Copy(value.AcknowledgementGrantId),
            Limits = limits with { },
        };
    }

    internal static BaseSubjectRetirementPolicy NormalizePolicy(BaseSubjectRetirementPolicy value)
    {
        ArgumentNullException.ThrowIfNull(value); ValidateId(value.ContractId); ValidateChecksum(value.PolicyChecksum);
        if (value.ContractVersion < 1 || !Enum.IsDefined(value.TimeoutBehavior)
            || value.CoordinationWindow < TimeSpan.FromMinutes(1) || value.CoordinationWindow > TimeSpan.FromDays(30)
            || value.PurgeRetention is null || value.PurgeRetention.MinimumTombstoneAge < TimeSpan.Zero || value.PurgeRetention.MinimumTombstoneAge > TimeSpan.FromDays(365)
            || value.AcceptedConsumers.IsDefault || value.AcceptedConsumers.Length > 32)
            throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ContractInvalid);
        ImmutableArray<BaseAcceptedRetirementConsumer> accepted = [.. value.AcceptedConsumers
            .Select(CloneAccepted).OrderBy(static item => item.ConsumerId, StringComparer.Ordinal).ThenBy(static item => item.ConsumerVersion)];
        if (accepted.GroupBy(static item => (item.ConsumerId, item.ConsumerVersion)).Any(static group => group.Count() != 1)
            || accepted.Any(static item => item.Participation != BaseSubjectRetirementParticipation.RequiredBeforePurge))
            throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.RegistrationConflict);
        return value with { ContractId = Copy(value.ContractId), AcceptedConsumers = accepted, PurgeRetention = value.PurgeRetention with { }, PolicyChecksum = Copy(value.PolicyChecksum) };
    }

    internal static string ConsumerChecksum(BaseSubjectRetirementConsumerDefinition value)
    {
        var writer = new ArrayBufferWriter<byte>(); Write(writer, "base.subjectRetirement.consumer.v1"); Write(writer, value.ConsumerId); Write(writer, value.ConsumerVersion);
        Write(writer, value.OwningModuleId); Write(writer, (int)value.Audience); Write(writer, value.LifecycleConsumerChecksum); Write(writer, value.RetirementProfileId);
        Write(writer, value.RetirementProfileVersion); Write(writer, value.RetirementProfileChecksum); Write(writer, (int)value.Participation); Write(writer, value.AcknowledgementGrantId);
        Write(writer, value.Limits.MaximumAcknowledgementsPerCommit); Write(writer, value.Limits.MaximumAcknowledgementRequestBytes); Write(writer, value.Limits.MaximumReceiptBytes);
        Write(writer, value.Limits.AcknowledgementTimeout.Ticks); Write(writer, value.Limits.ReceiptResolutionTimeout.Ticks);
        return Convert.ToHexStringLower(SHA256.HashData(writer.WrittenSpan));
    }

    internal static string PolicyChecksum(BaseSubjectRetirementPolicy value)
    {
        var writer = new ArrayBufferWriter<byte>(); Write(writer, "base.subjectRetirement.policy.v1"); Write(writer, value.ContractId); Write(writer, value.ContractVersion);
        Write(writer, value.AcceptedConsumers.Length);
        foreach (BaseAcceptedRetirementConsumer item in value.AcceptedConsumers) Write(writer, item.RetirementConsumerChecksum);
        Write(writer, value.CoordinationWindow.Ticks); Write(writer, (int)value.TimeoutBehavior); Write(writer, value.PurgeRetention.MinimumTombstoneAge.Ticks);
        return Convert.ToHexStringLower(SHA256.HashData(writer.WrittenSpan));
    }

    internal static string AcceptedSetChecksum(IEnumerable<BaseAcceptedRetirementConsumer> consumers)
    {
        var writer = new ArrayBufferWriter<byte>(); Write(writer, "base.subjectRetirement.acceptedSet.v1");
        BaseAcceptedRetirementConsumer[] values = [.. consumers.OrderBy(static item => item.ConsumerId, StringComparer.Ordinal).ThenBy(static item => item.ConsumerVersion)];
        Write(writer, values.Length); foreach (BaseAcceptedRetirementConsumer value in values) Write(writer, value.RetirementConsumerChecksum);
        return Convert.ToHexStringLower(SHA256.HashData(writer.WrittenSpan));
    }

    internal static string BarrierChecksum(BaseSubjectRetirementBarrier value, IEnumerable<string> acknowledgements)
    {
        var writer = new ArrayBufferWriter<byte>(); Write(writer, "base.subjectRetirement.barrier.v1"); Write(writer, value.ContractId); Write(writer, value.ContractVersion);
        Write(writer, value.SubjectId.Value); Write(writer, Convert.ToHexStringLower(value.AuthorityEpoch.ToArray())); Write(writer, Convert.ToHexStringLower(value.Incarnation.ToArray()));
        Write(writer, value.TombstoneSequence); Write(writer, value.RequiredConsumerSetChecksum); Write(writer, value.CreatedAtUtc.UtcTicks); Write(writer, value.DeadlineUtc.UtcTicks);
        Write(writer, (int)value.State); Write(writer, value.Generation);
        string[] values = [.. acknowledgements.Order(StringComparer.Ordinal)];
        Write(writer, values.Length); foreach (string item in values) Write(writer, item);
        return Convert.ToHexStringLower(SHA256.HashData(writer.WrittenSpan));
    }

    internal static string TerminalChecksum(BaseSubjectRetirementTerminalReceipt value)
    {
        var writer = new ArrayBufferWriter<byte>(); Write(writer, "base.subjectRetirement.terminal.v1"); Write(writer, (int)value.Scope.Kind);
        Write(writer, Convert.ToHexStringLower(value.Scope.IndexDigest)); Write(writer, Convert.ToHexStringLower(value.Scope.ProtectedCanonicalValue));
        Write(writer, value.ContractId); Write(writer, value.ContractVersion); Write(writer, value.SubjectId.Value);
        Write(writer, value.AuthorityEpoch.ToBase64Url()); Write(writer, value.Incarnation.ToBase64Url()); Write(writer, value.TombstoneSequence);
        Write(writer, (int)value.AuthorizingState); Write(writer, value.FinalBarrierGeneration); Write(writer, value.FinalBarrierChecksum);
        Write(writer, value.RequiredConsumerSetChecksum); Write(writer, value.RetiredPosition.Value); Write(writer, value.PurgedAtUtc.UtcTicks);
        BaseSubjectTerminalAcknowledgement[] acknowledgements = [.. value.Acknowledgements.OrderBy(static item => item.ConsumerId, StringComparer.Ordinal).ThenBy(static item => item.ConsumerVersion)];
        Write(writer, acknowledgements.Length); foreach (BaseSubjectTerminalAcknowledgement item in acknowledgements) { Write(writer, item.ConsumerId); Write(writer, item.ConsumerVersion); Write(writer, item.ConsumerChecksum); Write(writer, item.ThroughSubjectSequence); Write(writer, (int)item.Disposition); Write(writer, item.AcknowledgedPosition.Value); }
        return Convert.ToHexStringLower(SHA256.HashData(writer.WrittenSpan));
    }

    internal static string AcknowledgementChecksumInput(string consumerId, int consumerVersion, string consumerChecksum, long sequence, BaseSubjectAcknowledgementDisposition disposition, long position) =>
        $"{consumerId}\0{consumerVersion}\0{consumerChecksum}\0{sequence}\0{(int)disposition}\0{position}";

    internal static void ValidatePublication(BaseSubjectRetirementPublicationRow row)
    {
        ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(row.Fact);
        BaseSubjectRetirementPublicationFact fact = row.Fact;
        int payloads = (fact.Barrier is null ? 0 : 1) + (fact.AdvisoryAcknowledgement is null ? 0 : 1) + (fact.Purged is null ? 0 : 1) + (fact.ConsumerSet is null ? 0 : 1) + (fact.Restore is null ? 0 : 1);
        bool barrierKind = fact.Kind is BaseSubjectRetirementPublicationKind.BarrierCreated or BaseSubjectRetirementPublicationKind.RequiredAcknowledgementAccepted
            or BaseSubjectRetirementPublicationKind.BarrierSatisfied or BaseSubjectRetirementPublicationKind.BarrierTimedOut
            or BaseSubjectRetirementPublicationKind.BarrierQuarantined or BaseSubjectRetirementPublicationKind.BarrierOverridden;
        (string contractId, int contractVersion) = PublicationIdentity(fact);
        string expectedAction = PublicationAuditAction(fact.Kind);
        string expectedEvent = $"subject-retirement:{fact.Position.Value}";
        string expectedControl = PublicationControlChecksum(fact, row.Scope, expectedAction, expectedEvent, contractId, contractVersion);
        bool valid = payloads == 1 && Enum.IsDefined(fact.Kind)
            && fact.AuditAction == expectedAction && fact.InvalidationEventId == expectedEvent
            && fact.InvalidationContractId == contractId && fact.InvalidationContractVersion == contractVersion
            && fact.ControlChecksum == expectedControl
            && (barrierKind) == (fact.Barrier is not null)
            && (fact.Kind == BaseSubjectRetirementPublicationKind.AdvisoryAcknowledgementAccepted) == (fact.AdvisoryAcknowledgement is not null)
            && (fact.Kind == BaseSubjectRetirementPublicationKind.SubjectPurged) == (fact.Purged is not null)
            && (fact.Kind == BaseSubjectRetirementPublicationKind.ConsumerSetChanged) == (fact.ConsumerSet is not null)
            && (fact.Kind == BaseSubjectRetirementPublicationKind.RestoreTransformed) == (fact.Restore is not null)
            && (barrierKind || fact.AdvisoryAcknowledgement is not null || fact.Purged is not null) == (row.Scope is not null);
        if (fact.Barrier is { } barrier)
        {
            bool consumerRequired = fact.Kind is BaseSubjectRetirementPublicationKind.RequiredAcknowledgementAccepted or BaseSubjectRetirementPublicationKind.BarrierSatisfied;
            valid &= (barrier.ConsumerId is not null) == consumerRequired && barrier.ContractVersion > 0 && barrier.TombstoneSequence > 0
                && barrier.PreviousGeneration >= 0 && barrier.PublishedGeneration > 0 && barrier.PublishedGeneration == checked(barrier.PreviousGeneration + 1);
        }
        if (!valid) throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
    }

    internal static BaseSubjectRetirementPublicationFact SealPublication(BaseSubjectRetirementPublicationFact fact, BaseProtectedSubjectScope? scope = null)
    {
        (string contractId, int contractVersion) = PublicationIdentity(fact);
        string action = PublicationAuditAction(fact.Kind);
        string eventId = $"subject-retirement:{fact.Position.Value}";
        return fact with
        {
            AuditAction = action,
            InvalidationEventId = eventId,
            InvalidationContractId = contractId,
            InvalidationContractVersion = contractVersion,
            ControlChecksum = PublicationControlChecksum(fact, scope, action, eventId, contractId, contractVersion),
        };
    }

    private static (string ContractId, int ContractVersion) PublicationIdentity(BaseSubjectRetirementPublicationFact fact) =>
        fact.Barrier is { } b ? (b.ContractId, b.ContractVersion) : fact.AdvisoryAcknowledgement is { } a ? (a.ContractId, a.ContractVersion) : fact.Purged is { } p ? (p.ContractId, p.ContractVersion) : fact.ConsumerSet is { } c ? (c.ContractId, c.ContractVersion) : fact.Restore is { } r ? (r.ContractId, r.ContractVersion) : throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);

    private static string PublicationAuditAction(BaseSubjectRetirementPublicationKind kind) => kind switch
    {
        BaseSubjectRetirementPublicationKind.BarrierCreated => "base.subjectRetirement.barrier.created",
        BaseSubjectRetirementPublicationKind.RequiredAcknowledgementAccepted or BaseSubjectRetirementPublicationKind.AdvisoryAcknowledgementAccepted => "base.subjectRetirement.acknowledgement.accepted",
        BaseSubjectRetirementPublicationKind.BarrierSatisfied => "base.subjectRetirement.barrier.satisfied",
        BaseSubjectRetirementPublicationKind.BarrierTimedOut => "base.subjectRetirement.barrier.timedOut",
        BaseSubjectRetirementPublicationKind.BarrierQuarantined => "base.subjectRetirement.barrier.quarantined",
        BaseSubjectRetirementPublicationKind.BarrierOverridden => "base.subjectRetirement.barrier.overridden",
        BaseSubjectRetirementPublicationKind.SubjectPurged => "base.subjectRetirement.subject.purged",
        BaseSubjectRetirementPublicationKind.ConsumerSetChanged => "base.subjectRetirement.consumerRemoval.completed",
        BaseSubjectRetirementPublicationKind.RestoreTransformed => "base.subjectRetirement.restore.transformed",
        _ => throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid),
    };

    private static string PublicationControlChecksum(BaseSubjectRetirementPublicationFact fact, BaseProtectedSubjectScope? scope, string action, string eventId, string contractId, int contractVersion)
    {
        BaseSubjectRetirementPublicationFact payload = fact with { AuditAction = null, InvalidationEventId = null, InvalidationContractId = null, InvalidationContractVersion = 0, ControlChecksum = null };
        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, HPDBaseJsonSerializerContext.Default.BaseSubjectRetirementPublicationFact);
        byte[] header = Encoding.UTF8.GetBytes($"base.subjectRetirement.control.v1\0{fact.Position.Value}\0{(int)fact.Kind}\0{action}\0{eventId}\0{contractId}\0{contractVersion}\0{(scope is null ? -1 : (int)scope.Kind)}\0{Convert.ToHexString(scope?.IndexDigest ?? [])}\0{Convert.ToHexString(scope?.ProtectedCanonicalValue ?? [])}\0");
        byte[] authority = new byte[checked(header.Length + payloadBytes.Length)]; header.CopyTo(authority, 0); payloadBytes.CopyTo(authority, header.Length);
        return Convert.ToHexStringLower(SHA256.HashData(authority));
    }

    private static bool AcceptedMatches(BaseAcceptedRetirementConsumer a, BaseInstalledSubjectRetirementConsumer b) =>
        a.OwningModuleId == b.Definition.OwningModuleId && a.Audience == b.Definition.Audience && a.LifecycleConsumerChecksum == b.Definition.LifecycleConsumerChecksum
        && a.RetirementProfileId == b.Definition.RetirementProfileId && a.RetirementProfileVersion == b.Definition.RetirementProfileVersion
        && a.RetirementProfileChecksum == b.Definition.RetirementProfileChecksum && a.Participation == b.Definition.Participation
        && a.AcknowledgementGrantId == b.Definition.AcknowledgementGrantId && a.Limits == b.Definition.Limits && ChecksumEquals(a.RetirementConsumerChecksum, b.Checksum);

    private static BaseAcceptedRetirementConsumer CloneAccepted(BaseAcceptedRetirementConsumer value)
    {
        ValidateId(value.ConsumerId); ValidateId(value.OwningModuleId); ValidateId(value.RetirementProfileId); ValidateId(value.AcknowledgementGrantId);
        ValidateChecksum(value.LifecycleConsumerChecksum); ValidateChecksum(value.RetirementProfileChecksum); ValidateChecksum(value.RetirementConsumerChecksum);
        return value with { ConsumerId = Copy(value.ConsumerId), OwningModuleId = Copy(value.OwningModuleId), LifecycleConsumerChecksum = Copy(value.LifecycleConsumerChecksum), RetirementProfileId = Copy(value.RetirementProfileId), RetirementProfileChecksum = Copy(value.RetirementProfileChecksum), AcknowledgementGrantId = Copy(value.AcknowledgementGrantId), RetirementConsumerChecksum = Copy(value.RetirementConsumerChecksum), Limits = value.Limits with { } };
    }

    private static bool ChecksumEquals(string left, string right) => left.Length == right.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    private static string Copy(string value) => new(value.AsSpan());
    private static void ValidateId(string value) { try { BaseApplicationId.Validate(value, nameof(value)); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ContractInvalid, e); } }
    private static void ValidateChecksum(string value) { if (value is not { Length: 64 } || value.Any(static c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ContractInvalid); }
    private static void Write(ArrayBufferWriter<byte> writer, string value) { int count = Encoding.UTF8.GetByteCount(value); BinaryPrimitives.WriteInt32BigEndian(writer.GetSpan(4), count); writer.Advance(4); Encoding.UTF8.GetBytes(value, writer.GetSpan(count)); writer.Advance(count); }
    private static void Write(ArrayBufferWriter<byte> writer, int value) { BinaryPrimitives.WriteInt32BigEndian(writer.GetSpan(4), value); writer.Advance(4); }
    private static void Write(ArrayBufferWriter<byte> writer, long value) { BinaryPrimitives.WriteInt64BigEndian(writer.GetSpan(8), value); writer.Advance(8); }
}

internal static class BaseSubjectRetirementCapabilityContract
{
    internal static bool Supports(BaseSubjectRetirementRegistry registry, BaseSubjectRetirementCapability capability)
    {
        if (registry.Consumers.Count == 0 && registry.Policies.Count == 0) return true;
        if (!capability.TransactionalBarrierSupported || !capability.TransactionalFinalPurgeSupported
            || capability.MaximumPendingBarriers < 1 || capability.MaximumAdministrationPageSize < 256
            || capability.MaximumResultBytes < 1_048_576 || capability.MaximumRetirementProjectionsPerCommit < 256
            || capability.MaximumBarrierReadsPerCommit < 256 || capability.MaximumAcknowledgementReadsPerCommit < 256
            || capability.MaximumPublicationsPerCommit < 256 || capability.MaximumEvidenceBytes < 1_048_576
            || capability.MaximumPublicationBytes < 1_048_576 || capability.MaximumTransientBytes < 32_000_000
            || capability.MaximumAcquisitionTimeout < TimeSpan.FromSeconds(5) || capability.MaximumTransactionTimeout < TimeSpan.FromSeconds(30)
            || capability.MaximumCommitCompletionTimeout < TimeSpan.FromSeconds(30) || capability.MaximumReceiptResolutionTimeout < TimeSpan.FromSeconds(30)) return false;
        foreach (BaseInstalledSubjectRetirementPolicy policy in registry.Policies)
            if (policy.RequiredConsumers.Length > capability.MaximumRequiredConsumersPerContract
                || policy.RequiredConsumers.Length > capability.MaximumAcknowledgementReadsPerCommit
                || policy.Definition.CoordinationWindow > capability.MaximumCoordinationWindow) return false;
        foreach (BaseInstalledSubjectRetirementConsumer consumer in registry.Consumers)
            if (consumer.Definition.Limits.MaximumAcknowledgementsPerCommit > capability.MaximumAcknowledgementsPerCommit
                || consumer.Definition.Limits.MaximumAcknowledgementRequestBytes > capability.MaximumEvidenceBytes
                || consumer.Definition.Limits.MaximumReceiptBytes > capability.MaximumResultBytes
                || consumer.Definition.Limits.AcknowledgementTimeout > capability.MaximumTransactionTimeout
                || consumer.Definition.Limits.ReceiptResolutionTimeout > capability.MaximumReceiptResolutionTimeout) return false;
        return true;
    }
}

internal static class BaseSubjectRetirementErrorCodes
{
    internal const string CursorInvalid = "base.subjectRetirement.cursorInvalid";
    internal const string ContractInvalid = "base.subjectRetirement.contractInvalid";
    internal const string RegistrationConflict = "base.subjectRetirement.registrationConflict";
    internal const string Unauthorized = "base.subjectRetirement.unauthorized";
    internal const string ScopeAuthorityInvalid = "base.subjectRetirement.scopeAuthorityInvalid";
    internal const string ProviderContractInvalid = "base.subjectRetirement.providerContractInvalid";
    internal const string BarrierPending = "base.subjectRetirement.barrierPending";
    internal const string AcknowledgementConflict = "base.subjectRetirement.acknowledgementConflict";
    internal const string SequenceInvalid = "base.subjectRetirement.sequenceInvalid";
    internal const string BarrierSatisfied = "base.subjectRetirement.barrierSatisfied";
    internal const string BarrierTimedOut = "base.subjectRetirement.barrierTimedOut";
    internal const string BarrierQuarantined = "base.subjectRetirement.barrierQuarantined";
    internal const string OverrideConflict = "base.subjectRetirement.overrideConflict";
    internal const string PurgeConflict = "base.subjectRetirement.purgeConflict";
    internal const string RetentionPending = "base.subjectRetirement.retentionPending";
    internal const string ConsumerRemovalPending = "base.subjectRetirement.consumerRemovalPending";
    internal const string CapacityExceeded = "base.subjectRetirement.capacityExceeded";
    internal const string Timeout = "base.subjectRetirement.timeout";
    internal const string CommitIndeterminate = "base.subjectRetirement.commitIndeterminate";
    internal const string MaintenanceRequired = "base.subjectRetirement.maintenanceRequired";
}
