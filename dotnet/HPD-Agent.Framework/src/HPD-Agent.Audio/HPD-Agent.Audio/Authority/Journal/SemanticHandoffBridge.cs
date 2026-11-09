using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal abstract record SemanticHandoffBridgeResult
{
    private SemanticHandoffBridgeResult() { }
    internal sealed record Admitted(JournalPositionV1 DecisionPosition, JournalPositionV1 AcceptancePosition,
        JournalPositionV1 BindingPosition, JournalPositionV1 ReceiptPosition) : SemanticHandoffBridgeResult;
    internal sealed record RetryRequired(long ObservedHead) : SemanticHandoffBridgeResult;
    internal sealed record Ineligible(BoundedAscii SafeCode) : SemanticHandoffBridgeResult;
    internal sealed record InvalidHistory(BoundedAscii SafeCode) : SemanticHandoffBridgeResult;
    internal sealed record OutcomeUnknown(BoundedAscii SafeCode) : SemanticHandoffBridgeResult;
}

internal sealed class SemanticReceiptAdmittedPayloadRegistration : AuthorityPayloadRegistrationV1
{
    internal const string SchemaId = "hpd.semantic-receipt-admitted.v1";
    internal SemanticReceiptAdmittedPayloadRegistration() : base(new BoundedAscii(SchemaId), 1, 0, OwnerSliceId.S4, 4096) { }
    private protected override bool ValidateCanonicalPayload(ReadOnlyMemory<byte> payload, SessionAuthorityStampV1 session) =>
        CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeSemanticReceipt(payload, out var value) &&
        value!.SourcePosition.Session == session;
}

internal static class SemanticHandoffBridge
{
    private const ushort MaximumFacts = AppendAuthorityBatchV1.MaximumItems;
    private const uint MaximumBytes = ProposedAuthorityFactV1.MaximumPayloadBytes;
    private const uint MaximumAppendBytes = 16_384;

