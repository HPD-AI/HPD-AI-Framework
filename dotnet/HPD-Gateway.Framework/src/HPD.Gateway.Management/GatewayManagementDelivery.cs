using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using HPD.Base;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Yarp;

namespace HPD.Gateway.Management;

public sealed record GatewayDeliveryRunResult(
    int Examined,
    int Claimed,
    int Completed,
    int Pending,
    int Failed);

public interface IGatewayDeliveryCoordinator
{
    ValueTask<GatewayDeliveryRunResult> ReconcileOnceAsync(CancellationToken cancellationToken = default);
}

internal sealed class GatewayDeliveryCoordinator(
    IGatewayAuthorityRuntime authority,
    IBaseSessionFactory sessions,
    IGatewayNodeActivator activator,
    GatewayManagementOptions options,
    TimeProvider timeProvider) : IGatewayDeliveryCoordinator
{
    private readonly SemaphoreSlim _lease = new(1, 1);

    public async ValueTask<GatewayDeliveryRunResult> ReconcileOnceAsync(CancellationToken cancellationToken = default)
    {
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _lease.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BaseSession session = Session();
            BaseRecord<GatewayDeliveryOutboxItem>[] items = (await session
                .Collection(GatewayDeliveryOutboxItem.Collection)
                .Query().Take(options.MaximumTargets).ToArrayAsync(options.MaximumTargets, cancellationToken)
                .ConfigureAwait(false)).RequireValue();
            int claimed = 0, completed = 0, pending = 0, failed = 0;
            foreach (BaseRecord<GatewayDeliveryOutboxItem> item in items)
            {
                if (item.Value.State is GatewayDeliveryState.Completed or GatewayDeliveryState.TerminalFailure)
                    continue;
                if (item.Value.State == GatewayDeliveryState.RetryScheduled && item.Value.NextAttemptAt > timeProvider.GetUtcNow())
                    continue;
                if (item.Value.State == GatewayDeliveryState.Claimed && item.Value.ClaimExpiresAt > timeProvider.GetUtcNow())
                    continue;

                if (item.Value.State == GatewayDeliveryState.OutcomePersistencePending &&
                    item.Value.PendingOutcomeKind is { } storedKind &&
                    item.Value.PendingOutcomeCode is { } storedCode)
                {
                    bool stored = await PersistOutcome(
                        session, item, storedKind, storedCode, cancellationToken).ConfigureAwait(false);
                    if (stored)
                    {
                        if (storedKind == GatewayNodeOutcomeKind.ActiveAcknowledged) completed++;
                        else failed++;
                    }
                    else pending++;
                    continue;
                }

                string claimId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
                var claimedValue = item.Value with
                {
                    State = GatewayDeliveryState.Claimed,
                    ClaimId = claimId,
                    ClaimExpiresAt = timeProvider.GetUtcNow().AddSeconds(30),
                    AttemptCount = checked(item.Value.AttemptCount + 1),
                };
                BaseResult<BaseRecord<GatewayDeliveryOutboxItem>> claim = await session
                    .Collection(GatewayDeliveryOutboxItem.Collection)
                    .ReplaceAsync(item.Id, claimedValue, item.Revision, cancellationToken)
                    .ConfigureAwait(false);
                if (!claim.TryGetValue(out BaseRecord<GatewayDeliveryOutboxItem>? claimedRecord))
                    continue;
                claimed++;

                BaseRecord<GatewayActivationIntent> intent = (await session
                    .Collection(GatewayActivationIntent.Collection)
                    .GetAsync(RecordId.Create(claimedValue.ActivationIntentId), cancellationToken)
                    .ConfigureAwait(false)).RequireValue();
                BaseRecord<GatewayAcceptedRevision> revision = (await session
                    .Collection(GatewayAcceptedRevision.Collection)
                    .GetAsync(RecordId.Create(intent.Value.RevisionId), cancellationToken)
                    .ConfigureAwait(false)).RequireValue();
                GatewayNodeActivationResult node = await activator.ActivateAsync(new GatewayNodeActivationRequest(
                    new CandidateId(intent.Value.CandidateId),
                    intent.Value.AuthorityId,
                    intent.Value.AuthorityEpoch,
                    checked((ulong)intent.Value.AuthorityVersion),
                    ImmutableArray.Create(revision.Value.CanonicalConfigurationUtf8)), cancellationToken).ConfigureAwait(false);

                GatewayNodeOutcomeKind kind = Map(node);
                string code = node.Diagnostics.IsDefaultOrEmpty ? kind.ToString() : node.Diagnostics[0].Code;
                var pendingValue = claimedValue with
                {
                    State = GatewayDeliveryState.OutcomePersistencePending,
                    ClaimId = null,
                    ClaimExpiresAt = null,
                    PendingOutcomeKind = kind,
                    PendingOutcomeCode = code,
                };
                BaseResult<BaseRecord<GatewayDeliveryOutboxItem>> recorded = await session
                    .Collection(GatewayDeliveryOutboxItem.Collection)
                    .ReplaceAsync(claimedRecord!.Id, pendingValue, claimedRecord.Revision, cancellationToken)
                    .ConfigureAwait(false);
                if (!recorded.TryGetValue(out BaseRecord<GatewayDeliveryOutboxItem>? pendingRecord) ||
                    !await PersistOutcome(session, pendingRecord!, kind, code, cancellationToken).ConfigureAwait(false))
                {
                    pending++;
                    continue;
                }
                if (kind == GatewayNodeOutcomeKind.ActiveAcknowledged) completed++;
                else failed++;
            }
            return new(items.Length, claimed, completed, pending, failed);
        }
        finally { _lease.Release(); }
    }

    private static async ValueTask<bool> PersistOutcome(
        BaseSession session,
        BaseRecord<GatewayDeliveryOutboxItem> pending,
        GatewayNodeOutcomeKind kind,
        string code,
        CancellationToken cancellationToken)
    {
        BaseRecord<GatewayActivationIntent> intent = (await session
            .Collection(GatewayActivationIntent.Collection)
            .GetAsync(RecordId.Create(pending.Value.ActivationIntentId), cancellationToken)
            .ConfigureAwait(false)).RequireValue();
        RecordId outcomeId = OutcomeId(intent.Value, kind, code);
        var outcome = new GatewayNodeActivationOutcome
        {
            NamespaceId = intent.Value.NamespaceId,
            TargetNodeId = intent.Value.TargetNodeId,
            ActivationIntentId = pending.Value.ActivationIntentId,
            AuthorityId = intent.Value.AuthorityId,
            AuthorityEpoch = intent.Value.AuthorityEpoch,
            AuthorityVersion = intent.Value.AuthorityVersion,
            Kind = kind,
            Code = code,
        };
        GatewayDeliveryState finalState = kind == GatewayNodeOutcomeKind.PublicationIndeterminate
            ? GatewayDeliveryState.TerminalFailure
            : kind == GatewayNodeOutcomeKind.ActiveAcknowledged
                ? GatewayDeliveryState.Completed
                : GatewayDeliveryState.TerminalFailure;
        var finalValue = pending.Value with
        {
            State = finalState,
            PendingOutcomeKind = null,
            PendingOutcomeCode = null,
            NextAttemptAt = null,
        };
        byte[] fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"gateway.management.outcome.v1\n{outcomeId.Value}\n{kind}\n{code}"));
        BaseBatchBuilder batch = session.Atomic(BaseMutationRequestIdentity.Create(
            $"gateway:{intent.Value.NamespaceId}", "gateway.persist-node-outcome", outcomeId.Value,
            BaseMutationRequestFingerprint.Create(fingerprint)));
        batch.Create(GatewayNodeActivationOutcome.Collection, outcomeId, outcome);
        batch.Replace(GatewayDeliveryOutboxItem.Collection, pending.Id, finalValue, pending.Revision);
        return (await Commit(batch, outcomeId.Value, cancellationToken).ConfigureAwait(false)).IsAccepted;
    }

    private BaseSession Session() => sessions.For(new PrincipalContext
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectId = "hpd.gateway.delivery",
        AuthSource = GatewayManagementBasePolicy.TrustedSource,
    }, value => value.Mode = OperationMode.System);

    private static GatewayNodeOutcomeKind Map(GatewayNodeActivationResult result)
    {
        if (result.Publication is { State: var state })
        {
            return state switch
            {
                GatewayPublicationState.ActiveAcknowledged => GatewayNodeOutcomeKind.ActiveAcknowledged,
                GatewayPublicationState.Duplicate => GatewayNodeOutcomeKind.ActiveAcknowledged,
                GatewayPublicationState.PublicationIndeterminate => GatewayNodeOutcomeKind.PublicationIndeterminate,
                GatewayPublicationState.Superseded => GatewayNodeOutcomeKind.Superseded,
                GatewayPublicationState.Stale => GatewayNodeOutcomeKind.Stale,
                GatewayPublicationState.IdentityConflict => GatewayNodeOutcomeKind.Conflict,
                GatewayPublicationState.CanceledBeforePublish => GatewayNodeOutcomeKind.CanceledBeforePublish,
                _ => GatewayNodeOutcomeKind.RejectedBeforePublish,
            };
        }
        return result.Diagnostics.Any(static value => value.Code.Contains("canceled", StringComparison.Ordinal))
            ? GatewayNodeOutcomeKind.CanceledBeforePublish
            : GatewayNodeOutcomeKind.RejectedBeforePublish;
    }

    private static RecordId OutcomeId(GatewayActivationIntent intent, GatewayNodeOutcomeKind kind, string code) =>
        GatewayAuthorityRecordIds.CommandFact(
            "node-outcome", intent.NamespaceId, "persist-node-outcome", intent.CandidateId,
            intent.TargetNodeId, intent.AuthorityId, intent.AuthorityEpoch,
            intent.AuthorityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), kind.ToString(), code, "v1");

    private static async ValueTask<GatewayManagementCommandResult> Commit(
        BaseBatchBuilder batch, string id, CancellationToken cancellationToken)
    {
        BaseResult<BaseBatchResult> result = await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseBatchResult> failure)
            return new(GatewayManagementCommandState.Unavailable, failure.Error.Code);
        BaseBatchResult value = ((BaseSuccess<BaseBatchResult>)result).Value;
        return value.Outcome == BaseRecordBatchOutcome.Committed
            ? new(value.RequestDisposition == BaseMutationRequestDisposition.Duplicate
                ? GatewayManagementCommandState.Duplicate : GatewayManagementCommandState.Accepted,
                "management.outcome.persisted", id)
            : new(GatewayManagementCommandState.Unavailable, value.Error?.Code ?? "management.outcome.persistence-failed");
    }
}
