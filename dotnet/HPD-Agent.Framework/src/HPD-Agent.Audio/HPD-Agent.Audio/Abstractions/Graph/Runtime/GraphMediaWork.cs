using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Formats.Cbor;
using System.Security.Cryptography;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal enum GraphMediaWorkState : byte { Registered, Running, Terminal, Unknown }
internal enum GraphMediaCleanupState : byte { Registered, Running, Succeeded, Unknown }
internal enum GraphMediaReleaseEligibility : byte { Eligible, Encumbered, NotFound }
internal enum GraphMediaWorkResult : byte
{
    Registered, IdempotentRegistered, Running, Terminal, Succeeded, OutcomeUnknown,
    ReconciledRunning, ReconciledSucceeded, InvalidRequest, StaleGeneration,
    ResidenceNotFound, ResidenceNotVisible, OwnerMismatch, WorkNotFound,
    CleanupNotFound, WrongState, ContradictoryDuplicate, WorkLimitReached,
    CleanupLimitReached, CleanupOrderConflict, ReconciledTerminal, Rejected
}

internal sealed record GraphMediaCleanupRegistration(StableId128 CleanupId, Hash256 RequestHash);
internal sealed record GraphMediaWorkRegistration(StableId128 WorkId, Hash256 RequestHash,
    StableId128 ResidenceId, IReadOnlyList<GraphMediaCleanupRegistration> Cleanups);
internal sealed record GraphMediaWorkRecord(StableId128 WorkId, Hash256 RequestHash,
    StableId128 ResidenceId, OperationId ResidenceOperationId, Hash256 ResidenceRequestHash,
    StableId128 OwnerId, GraphMediaOwnerKey OwnerKey, GraphMediaBinding Media,
    ParticipantId ParticipantId, JournalPositionV1 BindingFactPosition, CapacityGrantId GrantId,
    JournalPositionV1 CurrentFact, Hash256 CoverageHashV2, GraphMediaCapacityAssignment Assignment,
    GraphMediaWorkState State, Hash256? OutcomeHash, Hash256? ReconciliationHash);
internal sealed record GraphMediaCleanupRecord(StableId128 CleanupId, StableId128 WorkId,
    Hash256 RequestHash, byte RegistrationOrdinal, GraphMediaCleanupState State,
    Hash256? EvidenceHash);
internal sealed record GraphMediaWorkTransition(GraphMediaWorkResult Result,
    GraphMediaWorkLedger Ledger);

internal sealed class GraphMediaWorkLedger
{
    internal const int MaximumWorkPerRuntime = 64, MaximumCleanupPerRuntime = 64,
        MaximumCleanupPerWork = 16;
    private readonly Dictionary<StableId128, GraphMediaWorkRecord> _work;
    private readonly Dictionary<StableId128, GraphMediaCleanupRecord> _cleanup;

    private GraphMediaWorkLedger(SessionAuthorityStampV1 session, GraphGenerationId graph,
        Dictionary<StableId128, GraphMediaWorkRecord> work,
        Dictionary<StableId128, GraphMediaCleanupRecord> cleanup)
    { Session = session; GraphGeneration = graph; _work = work; _cleanup = cleanup; }

    internal SessionAuthorityStampV1 Session { get; }
    internal GraphGenerationId GraphGeneration { get; }
    internal IReadOnlyDictionary<StableId128, GraphMediaWorkRecord> Work =>
        new ReadOnlyDictionary<StableId128, GraphMediaWorkRecord>(_work);
    internal IReadOnlyDictionary<StableId128, GraphMediaCleanupRecord> Cleanup =>
        new ReadOnlyDictionary<StableId128, GraphMediaCleanupRecord>(_cleanup);
    internal Hash256 Fingerprint => ComputeFingerprint(Session, GraphGeneration, _work.Values, _cleanup.Values);

    internal static GraphMediaWorkLedger Create(SessionAuthorityStampV1 session,
        GraphGenerationId graph)
    {
        if (!session.IsValid || !graph.IsValid) throw new ArgumentException("Valid authority is required.");
        return new(session, graph, [], []);
    }