    internal static async ValueTask<SemanticHandoffBridgeResult> AdmitAsync(IAuthorityJournalV1 journal,
        JournalPositionV1 decisionPosition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (!decisionPosition.IsValid) throw new ArgumentException("A valid decision position is required.", nameof(decisionPosition));
        var proof = await ReadAsync(journal, decisionPosition.Session, cancellationToken).ConfigureAwait(false);
        if (proof.Error is not null) return proof.Error;
        var decision = proof.Facts!.SingleOrDefault(fact => fact.Position == decisionPosition);
        if (decision is null || decision.Owner != OwnerSliceId.S4 ||
            decision.PayloadSchema != TurnGenerationAuthorityPayloadRegistrations.TurnDecisionFinalized.Schema ||
            !TurnGenerationAuthorityOuterCodec.TryDecodeTurn(decision.PayloadMemory, out var outer) ||
            outer!.Session != decisionPosition.Session || outer.ExpectedAuthority.Session != outer.Session ||
            !TurnGenerationRecordCodecs.TryDecodeTurn(outer.Body.ToArray(), out var body) ||
            body!.OperationId != decision.Correlation.OperationId || body.Authority != outer.ExpectedAuthority ||
            body.SourcePosition.Session != outer.Session || decision.PayloadHash != TurnGenerationAuthorityOuterCodec.ComputeHash(outer))
            return new SemanticHandoffBridgeResult.InvalidHistory(new BoundedAscii("turn-decision-proof-invalid"));

        var handoff = await SemanticHandoffCoordinatorV1.BindAsync(journal, decisionPosition, body.OperationId,
            body.Authority, decision.Correlation, decision.ObservedAt, cancellationToken).ConfigureAwait(false);
        if (handoff is not SemanticHandoffResultV1.Bound bound) return Map(handoff);

        var reread = await ReadAsync(journal, decisionPosition.Session, cancellationToken).ConfigureAwait(false);
        if (reread.Error is not null) return reread.Error;
        var existing = FindReceipt(reread.Facts!, body, bound.BindingPosition);
        if (existing is not null)
            return new SemanticHandoffBridgeResult.Admitted(decisionPosition, bound.AcceptancePosition,
                bound.BindingPosition, existing.Position);

        var receipt = new SemanticReceiptAdmitted(body.OperationId, bound.BindingPosition, body.Authority, 1);
        var registration = new SemanticReceiptAdmittedPayloadRegistration();
        var payload = CrossOwnerLifecycleAuthorityRecordCodecs.Encode(receipt);
        var payloadHash = AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload);
        var proposal = new ProposedAuthorityFactV1(ReceiptFactId(payloadHash), null, OwnerSliceId.S4,
            registration.Schema, payload, payloadHash, decision.Correlation, decision.ObservedAt);
        try
        {
            var appended = await journal.AppendAsync(new AppendAuthorityBatchV1(decisionPosition.Session, reread.Head,
                [], [proposal], MaximumAppendBytes), cancellationToken).ConfigureAwait(false);
            if (appended is AppendAuthorityResultV1.Committed committed && committed.PreviousHead == reread.Head &&
                committed.Envelopes.Count == 1 && Matches(committed.Envelopes[0], proposal))
                return new SemanticHandoffBridgeResult.Admitted(decisionPosition, bound.AcceptancePosition,
                    bound.BindingPosition, committed.Envelopes[0].Position);
            if (appended is AppendAuthorityResultV1.AlreadyCommitted already && already.Envelopes.Count == 1 &&
                Matches(already.Envelopes[0], proposal))
                return new SemanticHandoffBridgeResult.Admitted(decisionPosition, bound.AcceptancePosition,
                    bound.BindingPosition, already.Envelopes[0].Position);
            if (appended is AppendAuthorityResultV1.SessionConflict or AppendAuthorityResultV1.OutcomeUnknown)
                return await ReconcileAsync(journal, decisionPosition, body, bound, cancellationToken.IsCancellationRequested).ConfigureAwait(false);
            return appended switch
            {
                AppendAuthorityResultV1.InvalidPayload invalid => new SemanticHandoffBridgeResult.Ineligible(invalid.SafeCode),
                AppendAuthorityResultV1.StoreUnavailable unavailable => new SemanticHandoffBridgeResult.OutcomeUnknown(unavailable.SafeCode),
                AppendAuthorityResultV1.ContradictoryDuplicate => new SemanticHandoffBridgeResult.InvalidHistory(new BoundedAscii("receipt-contradictory-duplicate")),
                _ => new SemanticHandoffBridgeResult.RetryRequired(reread.Head),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var reconciled = await ReconcileAsync(journal, decisionPosition, body, bound, true).ConfigureAwait(false);
            if (reconciled is SemanticHandoffBridgeResult.Admitted) return reconciled;
            throw;
        }
        catch (Exception)
        {
            return await ReconcileAsync(journal, decisionPosition, body, bound, false).ConfigureAwait(false);
        }
    }

    private static async ValueTask<SemanticHandoffBridgeResult> ReconcileAsync(IAuthorityJournalV1 journal,
        JournalPositionV1 decision, TurnDecisionFinalized body, SemanticHandoffResultV1.Bound bound, bool cancelled)
    {
        var read = await ReadAsync(journal, decision.Session, CancellationToken.None).ConfigureAwait(false);
        if (read.Error is not null) return read.Error;
        var receipt = FindReceipt(read.Facts!, body, bound.BindingPosition);
        if (receipt is not null)
            return new SemanticHandoffBridgeResult.Admitted(decision, bound.AcceptancePosition, bound.BindingPosition, receipt.Position);
        return cancelled ? new SemanticHandoffBridgeResult.OutcomeUnknown(new BoundedAscii("receipt-cancelled-after-invocation"))
            : new SemanticHandoffBridgeResult.OutcomeUnknown(new BoundedAscii("receipt-outcome-unknown"));
    }

