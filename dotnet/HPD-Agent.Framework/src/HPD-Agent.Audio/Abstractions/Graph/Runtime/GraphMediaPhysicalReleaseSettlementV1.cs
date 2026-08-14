using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal abstract record GraphMediaPhysicalReleaseSettlementResultV1
{
    private GraphMediaPhysicalReleaseSettlementResultV1() { }
    internal sealed record Settled(AuthorityFactEnvelopeV1 Envelope, CapacityGrantSnapshotV1 Grant) : GraphMediaPhysicalReleaseSettlementResultV1;
    internal sealed record RetryRequired(long ObservedHead) : GraphMediaPhysicalReleaseSettlementResultV1;
    internal sealed record StoreUnavailable(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseSettlementResultV1;
    internal sealed record Quarantined(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseSettlementResultV1;
}

internal sealed class GraphMediaPhysicalReleaseSettlementCoordinatorV1
{
    private const ushort MaximumReadItems = AppendAuthorityBatchV1.MaximumItems;
    private const uint MaximumReadBytes = ProposedAuthorityFactV1.MaximumPayloadBytes;
    private readonly IAuthorityJournalV1 _releaseJournal;
    private readonly IAuthorityJournalV1 _capacityJournal;
    private readonly AuthorityPayloadAdmissionRegistryV1 _releaseRegistry;

    internal GraphMediaPhysicalReleaseSettlementCoordinatorV1(IAuthorityJournalV1 releaseJournal,
        IAuthorityJournalV1 capacityJournal, AuthorityPayloadAdmissionRegistryV1 releaseRegistry)
    {
        _releaseJournal = releaseJournal ?? throw new ArgumentNullException(nameof(releaseJournal));
        _capacityJournal = capacityJournal ?? throw new ArgumentNullException(nameof(capacityJournal));
        _releaseRegistry = releaseRegistry ?? throw new ArgumentNullException(nameof(releaseRegistry));
    }

    internal async ValueTask<GraphMediaPhysicalReleaseSettlementResultV1> SettleAsync(
        GraphMediaPhysicalReleaseFoldResultV1.Released claimed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimed);
        cancellationToken.ThrowIfCancellationRequested();

        var authenticated = await AuthenticateReleaseAsync(claimed, cancellationToken).ConfigureAwait(false);
        if (authenticated is null) return Quarantine("release-history-invalid");

        CapacityGrantSnapshotAtResultV1 pinned;
        try
        {
            pinned = await CapacityGrantSnapshotReaderV1.ReadAtAsync(_capacityJournal,
                authenticated.Command.Position.Session, authenticated.FactBody.GrantId,
                authenticated.FactBody.CurrentFact, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Store("capacity-history-unavailable"); }
        if (pinned is CapacityGrantSnapshotAtResultV1.OutcomeUnknown unknown)
            return unknown.SafeCode.ToString() is "capacity-history-read-failed" or "capacity-history-snapshot-drift"
                ? Store("capacity-history-unavailable") : Quarantine("capacity-history-invalid");

        var grant = ((CapacityGrantSnapshotAtResultV1.Exact)pinned).Grant;
        var charge = authenticated.FactBody.Assignment.Charge;
        var matches = grant.Balances.Where(balance => balance.Charge == charge).ToArray();
        if (grant.GrantId != authenticated.FactBody.GrantId || grant.CurrentFact != authenticated.FactBody.CurrentFact ||
            grant.Authority.Session != authenticated.Command.Position.Session || matches.Length != 1 ||
            matches[0].Unactivated + matches[0].Active != charge.Amount || matches[0].Released != 0 ||
            matches[0].Revoked != 0 || matches[0].ExplicitlyUnknown != 0)
            return Quarantine("capacity-release-join-invalid");

        var body = new CapacitySettlementFactBodyV1(grant.GrantId, authenticated.CommandBody.OperationId,
            grant.CurrentFact, CapacitySettlementKindV1.Released,
            [new CapacitySettlementChargeV1(charge.DimensionId, charge.Scope, charge.Purpose, charge.Amount)],
            authenticated.FactBody.ObservedAt);
        cancellationToken.ThrowIfCancellationRequested();
        CapacityAdmissionResultV1 admitted;
        try
        {
            admitted = await CapacityAdmissionCoordinatorV1.SettleAsync(_capacityJournal,
                authenticated.Command.Position.Session, body, authenticated.Fact.Correlation,
                authenticated.Fact.ObservedAt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception) { return Store("capacity-settlement-unavailable"); }

        return admitted switch
        {
            CapacityAdmissionResultV1.Settled settled when IsClosed(settled, charge) =>
                new GraphMediaPhysicalReleaseSettlementResultV1.Settled(settled.Envelope, settled.Grant),
            CapacityAdmissionResultV1.Settled => Quarantine("capacity-settlement-invalid"),
            CapacityAdmissionResultV1.RetryRequired retry => new GraphMediaPhysicalReleaseSettlementResultV1.RetryRequired(retry.ObservedHead),
            CapacityAdmissionResultV1.OutcomeUnknown => Store("capacity-settlement-outcome-unknown"),
            CapacityAdmissionResultV1.StaleAuthority => Quarantine("capacity-authority-stale"),
            CapacityAdmissionResultV1.ContradictoryDuplicate => Quarantine("capacity-settlement-contradiction"),
            CapacityAdmissionResultV1.Refused refused => new GraphMediaPhysicalReleaseSettlementResultV1.Quarantined(refused.SafeCode),
            _ => Quarantine("capacity-settlement-invalid")
        };
    }

    private async ValueTask<GraphMediaPhysicalReleaseFoldResultV1.Released?> AuthenticateReleaseAsync(
        GraphMediaPhysicalReleaseFoldResultV1.Released claimed, CancellationToken cancellationToken)
    {
        var session = claimed.Command.Position.Session;
        if (!session.IsValid || claimed.Fact.Position.Session != session ||
            claimed.CommandBody.Residence.ResidenceId.Equals(default(StableId128))) return null;
        var fold = GraphMediaPhysicalReleaseFoldV1.Create(session, claimed.CommandBody.Residence.ResidenceId, _releaseRegistry);
        long cursor = 0;
        var through = claimed.Fact.Position.Sequence;
        while (cursor < through)
        {
            ReadAuthorityRangeResultV1 read;
            try
            {
                read = await _releaseJournal.ReadAsync(new ReadAuthorityRangeV1(session, cursor, through,
                    MaximumReadItems, MaximumReadBytes), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return null; }
            if (read is not ReadAuthorityRangeResultV1.Batch batch || batch.AfterExclusive != cursor ||
                batch.SnapshotThrough != through || batch.Facts.Count == 0) return null;
            foreach (var envelope in batch.Facts)
                if (fold.Apply(envelope) is GraphMediaPhysicalReleaseFoldApplyResultV1.InvalidHistory) return null;
            cursor = batch.Facts[^1].Position.Sequence;
            if (!batch.HasMore && cursor != through) return null;
        }
        if (fold.Complete() is not GraphMediaPhysicalReleaseFoldResultV1.Released actual ||
            !SameEnvelope(actual.Command, claimed.Command) || !SameEnvelope(actual.Fact, claimed.Fact) ||
            actual.EvidenceHash != claimed.EvidenceHash || actual.CommandBody != claimed.CommandBody ||
            actual.FactBody != claimed.FactBody) return null;
        return actual;
    }

    private static bool IsClosed(CapacityAdmissionResultV1.Settled settled, CapacityChargeV1 charge)
    {
        var balances = settled.Grant.Balances.Where(balance => balance.Charge == charge).ToArray();
        return settled.Grant.CurrentFact == settled.Envelope.Position && balances.Length == 1 &&
            balances[0].Unactivated == 0 && balances[0].Active == 0 && balances[0].Revoked == 0 &&
            balances[0].ExplicitlyUnknown == 0 && balances[0].Released == charge.Amount &&
            balances[0].EncumberedNormal == 0 && balances[0].EncumberedReserve == 0;
    }

    private static bool SameEnvelope(AuthorityFactEnvelopeV1 left, AuthorityFactEnvelopeV1 right) =>
        left.FactId == right.FactId && left.Position == right.Position && left.ThreadScope == right.ThreadScope &&
        left.Owner == right.Owner && left.PayloadSchema == right.PayloadSchema && left.PayloadHash == right.PayloadHash &&
        left.Correlation == right.Correlation && left.ObservedAt == right.ObservedAt && left.AdmittedAt == right.AdmittedAt &&
        left.PayloadBytes.SequenceEqual(right.PayloadBytes) && left.Integrity.Profile == right.Integrity.Profile &&
        left.Integrity.KeyVersion == right.Integrity.KeyVersion && left.Integrity.Digest == right.Integrity.Digest &&
        left.Integrity.SignatureBytes.SequenceEqual(right.Integrity.SignatureBytes);

    private static GraphMediaPhysicalReleaseSettlementResultV1.Quarantined Quarantine(string code) => new(new BoundedAscii(code));
    private static GraphMediaPhysicalReleaseSettlementResultV1.StoreUnavailable Store(string code) => new(new BoundedAscii(code));
}