    internal GraphMediaWorkTransition Register(GraphMediaWorkRegistration request,
        GraphMediaResidenceLedger residences, GraphMediaOwnershipLedger ownership)
    {
        if (request is null || residences is null || ownership is null ||
            request.WorkId.Equals(default) || request.RequestHash == default ||
            request.ResidenceId.Equals(default) || request.Cleanups is null ||
            request.Cleanups.Count is < 1 or > MaximumCleanupPerWork ||
            request.Cleanups.Any(x => x is null || x.CleanupId.Equals(default) || x.RequestHash == default))
            return Fail(GraphMediaWorkResult.InvalidRequest);
        if (residences.Session != Session || ownership.Session != Session ||
            residences.GraphGeneration != GraphGeneration || ownership.GraphGeneration != GraphGeneration)
            return Fail(GraphMediaWorkResult.StaleGeneration);
        if (_work.TryGetValue(request.WorkId, out var retry))
        {
            var recordedCleanup = _cleanup.Values.Where(x => x.WorkId.Equals(request.WorkId))
                .OrderBy(x => x.RegistrationOrdinal).ToArray();
            var exact = retry.RequestHash == request.RequestHash && retry.ResidenceId.Equals(request.ResidenceId) &&
                request.Cleanups.Count == recordedCleanup.Length && request.Cleanups.Select((x, i) => (x, i)).All(pair =>
                    recordedCleanup[pair.i].CleanupId.Equals(pair.x.CleanupId) &&
                    recordedCleanup[pair.i].RequestHash == pair.x.RequestHash);
            return Fail(exact ? GraphMediaWorkResult.IdempotentRegistered : GraphMediaWorkResult.ContradictoryDuplicate);
        }
        if (request.Cleanups.Select(x => x.CleanupId).Distinct().Count() != request.Cleanups.Count)
            return Fail(GraphMediaWorkResult.InvalidRequest);
        var currentId = new byte[16]; var priorId = new byte[16];
        for (var i = 0; i < request.Cleanups.Count; i++)
        {
            request.Cleanups[i].CleanupId.TryWriteBytes(currentId);
            if (i > 0)
            {
                request.Cleanups[i - 1].CleanupId.TryWriteBytes(priorId);
                if (priorId.AsSpan().SequenceCompareTo(currentId) >= 0)
                    return Fail(GraphMediaWorkResult.InvalidRequest);
            }
        }
        if (request.Cleanups.Any(x => _cleanup.ContainsKey(x.CleanupId)))
            return Fail(GraphMediaWorkResult.ContradictoryDuplicate);
        if (!residences.Residences.TryGetValue(request.ResidenceId, out var residence))
            return Fail(GraphMediaWorkResult.ResidenceNotFound);
        if (residence.Class != GraphMediaResidenceClass.Controlled ||
            residence.State != GraphMediaResidenceState.Visible)
            return Fail(GraphMediaWorkResult.ResidenceNotVisible);
        if (!ownership.Owners.TryGetValue(residence.OwnerId, out var owner) ||
            owner.State != GraphMediaOwnerState.Owned || owner.Key != residence.OwnerKey ||
            owner.Media != residence.Media)
            return Fail(GraphMediaWorkResult.OwnerMismatch);
        if (RegistrationHash(request, residence) != request.RequestHash)
            return Fail(GraphMediaWorkResult.InvalidRequest);
        if (_work.Count >= MaximumWorkPerRuntime) return Fail(GraphMediaWorkResult.WorkLimitReached);
        if (_cleanup.Count + request.Cleanups.Count > MaximumCleanupPerRuntime)
            return Fail(GraphMediaWorkResult.CleanupLimitReached);

        var work = new Dictionary<StableId128, GraphMediaWorkRecord>(_work)
        {
            [request.WorkId] = new(request.WorkId, request.RequestHash, residence.ResidenceId,
                residence.OperationId, residence.RequestHash, residence.OwnerId, residence.OwnerKey,
                residence.Media, residence.ParticipantId, residence.BindingFactPosition,
                residence.GrantId, residence.CurrentFact, residence.CoverageHashV2,
                residence.Assignment, GraphMediaWorkState.Registered, null, null)
        };
        var cleanup = new Dictionary<StableId128, GraphMediaCleanupRecord>(_cleanup);
        for (var i = 0; i < request.Cleanups.Count; i++)
        {
            var row = request.Cleanups[i];
            cleanup[row.CleanupId] = new(row.CleanupId, request.WorkId, row.RequestHash,
                checked((byte)(_cleanup.Count + i)), GraphMediaCleanupState.Registered, null);
        }
        return new(GraphMediaWorkResult.Registered, Next(work, cleanup));
    }

