using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using HPD.Base;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Core;

namespace HPD.Gateway.Management;

public sealed record GatewayManagementActor(
    string ActorId,
    string AuthenticationScheme,
    string AuthorizationPolicy);

public sealed record GatewayProvisionTargetCommand(
    string NamespaceId,
    string TargetNodeId,
    string IdempotencyKey,
    GatewayManagementActor Actor,
    string CorrelationId,
    string AuthorityEpoch);

public sealed record GatewayLocalProvisionTargetCommand(
    string NamespaceId,
    string TargetNodeId,
    string IdempotencyKey,
    GatewayManagementActor Actor,
    string CorrelationId);

public sealed record GatewaySubmitCommand(
    string NamespaceId,
    string TargetNodeId,
    string IdempotencyKey,
    GatewayManagementActor Actor,
    string CorrelationId,
    string SourceKind,
    string SourceId,
    string? Description,
    ImmutableArray<byte> Utf8Configuration,
    string? ExpectedDesiredStateToken = null,
    bool Activate = true,
    string? DerivedFromRevisionId = null);

public sealed record GatewayActivateRevisionCommand(
    string NamespaceId,
    string TargetNodeId,
    string RevisionId,
    string IdempotencyKey,
    GatewayManagementActor Actor,
    string CorrelationId,
    string? ExpectedDesiredStateToken = null);

public enum GatewayManagementCommandState : byte
{
    Accepted = 0,
    Duplicate = 1,
    Invalid = 2,
    Conflict = 3,
    OutcomeUnknown = 4,
    Unavailable = 5,
}

public sealed record GatewayManagementCommandResult(
    GatewayManagementCommandState State,
    string Code,
    string? OperationId = null,
    string? DesiredStateToken = null,
    ImmutableArray<GatewayNodeActivationDiagnostic> Diagnostics = default)
{
    public bool IsAccepted => State is GatewayManagementCommandState.Accepted or GatewayManagementCommandState.Duplicate;
}