    private static AuthorityFactEnvelopeV1? FindReceipt(IReadOnlyList<AuthorityFactEnvelopeV1> facts,
        TurnDecisionFinalized decision, JournalPositionV1 binding)
    {
        AuthorityFactEnvelopeV1? found = null;
        foreach (var fact in facts.Where(static fact => fact.PayloadSchema == new SemanticReceiptAdmittedPayloadRegistration().Schema))
        {
            if (!CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeSemanticReceipt(fact.PayloadMemory, out var value) ||
                value!.OperationId != decision.OperationId) continue;
            if (found is not null || fact.Owner != OwnerSliceId.S4 || value.SourcePosition != binding ||
                value.Authority != decision.Authority || value.Disposition != 1 ||
                fact.PayloadHash != CrossOwnerLifecycleAuthorityRecordCodecs.ComputeHash(value) ||
                fact.FactId != ReceiptFactId(fact.PayloadHash)) throw new InvalidOperationException("Contradictory semantic receipt history.");
            found = fact;
        }
        return found;
    }

    private static async ValueTask<(IReadOnlyList<AuthorityFactEnvelopeV1>? Facts, long Head, SemanticHandoffBridgeResult? Error)> ReadAsync(
        IAuthorityJournalV1 journal, SessionAuthorityStampV1 session, CancellationToken cancellationToken)
    {
        var result = await journal.ReadAsync(new ReadAuthorityRangeV1(session, 0, long.MaxValue, MaximumFacts, MaximumBytes), cancellationToken).ConfigureAwait(false);
        if (result is ReadAuthorityRangeResultV1.StoreUnavailable unavailable)
            return (null, 0, new SemanticHandoffBridgeResult.OutcomeUnknown(unavailable.SafeCode));
        if (result is not ReadAuthorityRangeResultV1.Batch batch || batch.Session != session || batch.AfterExclusive != 0 || batch.HasMore)
            return (null, 0, new SemanticHandoffBridgeResult.OutcomeUnknown(new BoundedAscii("receipt-read-invalid")));
        return (batch.Facts, batch.SnapshotThrough, null);
    }

    private static JournalFactId ReceiptFactId(Hash256 payloadHash)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd-s4-l5-fact-id-v1\0"u8);
        Span<byte> bytes = stackalloc byte[32];
        if (!payloadHash.TryWriteBytes(bytes)) throw new InvalidOperationException("Receipt payload hash is invalid.");
        hash.AppendData(bytes);
        return JournalFactId.FromValue(StableId128.FromBytes(hash.GetHashAndReset().AsSpan(0, 16)));
    }

    private static bool Matches(AuthorityFactEnvelopeV1 envelope, ProposedAuthorityFactV1 proposal) =>
        envelope.FactId == proposal.FactId && envelope.Owner == proposal.Owner && envelope.ThreadScope is null &&
        envelope.PayloadSchema == proposal.PayloadSchema && envelope.PayloadHash == proposal.PayloadHash &&
        envelope.Payload.SequenceEqual(proposal.Payload) && envelope.Correlation == proposal.Correlation &&
        envelope.ObservedAt == proposal.ObservedAt;

    private static SemanticHandoffBridgeResult Map(SemanticHandoffResultV1 result) => result switch
    {
        SemanticHandoffResultV1.RetryRequired retry => new SemanticHandoffBridgeResult.RetryRequired(retry.ObservedHead),
        SemanticHandoffResultV1.Ineligible invalid => new SemanticHandoffBridgeResult.Ineligible(invalid.SafeCode),
        SemanticHandoffResultV1.InvalidHistory invalid => new SemanticHandoffBridgeResult.InvalidHistory(invalid.SafeCode),
        SemanticHandoffResultV1.OutcomeUnknown unknown => new SemanticHandoffBridgeResult.OutcomeUnknown(unknown.SafeCode),
        _ => throw new InvalidOperationException("Unexpected semantic handoff result."),
    };
}