    internal GraphMediaWorkTransition StartWork(StableId128 workId, Hash256 requestHash)
    {
        if (!_work.TryGetValue(workId, out var row)) return Fail(GraphMediaWorkResult.WorkNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State == GraphMediaWorkState.Running) return Fail(GraphMediaWorkResult.Running);
        if (row.State != GraphMediaWorkState.Registered) return Fail(GraphMediaWorkResult.WrongState);
        return new(GraphMediaWorkResult.Running, WithWork(row with { State = GraphMediaWorkState.Running }));
    }

    internal GraphMediaWorkTransition FinishWork(StableId128 workId, Hash256 requestHash,
        Hash256 outcomeHash)
    {
        if (outcomeHash == default) return Fail(GraphMediaWorkResult.InvalidRequest);
        if (!_work.TryGetValue(workId, out var row)) return Fail(GraphMediaWorkResult.WorkNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State == GraphMediaWorkState.Terminal)
            return Fail(row.OutcomeHash == outcomeHash ? GraphMediaWorkResult.Terminal : GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State == GraphMediaWorkState.Unknown) return Fail(GraphMediaWorkResult.WrongState);
        return new(GraphMediaWorkResult.Terminal,
            WithWork(row with
            {
                State = GraphMediaWorkState.Terminal,
                OutcomeHash = outcomeHash,
                ReconciliationHash = null
            }));
    }

    internal GraphMediaWorkTransition LoseWorkOutcome(StableId128 workId, Hash256 requestHash)
    {
        if (!_work.TryGetValue(workId, out var row)) return Fail(GraphMediaWorkResult.WorkNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State == GraphMediaWorkState.Unknown) return Fail(GraphMediaWorkResult.OutcomeUnknown);
        if (row.State != GraphMediaWorkState.Running) return Fail(GraphMediaWorkResult.WrongState);
        return new(GraphMediaWorkResult.OutcomeUnknown,
            WithWork(row with { State = GraphMediaWorkState.Unknown, ReconciliationHash = null }));
    }

    internal GraphMediaWorkTransition RejectWork(StableId128 workId, Hash256 requestHash)
    {
        if (!_work.TryGetValue(workId, out var row)) return Fail(GraphMediaWorkResult.WorkNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State != GraphMediaWorkState.Running) return Fail(GraphMediaWorkResult.WrongState);
        return new(GraphMediaWorkResult.Rejected, WithWork(row with
        {
            State = GraphMediaWorkState.Registered,
            OutcomeHash = null,
            ReconciliationHash = null
        }));
    }

    internal GraphMediaWorkTransition ReconcileWork(StableId128 workId, Hash256 requestHash,
        bool terminal, Hash256 evidenceHash)
    {
        if (evidenceHash == default) return Fail(GraphMediaWorkResult.InvalidRequest);
        if (!_work.TryGetValue(workId, out var row)) return Fail(GraphMediaWorkResult.WorkNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State == GraphMediaWorkState.Terminal)
            return Fail(terminal && row.OutcomeHash == evidenceHash
                ? GraphMediaWorkResult.ReconciledTerminal : GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State == GraphMediaWorkState.Running && row.ReconciliationHash is { } prior)
            return Fail(!terminal && prior == evidenceHash
                ? GraphMediaWorkResult.ReconciledRunning : GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State != GraphMediaWorkState.Unknown) return Fail(GraphMediaWorkResult.WrongState);
        return terminal
            ? new(GraphMediaWorkResult.ReconciledTerminal, WithWork(row with
            { State = GraphMediaWorkState.Terminal, OutcomeHash = evidenceHash, ReconciliationHash = null }))
            : new(GraphMediaWorkResult.ReconciledRunning, WithWork(row with
            { State = GraphMediaWorkState.Running, ReconciliationHash = evidenceHash }));
    }

    internal GraphMediaWorkTransition ClaimCleanup(StableId128 workId, StableId128 cleanupId,
        Hash256 requestHash)
    {
        if (!_work.TryGetValue(workId, out var work)) return Fail(GraphMediaWorkResult.WorkNotFound);
        if (!_cleanup.TryGetValue(cleanupId, out var row) || !row.WorkId.Equals(workId))
            return Fail(GraphMediaWorkResult.CleanupNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResult.ContradictoryDuplicate);
        if (work.State != GraphMediaWorkState.Terminal) return Fail(GraphMediaWorkResult.WrongState);
        if (row.State == GraphMediaCleanupState.Running) return Fail(GraphMediaWorkResult.Running);
        if (row.State != GraphMediaCleanupState.Registered) return Fail(GraphMediaWorkResult.WrongState);
        if (_cleanup.Values.Any(x => x.RegistrationOrdinal > row.RegistrationOrdinal &&
            x.State != GraphMediaCleanupState.Succeeded))
            return Fail(GraphMediaWorkResult.CleanupOrderConflict);
        return new(GraphMediaWorkResult.Running,
            WithCleanup(row with { State = GraphMediaCleanupState.Running }));
    }