public interface IGatewayManagementCommandCoordinator
{
    ValueTask<GatewayManagementCommandResult> ProvisionLocalTargetAsync(
        GatewayLocalProvisionTargetCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<GatewayManagementCommandResult> ProvisionTargetAsync(
        GatewayProvisionTargetCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<GatewayManagementCommandResult> SubmitAsync(
        GatewaySubmitCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<GatewayManagementCommandResult> ActivateRevisionAsync(
        GatewayActivateRevisionCommand command,
        CancellationToken cancellationToken = default);
}

internal sealed class GatewayManagementCommandCoordinator(
    IGatewayAuthorityRuntime authority,
    IBaseSessionFactory sessions,
    HostCapabilitySnapshot gatewayCapabilities,
    GatewayManagementOptions options) : IGatewayManagementCommandCoordinator
{
    private const string ContractVersion = "gateway.management.command.v1";
    private const string EpochReservationContractVersion = "gateway.management.epoch-reservation.v1";
    private const int MaximumTargetEpochReservations = 4_096;
    private readonly SemaphoreSlim _commands = new(1, 1);

    public async ValueTask<GatewayManagementCommandResult> ProvisionLocalTargetAsync(
        GatewayLocalProvisionTargetCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommon(command.NamespaceId, command.TargetNodeId, command.IdempotencyKey, command.Actor, command.CorrelationId);
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _commands.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BaseSession session = Session(command.Actor, command.NamespaceId, command.CorrelationId);
            RecordId productReceiptId = GatewayAuthorityRecordIds.CommandFact(
                "receipt", command.NamespaceId, "provision-target", command.IdempotencyKey, command.TargetNodeId, ContractVersion);
            bool productReceiptExists = (await session.Collection(GatewayCommandReceipt.Collection)
                .GetAsync(productReceiptId, cancellationToken).ConfigureAwait(false)).TryGetValue(out _);
            EpochReservationResolution reservation = await ResolveTargetEpochReservationAsync(
                session, command.TargetNodeId, allowCreate: !productReceiptExists, cancellationToken).ConfigureAwait(false);
            if (!reservation.IsResolved)
                return new(reservation.State, reservation.Code);

            return await ProvisionTargetCoreAsync(new GatewayProvisionTargetCommand(
                command.NamespaceId,
                command.TargetNodeId,
                command.IdempotencyKey,
                command.Actor,
                command.CorrelationId,
                reservation.AuthorityEpoch!), cancellationToken).ConfigureAwait(false);
        }
        finally { _commands.Release(); }
    }

    public async ValueTask<GatewayManagementCommandResult> ProvisionTargetAsync(
        GatewayProvisionTargetCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommon(command.NamespaceId, command.TargetNodeId, command.IdempotencyKey, command.Actor, command.CorrelationId);
        Validate(command.AuthorityEpoch, nameof(command.AuthorityEpoch));
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _commands.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ProvisionTargetCoreAsync(command, cancellationToken).ConfigureAwait(false);
        }
        finally { _commands.Release(); }
    }

    private async ValueTask<GatewayManagementCommandResult> ProvisionTargetCoreAsync(
        GatewayProvisionTargetCommand command,
        CancellationToken cancellationToken)
    {
            RecordId ownershipId = GatewayAuthorityRecordIds.TargetOwnership(options.ManagementAuthorityId, command.TargetNodeId);
            RecordId deliveryId = GatewayAuthorityRecordIds.NodeDeliveryAuthority(options.ManagementAuthorityId, command.TargetNodeId);
            byte[] fingerprint = Fingerprint(
                "provision-target", command.NamespaceId, command.TargetNodeId,
                command.Actor.ActorId, command.Actor.AuthenticationScheme,
                command.Actor.AuthorizationPolicy, command.CorrelationId,
                command.AuthorityEpoch);
            BaseSession session = Session(command.Actor, command.NamespaceId, command.CorrelationId);
            var identity = BaseMutationRequestIdentity.Create(
                $"gateway:{command.NamespaceId}:{command.TargetNodeId}", "gateway.provision-target", command.IdempotencyKey,
                BaseMutationRequestFingerprint.Create(fingerprint));
            BaseBatchBuilder batch = session.Atomic(identity);
            batch.Create(GatewayTargetOwnership.Collection, ownershipId, new GatewayTargetOwnership
            {
                ManagementAuthorityId = options.ManagementAuthorityId,
                TargetNodeId = command.TargetNodeId,
                NamespaceId = command.NamespaceId,
            });
            batch.Create(GatewayNodeDeliveryAuthorityState.Collection, deliveryId, new GatewayNodeDeliveryAuthorityState
            {
                ManagementAuthorityId = options.ManagementAuthorityId,
                TargetNodeId = command.TargetNodeId,
                NamespaceId = command.NamespaceId,
                AuthorityId = DeriveText("authority", options.ManagementAuthorityId, command.NamespaceId, command.TargetNodeId),
                AuthorityEpoch = command.AuthorityEpoch,
                NextAuthorityVersion = 1,
            });
            RecordId auditId = GatewayAuthorityRecordIds.CommandFact("provision-audit", command.NamespaceId, "provision-target", command.IdempotencyKey, command.TargetNodeId, ContractVersion);
            batch.Create(GatewayAdministrativeAuditRecord.Collection, auditId, Audit(
                command.NamespaceId, command.Actor, "provision-target", "accepted", command.CorrelationId, command.TargetNodeId));
            RecordId receiptId = GatewayAuthorityRecordIds.CommandFact("receipt", command.NamespaceId, "provision-target", command.IdempotencyKey, command.TargetNodeId, ContractVersion);
            batch.Create(GatewayCommandReceipt.Collection, receiptId, Receipt(
                command.NamespaceId, command.TargetNodeId, "provision-target", command.IdempotencyKey, fingerprint, "provisioned", ownershipId.Value));
            return await CommitAsync(batch, ownershipId.Value, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<EpochReservationResolution> ResolveTargetEpochReservationAsync(
        BaseSession session,
        string targetNodeId,
        bool allowCreate,
        CancellationToken cancellationToken)
    {
        RecordId reservationId = GatewayAuthorityRecordIds.TargetEpochReservation(options.ManagementAuthorityId, targetNodeId);
        RecordId receiptId = GatewayAuthorityRecordIds.TargetEpochReservationReceipt(options.ManagementAuthorityId, targetNodeId);
        EpochReservationResolution existing = await ReadTargetEpochReservationAsync(
            session, reservationId, receiptId, targetNodeId, cancellationToken).ConfigureAwait(false);
        if (existing.IsResolved || existing.State == GatewayManagementCommandState.Unavailable)
            return existing;
        if (!allowCreate)
            return new(false, null, GatewayManagementCommandState.Unavailable, "management.epoch-reservation.missing");

        BaseRecord<GatewayTargetEpochReservation>[] reservations = (await session
            .Collection(GatewayTargetEpochReservation.Collection).Query()
            .Take(MaximumTargetEpochReservations + 1)
            .ToArrayAsync(MaximumTargetEpochReservations + 1, cancellationToken)
            .ConfigureAwait(false)).RequireValue();
        if (reservations.Length >= MaximumTargetEpochReservations)
            return new(false, null, GatewayManagementCommandState.Unavailable, "management.epoch-reservation.capacity-exhausted");

        string attemptId = DeriveSecret("epoch-attempt", targetNodeId);
        string epoch = DeriveSecret("authority-epoch", targetNodeId);
        string epochDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(epoch)));
        byte[] fingerprint = Fingerprint(
            "reserve-target-epoch", options.ManagementAuthorityId, targetNodeId, attemptId, epoch);
        var identity = BaseMutationRequestIdentity.Create(
            $"gateway-authority:{options.ManagementAuthorityId}",
            "gateway.reserve-target-epoch",
            attemptId,
            BaseMutationRequestFingerprint.Create(fingerprint));
        BaseBatchBuilder batch = session.Atomic(identity);
        batch.Create(GatewayTargetEpochReservation.Collection, reservationId, new GatewayTargetEpochReservation
        {
            ManagementAuthorityId = options.ManagementAuthorityId,
            TargetNodeId = targetNodeId,
            AuthorityEpoch = epoch,
            ContractVersion = EpochReservationContractVersion,
        });
        batch.Create(GatewayTargetEpochReservationReceipt.Collection, receiptId, new GatewayTargetEpochReservationReceipt
        {
            ReservationId = reservationId.Value,
            EpochDigest = epochDigest,
            StableResultCode = "management.epoch-reservation.committed",
            ContractVersion = EpochReservationContractVersion,
        });

        BaseResult<BaseBatchResult> committed = await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (committed is BaseFailure<BaseBatchResult> failure &&
            failure.Error.Code == BaseMutationRequestErrorCodes.OutcomeUnknown)
            return new(false, null, GatewayManagementCommandState.OutcomeUnknown, failure.Error.Code);

        EpochReservationResolution resolved = await ReadTargetEpochReservationAsync(
            session, reservationId, receiptId, targetNodeId, cancellationToken).ConfigureAwait(false);
        if (resolved.IsResolved)
            return resolved;
        if (committed is BaseFailure<BaseBatchResult> failed)
            return new(false, null,
                failed.Status == OperationStatus.Conflict
                    ? GatewayManagementCommandState.Conflict
                    : GatewayManagementCommandState.Unavailable,
                failed.Error.Code);
        return new(false, null, GatewayManagementCommandState.Unavailable,
            ((BaseSuccess<BaseBatchResult>)committed).Value.Error?.Code ?? "management.epoch-reservation.rolled-back");
    }

    private async ValueTask<EpochReservationResolution> ReadTargetEpochReservationAsync(
        BaseSession session,
        RecordId reservationId,
        RecordId receiptId,
        string targetNodeId,
        CancellationToken cancellationToken)
    {
        BaseResult<BaseRecord<GatewayTargetEpochReservation>> reservationResult = await session
            .Collection(GatewayTargetEpochReservation.Collection).GetAsync(reservationId, cancellationToken).ConfigureAwait(false);
        BaseResult<BaseRecord<GatewayTargetEpochReservationReceipt>> receiptResult = await session
            .Collection(GatewayTargetEpochReservationReceipt.Collection).GetAsync(receiptId, cancellationToken).ConfigureAwait(false);
        bool hasReservation = reservationResult.TryGetValue(out BaseRecord<GatewayTargetEpochReservation>? reservation);
        bool hasReceipt = receiptResult.TryGetValue(out BaseRecord<GatewayTargetEpochReservationReceipt>? receipt);
        if (!hasReservation && !hasReceipt)
            return new(false, null, GatewayManagementCommandState.Invalid, "management.epoch-reservation.absent");
        if (!hasReservation || !hasReceipt)
            return new(false, null, GatewayManagementCommandState.Unavailable, "management.epoch-reservation.corrupt");

        GatewayTargetEpochReservation value = reservation!.Value;
        GatewayTargetEpochReservationReceipt receiptValue = receipt!.Value;
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(value.AuthorityEpoch)));
        bool valid = StringComparer.Ordinal.Equals(value.ManagementAuthorityId, options.ManagementAuthorityId)
            && StringComparer.Ordinal.Equals(value.TargetNodeId, targetNodeId)
            && StringComparer.Ordinal.Equals(value.ContractVersion, EpochReservationContractVersion)
            && StringComparer.Ordinal.Equals(receiptValue.ReservationId, reservationId.Value)
            && StringComparer.Ordinal.Equals(receiptValue.EpochDigest, digest)
            && StringComparer.Ordinal.Equals(receiptValue.ContractVersion, EpochReservationContractVersion)
            && GatewayAuthorityRecordIds.IsCanonicalComponent(value.AuthorityEpoch);
        return valid
            ? new(true, value.AuthorityEpoch, GatewayManagementCommandState.Accepted, receiptValue.StableResultCode)
            : new(false, null, GatewayManagementCommandState.Unavailable, "management.epoch-reservation.corrupt");
    }

    private readonly record struct EpochReservationResolution(
        bool IsResolved,
        string? AuthorityEpoch,
        GatewayManagementCommandState State,
        string Code);

    public async ValueTask<GatewayManagementCommandResult> SubmitAsync(
        GatewaySubmitCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommon(command.NamespaceId, command.TargetNodeId, command.IdempotencyKey, command.Actor, command.CorrelationId);
        Validate(command.SourceKind, nameof(command.SourceKind));
        Validate(command.SourceId, nameof(command.SourceId));
        if (command.DerivedFromRevisionId is not null)
            Validate(command.DerivedFromRevisionId, nameof(command.DerivedFromRevisionId));
        if (command.Utf8Configuration.IsDefaultOrEmpty || command.Utf8Configuration.Length > options.MaximumCommandUtf8Bytes)
            return new(GatewayManagementCommandState.Invalid, "management.configuration.invalid");
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);

        GatewayCandidateReadResult candidate = GatewayCandidateReader.Read(command.Utf8Configuration.AsSpan(), gatewayCapabilities);
        if (!candidate.IsAccepted)
            return new(
                GatewayManagementCommandState.Invalid,
                "management.candidate.rejected",
                Diagnostics: candidate.Errors.Select(static error => new GatewayNodeActivationDiagnostic(
                    error.Code.ToString(), error.Path, error.Message)).ToImmutableArray());

        await _commands.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BaseSession session = Session(command.Actor, command.NamespaceId, command.CorrelationId);
            RecordId ownershipId = GatewayAuthorityRecordIds.TargetOwnership(options.ManagementAuthorityId, command.TargetNodeId);
            BaseResult<BaseRecord<GatewayTargetOwnership>> ownershipResult = await session
                .Collection(GatewayTargetOwnership.Collection).GetAsync(ownershipId, cancellationToken).ConfigureAwait(false);
            if (!ownershipResult.TryGetValue(out BaseRecord<GatewayTargetOwnership>? ownership) ||
                !StringComparer.Ordinal.Equals(ownership!.Value.NamespaceId, command.NamespaceId))
                return new(GatewayManagementCommandState.Conflict, "management.target.not-owned");

            RecordId deliveryId = GatewayAuthorityRecordIds.NodeDeliveryAuthority(options.ManagementAuthorityId, command.TargetNodeId);
            BaseRecord<GatewayNodeDeliveryAuthorityState> delivery = (await session
                .Collection(GatewayNodeDeliveryAuthorityState.Collection).GetAsync(deliveryId, cancellationToken).ConfigureAwait(false)).RequireValue();
            RecordId desiredId = GatewayAuthorityRecordIds.DesiredState(options.ManagementAuthorityId, command.TargetNodeId);
            BaseResult<BaseRecord<GatewayDesiredState>> desiredResult = await session
                .Collection(GatewayDesiredState.Collection).GetAsync(desiredId, cancellationToken).ConfigureAwait(false);
            BaseRecord<GatewayDesiredState>? desired = desiredResult.TryGetValue(out var found) ? found : null;

            GatewayCanonicalDocument canonical = candidate.CanonicalDocument!;
            string operation = command.Activate ? "submit-and-activate" : "submit-only";
            byte[] fingerprint = Fingerprint(
                operation, command.NamespaceId, command.TargetNodeId, command.Actor.ActorId,
                command.Actor.AuthenticationScheme, command.Actor.AuthorizationPolicy,
                command.CorrelationId, command.SourceKind, command.SourceId,
                command.Description ?? string.Empty, canonical.ContentHash.Algorithm,
                canonical.ContentHash.Value, command.ExpectedDesiredStateToken ?? string.Empty,
                command.DerivedFromRevisionId ?? string.Empty);
            RecordId receiptId = GatewayAuthorityRecordIds.CommandFact("receipt", command.NamespaceId, operation, command.IdempotencyKey, command.TargetNodeId, ContractVersion);
            BaseResult<BaseRecord<GatewayCommandReceipt>> priorReceiptResult = await session
                .Collection(GatewayCommandReceipt.Collection).GetAsync(receiptId, cancellationToken).ConfigureAwait(false);
            if (priorReceiptResult.TryGetValue(out BaseRecord<GatewayCommandReceipt>? priorReceipt))
            {
                if (!CryptographicOperations.FixedTimeEquals(fingerprint, priorReceipt!.Value.Fingerprint))
                    return new(GatewayManagementCommandState.Conflict, "management.idempotency-key.reused");
                return new(
                    GatewayManagementCommandState.Duplicate,
                    "management.duplicate",
                    priorReceipt.Value.StableOperationId,
                    priorReceipt.Value.StableDesiredStateToken);
            }
            if (command.Activate && !ValidateDesiredToken(command, desired))
                return new(GatewayManagementCommandState.Conflict, "management.desired-token.conflict");
            if (!command.Activate && command.ExpectedDesiredStateToken is not null)
                return new(GatewayManagementCommandState.Invalid, "management.desired-token.forbidden");
            var identity = BaseMutationRequestIdentity.Create(
                $"gateway:{command.NamespaceId}:{command.TargetNodeId}", $"gateway.{operation}", command.IdempotencyKey,
                BaseMutationRequestFingerprint.Create(fingerprint));
            RecordId revisionId = GatewayAuthorityRecordIds.CommandFact("revision", command.NamespaceId, operation, command.IdempotencyKey, command.TargetNodeId, ContractVersion);
            RecordId validationId = GatewayAuthorityRecordIds.CommandFact("validation", command.NamespaceId, operation, command.IdempotencyKey, command.TargetNodeId, ContractVersion);
            RecordId auditId = GatewayAuthorityRecordIds.CommandFact("acceptance-audit", command.NamespaceId, operation, command.IdempotencyKey, command.TargetNodeId, ContractVersion);
            RecordId intentId = GatewayAuthorityRecordIds.CommandFact("activation-intent", command.NamespaceId, operation, command.IdempotencyKey, command.TargetNodeId, ContractVersion);
            string candidateId = DeriveText("candidate", command.NamespaceId, revisionId.Value, canonical.ContentHash.Value);
            long assignedVersion = delivery.Value.NextAuthorityVersion;
            if (assignedVersion < 1 || assignedVersion == long.MaxValue)
                return new(GatewayManagementCommandState.Conflict, "management.authority-version.exhausted");

            BaseBatchBuilder batch = session.Atomic(identity);
            batch.Create(GatewayValidationRecord.Collection, validationId, new GatewayValidationRecord
            {
                NamespaceId = command.NamespaceId,
                TargetNodeId = command.TargetNodeId,
                Outcome = GatewayValidationOutcome.Valid,
                ContentHashValue = canonical.ContentHash.Value,
                DiagnosticsJson = "[]"u8.ToArray(),
                CorrelationId = command.CorrelationId,
            });
            batch.Create(GatewayAcceptedRevision.Collection, revisionId, new GatewayAcceptedRevision
            {
                NamespaceId = command.NamespaceId,
                TargetNodeId = command.TargetNodeId,
                ContentHashAlgorithm = canonical.ContentHash.Algorithm,
                ContentHashValue = canonical.ContentHash.Value,
                CanonicalConfigurationUtf8 = canonical.Utf8Json.AsSpan().ToArray(),
                SchemaVersion = "1.0",
                CanonicalizationVersion = "1",
                ParentRevisionId = desired?.Value.RevisionId,
                DerivedFromRevisionId = command.DerivedFromRevisionId,
                ValidationId = validationId.Value,
                ActorId = command.Actor.ActorId,
                SourceKind = command.SourceKind,
                SourceId = command.SourceId,
                CorrelationId = command.CorrelationId,
                Description = command.Description,
            });
            batch.Create(GatewayAdministrativeAuditRecord.Collection, auditId, Audit(
                command.NamespaceId, command.Actor, operation, "accepted", command.CorrelationId, revisionId.Value));

            if (!command.Activate)
            {
                batch.Create(GatewayCommandReceipt.Collection, receiptId, Receipt(
                    command.NamespaceId, command.TargetNodeId, operation, command.IdempotencyKey, fingerprint,
                    "accepted", revisionId.Value));
                return await CommitAsync(batch, revisionId.Value, cancellationToken).ConfigureAwait(false);
            }

            string selectedIntentId = intentId.Value;
            var nextDesired = new GatewayDesiredState
            {
                ManagementAuthorityId = options.ManagementAuthorityId,
                TargetNodeId = command.TargetNodeId,
                NamespaceId = command.NamespaceId,
                ActivationIntentId = selectedIntentId,
                RevisionId = revisionId.Value,
                CandidateId = candidateId,
            };
            string selectedDesiredToken = ProtectDesiredToken(nextDesired);
            BaseBatchItem<GatewayDesiredState> desiredItem = batch.Upsert(
                GatewayDesiredState.Collection, desiredId, nextDesired, nextDesired,
                desired is null ? RecordUpsertExistenceCondition.CreateOnly : RecordUpsertExistenceCondition.UpdateOnly,
                desired?.Revision);

            batch.Replace(GatewayNodeDeliveryAuthorityState.Collection, deliveryId, delivery.Value with
            {
                NextAuthorityVersion = assignedVersion + 1,
            }, delivery.Revision);
            batch.Create(GatewayActivationIntent.Collection, intentId, new GatewayActivationIntent
            {
                NamespaceId = command.NamespaceId,
                TargetNodeId = command.TargetNodeId,
                RevisionId = revisionId.Value,
                CandidateId = candidateId,
                ContentHashValue = canonical.ContentHash.Value,
                AuthorityId = delivery.Value.AuthorityId,
                AuthorityEpoch = delivery.Value.AuthorityEpoch,
                AuthorityVersion = assignedVersion,
            });
            RecordId outboxId = GatewayAuthorityRecordIds.CommandFact("outbox", command.NamespaceId, operation, command.IdempotencyKey, intentId.Value, command.TargetNodeId, ContractVersion);
            batch.Create(GatewayDeliveryOutboxItem.Collection, outboxId, new GatewayDeliveryOutboxItem
            {
                NamespaceId = command.NamespaceId,
                TargetNodeId = command.TargetNodeId,
                ActivationIntentId = intentId.Value,
                State = GatewayDeliveryState.Immediate,
                AttemptCount = 0,
            });

            batch.Create(GatewayCommandReceipt.Collection, receiptId, Receipt(
                command.NamespaceId, command.TargetNodeId, operation, command.IdempotencyKey, fingerprint,
                "accepted", revisionId.Value, selectedDesiredToken));
            return await CommitDesiredAsync(
                batch, desiredItem, command.NamespaceId, command.TargetNodeId,
                desiredId, revisionId.Value, selectedDesiredToken, cancellationToken).ConfigureAwait(false);
        }
        finally { _commands.Release(); }
    }

    public async ValueTask<GatewayManagementCommandResult> ActivateRevisionAsync(
        GatewayActivateRevisionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommon(command.NamespaceId, command.TargetNodeId, command.IdempotencyKey, command.Actor, command.CorrelationId);
        Validate(command.RevisionId, nameof(command.RevisionId));
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _commands.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BaseSession session = Session(command.Actor, command.NamespaceId, command.CorrelationId);
            RecordId ownershipId = GatewayAuthorityRecordIds.TargetOwnership(options.ManagementAuthorityId, command.TargetNodeId);
            BaseResult<BaseRecord<GatewayTargetOwnership>> ownershipResult = await session
                .Collection(GatewayTargetOwnership.Collection).GetAsync(ownershipId, cancellationToken).ConfigureAwait(false);
            if (!ownershipResult.TryGetValue(out BaseRecord<GatewayTargetOwnership>? ownership) ||
                !StringComparer.Ordinal.Equals(ownership!.Value.NamespaceId, command.NamespaceId))
                return new(GatewayManagementCommandState.Conflict, "management.target.not-owned");

            BaseResult<BaseRecord<GatewayAcceptedRevision>> revisionResult = await session
                .Collection(GatewayAcceptedRevision.Collection).GetAsync(RecordId.Create(command.RevisionId), cancellationToken).ConfigureAwait(false);
            if (!revisionResult.TryGetValue(out BaseRecord<GatewayAcceptedRevision>? revision) ||
                !StringComparer.Ordinal.Equals(revision!.Value.NamespaceId, command.NamespaceId) ||
                !StringComparer.Ordinal.Equals(revision.Value.TargetNodeId, command.TargetNodeId))
                return new(GatewayManagementCommandState.Invalid, "management.revision.not-found");

            RecordId deliveryId = GatewayAuthorityRecordIds.NodeDeliveryAuthority(options.ManagementAuthorityId, command.TargetNodeId);
            BaseRecord<GatewayNodeDeliveryAuthorityState> delivery = (await session
                .Collection(GatewayNodeDeliveryAuthorityState.Collection).GetAsync(deliveryId, cancellationToken).ConfigureAwait(false)).RequireValue();
            RecordId desiredId = GatewayAuthorityRecordIds.DesiredState(options.ManagementAuthorityId, command.TargetNodeId);
            BaseResult<BaseRecord<GatewayDesiredState>> desiredResult = await session
                .Collection(GatewayDesiredState.Collection).GetAsync(desiredId, cancellationToken).ConfigureAwait(false);
            BaseRecord<GatewayDesiredState>? desired = desiredResult.TryGetValue(out BaseRecord<GatewayDesiredState>? current) ? current : null;
            const string operation = "activate-existing";
            byte[] fingerprint = Fingerprint(
                operation, command.NamespaceId, command.TargetNodeId, command.RevisionId,
                command.Actor.ActorId, command.Actor.AuthenticationScheme, command.Actor.AuthorizationPolicy,
                command.CorrelationId, command.ExpectedDesiredStateToken ?? string.Empty);
            RecordId receiptId = GatewayAuthorityRecordIds.CommandFact("receipt", command.NamespaceId, operation, command.IdempotencyKey, command.TargetNodeId, ContractVersion);
            BaseResult<BaseRecord<GatewayCommandReceipt>> priorReceiptResult = await session
                .Collection(GatewayCommandReceipt.Collection).GetAsync(receiptId, cancellationToken).ConfigureAwait(false);
            if (priorReceiptResult.TryGetValue(out BaseRecord<GatewayCommandReceipt>? priorReceipt))
            {
                if (!CryptographicOperations.FixedTimeEquals(fingerprint, priorReceipt!.Value.Fingerprint))
                    return new(GatewayManagementCommandState.Conflict, "management.idempotency-key.reused");
                return new(GatewayManagementCommandState.Duplicate, "management.duplicate",
                    priorReceipt.Value.StableOperationId, priorReceipt.Value.StableDesiredStateToken);
            }
            if (!ValidateDesiredToken(command.ExpectedDesiredStateToken, desired))
                return new(GatewayManagementCommandState.Conflict, "management.desired-token.conflict");

            long assignedVersion = delivery.Value.NextAuthorityVersion;
            if (assignedVersion < 1 || assignedVersion == long.MaxValue)
                return new(GatewayManagementCommandState.Conflict, "management.authority-version.exhausted");
            RecordId intentId = GatewayAuthorityRecordIds.CommandFact(
                "activation-intent", command.NamespaceId, operation, command.IdempotencyKey, command.TargetNodeId, ContractVersion);
            string candidateId = DeriveText("candidate", command.NamespaceId, command.RevisionId, revision.Value.ContentHashValue);
            var nextDesired = new GatewayDesiredState
            {
                ManagementAuthorityId = options.ManagementAuthorityId,
                TargetNodeId = command.TargetNodeId,
                NamespaceId = command.NamespaceId,
                ActivationIntentId = intentId.Value,
                RevisionId = command.RevisionId,
                CandidateId = candidateId,
            };
            string selectedDesiredToken = ProtectDesiredToken(nextDesired);
            var identity = BaseMutationRequestIdentity.Create(
                $"gateway:{command.NamespaceId}:{command.TargetNodeId}", "gateway.activate-existing", command.IdempotencyKey,
                BaseMutationRequestFingerprint.Create(fingerprint));
            BaseBatchBuilder batch = session.Atomic(identity);
            batch.Replace(GatewayNodeDeliveryAuthorityState.Collection, deliveryId, delivery.Value with
            {
                NextAuthorityVersion = assignedVersion + 1,
            }, delivery.Revision);
            batch.Create(GatewayActivationIntent.Collection, intentId, new GatewayActivationIntent
            {
                NamespaceId = command.NamespaceId,
                TargetNodeId = command.TargetNodeId,
                RevisionId = command.RevisionId,
                CandidateId = candidateId,
                ContentHashValue = revision.Value.ContentHashValue,
                AuthorityId = delivery.Value.AuthorityId,
                AuthorityEpoch = delivery.Value.AuthorityEpoch,
                AuthorityVersion = assignedVersion,
            });
            RecordId outboxId = GatewayAuthorityRecordIds.CommandFact(
                "outbox", command.NamespaceId, operation, command.IdempotencyKey,
                intentId.Value, command.TargetNodeId, ContractVersion);
            batch.Create(GatewayDeliveryOutboxItem.Collection, outboxId, new GatewayDeliveryOutboxItem
            {
                NamespaceId = command.NamespaceId,
                TargetNodeId = command.TargetNodeId,
                ActivationIntentId = intentId.Value,
                State = GatewayDeliveryState.Immediate,
                AttemptCount = 0,
            });
            BaseBatchItem<GatewayDesiredState> desiredItem = batch.Upsert(
                GatewayDesiredState.Collection, desiredId, nextDesired, nextDesired,
                desired is null ? RecordUpsertExistenceCondition.CreateOnly : RecordUpsertExistenceCondition.UpdateOnly,
                desired?.Revision);
            RecordId auditId = GatewayAuthorityRecordIds.CommandFact(
                "acceptance-audit", command.NamespaceId, operation, command.IdempotencyKey, command.TargetNodeId, ContractVersion);
            batch.Create(GatewayAdministrativeAuditRecord.Collection, auditId, Audit(
                command.NamespaceId, command.Actor, operation, "accepted", command.CorrelationId, intentId.Value));
            batch.Create(GatewayCommandReceipt.Collection, receiptId, Receipt(
                command.NamespaceId, command.TargetNodeId, operation, command.IdempotencyKey, fingerprint,
                "accepted", intentId.Value, selectedDesiredToken));
            return await CommitDesiredAsync(batch, desiredItem, command.NamespaceId, command.TargetNodeId,
                desiredId, intentId.Value, selectedDesiredToken, cancellationToken).ConfigureAwait(false);
        }
        finally { _commands.Release(); }
    }

    private BaseSession Session(GatewayManagementActor actor, string namespaceId, string correlationId) =>
        sessions.For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectId = "hpd.gateway.management",
            AuthSource = GatewayManagementBasePolicy.TrustedSource,
        }, session =>
        {
            session.TenantId = namespaceId;
            session.CorrelationId = correlationId;
            session.Mode = OperationMode.Admin;
        });

    private static async ValueTask<GatewayManagementCommandResult> CommitAsync(
        BaseBatchBuilder batch,
        string operationId,
        CancellationToken cancellationToken)
    {
        BaseResult<BaseBatchResult> result = await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseBatchResult> failure)
        {
            GatewayManagementCommandState state = failure.Error.Code == BaseMutationRequestErrorCodes.OutcomeUnknown
                ? GatewayManagementCommandState.OutcomeUnknown
                : failure.Status == OperationStatus.Conflict
                    ? GatewayManagementCommandState.Conflict
                    : GatewayManagementCommandState.Unavailable;
            return new(state, failure.Error.Code);
        }
        BaseBatchResult committed = ((BaseSuccess<BaseBatchResult>)result).Value;
        if (committed.Outcome != BaseRecordBatchOutcome.Committed)
        {
            GatewayManagementCommandState failedState = committed.Outcome == BaseRecordBatchOutcome.RolledBack
                    || committed.Error?.Category == ErrorCategory.Conflict
                    ? GatewayManagementCommandState.Conflict
                    : GatewayManagementCommandState.Unavailable;
            return new(failedState, committed.Error?.Code ?? "management.batch.rolled-back");
        }
        return new(
            committed.RequestDisposition == BaseMutationRequestDisposition.Duplicate
                ? GatewayManagementCommandState.Duplicate
                : GatewayManagementCommandState.Accepted,
            committed.RequestDisposition == BaseMutationRequestDisposition.Duplicate ? "management.duplicate" : "management.accepted",
            operationId);
    }

    private async ValueTask<GatewayManagementCommandResult> CommitDesiredAsync(
        BaseBatchBuilder batch,
        BaseBatchItem<GatewayDesiredState> desiredItem,
        string namespaceId,
        string targetNodeId,
        RecordId desiredId,
        string operationId,
        string selectedDesiredToken,
        CancellationToken cancellationToken)
    {
        BaseResult<BaseBatchResult> result = await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseBatchResult> failure)
        {
            GatewayManagementCommandState state = failure.Error.Code == BaseMutationRequestErrorCodes.OutcomeUnknown
                ? GatewayManagementCommandState.OutcomeUnknown
                : failure.Status == OperationStatus.Conflict
                    ? GatewayManagementCommandState.Conflict
                    : GatewayManagementCommandState.Unavailable;
            return new(state, failure.Error.Code);
        }
        BaseBatchResult batchResult = ((BaseSuccess<BaseBatchResult>)result).Value;
        if (batchResult.Outcome != BaseRecordBatchOutcome.Committed)
            return new(
                batchResult.Outcome == BaseRecordBatchOutcome.RolledBack
                    ? GatewayManagementCommandState.Conflict
                    : GatewayManagementCommandState.Unavailable,
                batchResult.Error?.Code ?? "management.batch.rolled-back");
        BaseRecord<GatewayDesiredState> selected = batchResult.RequireCommitted().Record(desiredItem);
        return new(
            batchResult.RequestDisposition == BaseMutationRequestDisposition.Duplicate
                ? GatewayManagementCommandState.Duplicate
                : GatewayManagementCommandState.Accepted,
            batchResult.RequestDisposition == BaseMutationRequestDisposition.Duplicate
                ? "management.duplicate"
                : "management.accepted",
            operationId,
            selectedDesiredToken);
    }

    private bool ValidateDesiredToken(GatewaySubmitCommand command, BaseRecord<GatewayDesiredState>? desired)
        => ValidateDesiredToken(command.ExpectedDesiredStateToken, desired);

    private bool ValidateDesiredToken(string? expectedDesiredStateToken, BaseRecord<GatewayDesiredState>? desired)
    {
        if (desired is null)
            return expectedDesiredStateToken is null;
        return expectedDesiredStateToken is not null &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expectedDesiredStateToken),
                Encoding.ASCII.GetBytes(GatewayDesiredStateTokens.Create(desired.Value, options)));
    }

    private string ProtectDesiredToken(GatewayDesiredState desired) =>
        GatewayDesiredStateTokens.Create(desired, options);

    private static GatewayAdministrativeAuditRecord Audit(
        string namespaceId, GatewayManagementActor actor, string operation,
        string result, string correlation, string subject) => new()
    {
        NamespaceId = namespaceId,
        ActorId = actor.ActorId,
        AuthenticationScheme = actor.AuthenticationScheme,
        AuthorizationPolicy = actor.AuthorizationPolicy,
        Operation = operation,
        ResultCode = result,
        CorrelationId = correlation,
        SubjectId = subject,
    };

    private static GatewayCommandReceipt Receipt(
        string namespaceId, string targetNodeId, string operation, string key, byte[] fingerprint,
        string result, string operationId, string? desiredStateToken = null) => new()
    {
        NamespaceId = namespaceId,
        TargetNodeId = targetNodeId,
        Operation = operation,
        IdempotencyKey = key,
        Fingerprint = fingerprint,
        StableResultCode = result,
        StableOperationId = operationId,
        StableDesiredStateToken = desiredStateToken,
    };

    private static byte[] Fingerprint(params string[] values)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, ContractVersion);
        foreach (string value in values) Append(hash, value);
        return hash.GetHashAndReset();
    }

    private static string DeriveText(string purpose, params string[] values) =>
        "gwm-" + purpose + "-" + Convert.ToHexStringLower(Fingerprint([purpose, .. values]));

    private string DeriveSecret(string purpose, string targetNodeId)
    {
        using var hmac = new HMACSHA256(options.GetTokenKey());
        byte[] material = Encoding.UTF8.GetBytes(
            $"{EpochReservationContractVersion}\0{purpose}\0{options.ManagementAuthorityId}\0{targetNodeId}");
        return Convert.ToHexStringLower(hmac.ComputeHash(material).AsSpan(0, 16));
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value.Normalize(NormalizationForm.FormC));
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void ValidateCommon(
        string namespaceId, string targetNodeId, string idempotencyKey,
        GatewayManagementActor actor, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        Validate(namespaceId, nameof(namespaceId));
        Validate(targetNodeId, nameof(targetNodeId));
        Validate(idempotencyKey, nameof(idempotencyKey));
        Validate(actor.ActorId, nameof(actor.ActorId));
        Validate(actor.AuthenticationScheme, nameof(actor.AuthenticationScheme));
        Validate(actor.AuthorizationPolicy, nameof(actor.AuthorizationPolicy));
        Validate(correlationId, nameof(correlationId));
    }

    private static void Validate(string value, string parameter)
    {
        if (!GatewayAuthorityRecordIds.IsCanonicalComponent(value))
            throw new ArgumentException("Management command identity is invalid.", parameter);
    }
}