    internal GraphMediaWorkTransition FinishCleanup(StableId128 cleanupId, Hash256 requestHash,
        Hash256 evidenceHash)
    {
        if (evidenceHash == default) return Fail(GraphMediaWorkResult.InvalidRequest);
        if (!_cleanup.TryGetValue(cleanupId, out var row)) return Fail(GraphMediaWorkResult.CleanupNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State == GraphMediaCleanupState.Succeeded)
            return Fail(row.EvidenceHash == evidenceHash ? GraphMediaWorkResult.Succeeded : GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State != GraphMediaCleanupState.Running) return Fail(GraphMediaWorkResult.WrongState);
        return new(GraphMediaWorkResult.Succeeded,
            WithCleanup(row with { State = GraphMediaCleanupState.Succeeded, EvidenceHash = evidenceHash }));
    }

    internal GraphMediaWorkTransition LoseCleanupOutcome(StableId128 cleanupId,
        Hash256 requestHash)
    {
        if (!_cleanup.TryGetValue(cleanupId, out var row)) return Fail(GraphMediaWorkResult.CleanupNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State == GraphMediaCleanupState.Unknown) return Fail(GraphMediaWorkResult.OutcomeUnknown);
        if (row.State != GraphMediaCleanupState.Running) return Fail(GraphMediaWorkResult.WrongState);
        return new(GraphMediaWorkResult.OutcomeUnknown,
            WithCleanup(row with { State = GraphMediaCleanupState.Unknown }));
    }

    internal GraphMediaWorkTransition ReconcileCleanup(StableId128 cleanupId,
        Hash256 requestHash, bool succeeded, Hash256 evidenceHash)
    {
        if (evidenceHash == default) return Fail(GraphMediaWorkResult.InvalidRequest);
        if (!_cleanup.TryGetValue(cleanupId, out var row)) return Fail(GraphMediaWorkResult.CleanupNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State == GraphMediaCleanupState.Succeeded)
            return Fail(succeeded && row.EvidenceHash == evidenceHash
                ? GraphMediaWorkResult.ReconciledSucceeded : GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State == GraphMediaCleanupState.Running && row.EvidenceHash is not null)
            return Fail(!succeeded && row.EvidenceHash == evidenceHash
                ? GraphMediaWorkResult.ReconciledRunning : GraphMediaWorkResult.ContradictoryDuplicate);
        if (row.State != GraphMediaCleanupState.Unknown) return Fail(GraphMediaWorkResult.WrongState);
        var state = succeeded ? GraphMediaCleanupState.Succeeded : GraphMediaCleanupState.Running;
        var result = succeeded ? GraphMediaWorkResult.ReconciledSucceeded : GraphMediaWorkResult.ReconciledRunning;
        return new(result, WithCleanup(row with { State = state, EvidenceHash = evidenceHash }));
    }

    internal GraphMediaReleaseEligibility QueryReleaseEligibility(StableId128 residenceId)
    {
        var rows = _work.Values.Where(x => x.ResidenceId.Equals(residenceId)).ToArray();
        if (rows.Length == 0) return GraphMediaReleaseEligibility.NotFound;
        if (rows.Any(x => x.State != GraphMediaWorkState.Terminal))
            return GraphMediaReleaseEligibility.Encumbered;
        return rows.All(work => _cleanup.Values.Where(x => x.WorkId.Equals(work.WorkId))
                .All(x => x.State == GraphMediaCleanupState.Succeeded))
            ? GraphMediaReleaseEligibility.Eligible : GraphMediaReleaseEligibility.Encumbered;
    }

    internal static Hash256 RegistrationHash(GraphMediaWorkRegistration request,
        GraphMediaControlledResidence residence)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(residence);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd-s2-graph-media-work-registration-v1\0"u8);
        static byte[] Stable(StableId128 value) { var bytes = new byte[16]; if (!value.TryWriteBytes(bytes)) throw new ArgumentException("Valid identity required."); return bytes; }
        static byte[] Operation(OperationId value) { var bytes = new byte[16]; if (!value.TryWriteBytes(bytes)) throw new ArgumentException("Valid operation required."); return bytes; }
        static byte[] Digest(Hash256 value) { var bytes = new byte[32]; if (!value.TryWriteBytes(bytes)) throw new ArgumentException("Valid hash required."); return bytes; }
        static void Field(IncrementalHash hash, ReadOnlySpan<byte> label, ReadOnlySpan<byte> value)
        { Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, label.Length); hash.AppendData(length); hash.AppendData(label); BinaryPrimitives.WriteInt32BigEndian(length, value.Length); hash.AppendData(length); hash.AppendData(value); }
        Field(hash, "workId"u8, Stable(request.WorkId)); Field(hash, "residenceId"u8, Stable(request.ResidenceId));
        Field(hash, "residenceOperation"u8, Operation(residence.OperationId));
        Field(hash, "residenceRequest"u8, Digest(residence.RequestHash));
        foreach (var cleanup in request.Cleanups)
        { Field(hash, "cleanupId"u8, Stable(cleanup.CleanupId)); Field(hash, "cleanupRequest"u8, Digest(cleanup.RequestHash)); }
        return Hash256.FromBytes(hash.GetHashAndReset());
    }

    private GraphMediaWorkTransition Fail(GraphMediaWorkResult result) => new(result, this);
    private GraphMediaWorkLedger WithWork(GraphMediaWorkRecord row)
    { var work = new Dictionary<StableId128, GraphMediaWorkRecord>(_work) { [row.WorkId] = row }; return Next(work, new(_cleanup)); }
    private GraphMediaWorkLedger WithCleanup(GraphMediaCleanupRecord row)
    { var cleanup = new Dictionary<StableId128, GraphMediaCleanupRecord>(_cleanup) { [row.CleanupId] = row }; return Next(new(_work), cleanup); }
    private GraphMediaWorkLedger Next(Dictionary<StableId128, GraphMediaWorkRecord> work,
        Dictionary<StableId128, GraphMediaCleanupRecord> cleanup) => new(Session, GraphGeneration, work, cleanup);

    private static Hash256 ComputeFingerprint(SessionAuthorityStampV1 session, GraphGenerationId graph,
        IEnumerable<GraphMediaWorkRecord> work,
        IEnumerable<GraphMediaCleanupRecord> cleanup)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd-s2-graph-media-work-ledger-v1\0"u8);
        static byte[] Stable(StableId128 value) { var bytes = new byte[16]; if (!value.TryWriteBytes(bytes)) throw new InvalidOperationException(); return bytes; }
        static byte[] Operation(OperationId value) { var bytes = new byte[16]; if (!value.TryWriteBytes(bytes)) throw new InvalidOperationException(); return bytes; }
        static byte[] Digest(Hash256 value) { var bytes = new byte[32]; if (!value.TryWriteBytes(bytes)) throw new InvalidOperationException(); return bytes; }
        static byte[] Canonical(object value)
        {
            if (value is StableId128 stable) return Stable(stable);
            if (value is OperationId operation) return Operation(operation);
            if (value is Hash256 digest) return Digest(digest);
            if (value is JournalPositionV1 position) return AuthorityPositionCodecsV1.Encode(position);
            if (value is GraphMediaOwnerKey key)
            {
                var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(4);
                writer.WriteByteString(Canonical(key.Session.LiveSessionId)); writer.WriteByteString(Canonical(key.GraphGeneration));
                writer.WriteByteString(Canonical(key.Session.RuntimeGenerationId)); writer.WriteByteString(Stable(key.MediaId));
                writer.WriteEndArray(); return writer.Encode();
            }
            if (value is GraphMediaBinding media)
            {
                var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(13);
                writer.WriteInt64(media.Start); writer.WriteInt64(media.EndExclusive); writer.WriteByteString(Stable(media.FormatId));
                writer.WriteUInt64(media.FormatRevision); writer.WriteUInt64(media.SampleRateHz); writer.WriteUInt64(media.ChannelCount);
                writer.WriteUInt64(media.BytesPerSample); writer.WriteByteString(Stable(media.ClockId)); writer.WriteUInt64(media.ClockRevision);
                writer.WriteUInt64(media.Sequence); writer.WriteUInt64((byte)media.Discontinuity); writer.WriteInt64(media.ByteLength);
                writer.WriteInt64(media.FrameCount); writer.WriteEndArray(); return writer.Encode();
            }
            if (value is CapacityChargeV1 charge)
            {
                var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(5);
                writer.WriteUInt64(charge.DimensionId.Value); writer.WriteByteString(CapacityScopeCanonicalCodecV1.Encode(charge.Scope));
                writer.WriteInt64(charge.Amount); writer.WriteByteString(Canonical(charge.Purpose));
                writer.WriteStartArray(charge.Window is CapacityChargeWindowV1.EndsAt ? 2 : 1); writer.WriteUInt64((ushort)charge.Window.Kind);
                if (charge.Window is CapacityChargeWindowV1.EndsAt endsAt) writer.WriteByteString(MonotonicStampV1Codec.Encode(endsAt.Value));
                writer.WriteEndArray(); writer.WriteEndArray(); return writer.Encode();
            }
            var bytes = new byte[16];
            var valid = value switch
            {
                LiveSessionId x => x.TryWriteBytes(bytes),
                RuntimeGenerationId x => x.TryWriteBytes(bytes),
                GraphGenerationId x => x.TryWriteBytes(bytes),
                ParticipantId x => x.TryWriteBytes(bytes),
                CapacityGrantId x => x.TryWriteBytes(bytes),
                CapacityPurposeId x => x.TryWriteBytes(bytes),
                _ => false
            };
            return valid ? bytes : throw new ArgumentException("Unsupported canonical value.");
        }
        static void Field(IncrementalHash hash, ReadOnlySpan<byte> label, ReadOnlySpan<byte> value)
        { Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, label.Length); hash.AppendData(length); hash.AppendData(label); BinaryPrimitives.WriteInt32BigEndian(length, value.Length); hash.AppendData(length); hash.AppendData(value); }
        var workRows = work.ToArray(); var cleanupRows = cleanup.ToArray();
        Field(hash, "runtime"u8, Canonical(session.RuntimeGenerationId));
        Field(hash, "session"u8, Canonical(session.LiveSessionId));
        Field(hash, "graph"u8, Canonical(graph));
        foreach (var row in workRows.OrderBy(x => Convert.ToHexString(Stable(x.WorkId)), StringComparer.Ordinal))
        {
            Field(hash, "workId"u8, Stable(row.WorkId)); Field(hash, "workRequest"u8, Digest(row.RequestHash));
            Field(hash, "residenceId"u8, Stable(row.ResidenceId)); Field(hash, "residenceOperation"u8, Operation(row.ResidenceOperationId));
            Field(hash, "residenceRequest"u8, Digest(row.ResidenceRequestHash)); Field(hash, "ownerId"u8, Stable(row.OwnerId));
            Field(hash, "ownerKey"u8, Canonical(row.OwnerKey)); Field(hash, "media"u8, Canonical(row.Media));
            Field(hash, "participant"u8, Canonical(row.ParticipantId)); Field(hash, "bindingFact"u8, Canonical(row.BindingFactPosition));
            Field(hash, "grantId"u8, Canonical(row.GrantId)); Field(hash, "currentFact"u8, Canonical(row.CurrentFact));
            Field(hash, "coverage"u8, Digest(row.CoverageHashV2)); Field(hash, "assignmentCharge"u8, Canonical(row.Assignment.Charge));
            Field(hash, "assignmentArm"u8, [(byte)row.Assignment.Arm]);
            Field(hash, "state"u8, [(byte)row.State]); Field(hash, "outcome"u8, row.OutcomeHash is { } outcome ? Digest(outcome) : []);
            Field(hash, "reconciliation"u8, row.ReconciliationHash is { } reconciliation ? Digest(reconciliation) : []);
        }
        foreach (var row in cleanupRows.OrderBy(x => Convert.ToHexString(Stable(x.CleanupId)), StringComparer.Ordinal))
        {
            Field(hash, "cleanupId"u8, Stable(row.CleanupId)); Field(hash, "cleanupWork"u8, Stable(row.WorkId));
            Field(hash, "cleanupRequest"u8, Digest(row.RequestHash)); Field(hash, "ordinal"u8, [row.RegistrationOrdinal]);
            Field(hash, "cleanupState"u8, [(byte)row.State]); Field(hash, "evidence"u8, row.EvidenceHash is { } evidence ? Digest(evidence) : []);
        }
        return Hash256.FromBytes(hash.GetHashAndReset());
    }
}
