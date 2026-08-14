using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Formats.Cbor;
using System.Security.Cryptography;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal enum GraphMediaWorkStateV1 : byte { Registered, Running, Terminal, Unknown }
internal enum GraphMediaCleanupStateV1 : byte { Registered, Running, Succeeded, Unknown }
internal enum GraphMediaReleaseEligibilityV1 : byte { Eligible, Encumbered, NotFound }
internal enum GraphMediaWorkResultV1 : byte
{
    Registered, IdempotentRegistered, Running, Terminal, Succeeded, OutcomeUnknown,
    ReconciledRunning, ReconciledSucceeded, InvalidRequest, StaleGeneration,
    ResidenceNotFound, ResidenceNotVisible, OwnerMismatch, WorkNotFound,
    CleanupNotFound, WrongState, ContradictoryDuplicate, WorkLimitReached,
    CleanupLimitReached, CleanupOrderConflict, ReconciledTerminal
}

internal sealed record GraphMediaCleanupRegistrationV1(StableId128 CleanupId, Hash256 RequestHash);
internal sealed record GraphMediaWorkRegistrationV1(StableId128 WorkId, Hash256 RequestHash,
    StableId128 ResidenceId, IReadOnlyList<GraphMediaCleanupRegistrationV1> Cleanups);
internal sealed record GraphMediaWorkRecordV1(StableId128 WorkId, Hash256 RequestHash,
    StableId128 ResidenceId, OperationId ResidenceOperationId, Hash256 ResidenceRequestHash,
    StableId128 OwnerId, GraphMediaOwnerKeyV1 OwnerKey, GraphMediaBindingV1 Media,
    ParticipantId ParticipantId, JournalPositionV1 BindingFactPosition, CapacityGrantId GrantId,
    JournalPositionV1 CurrentFact, Hash256 CoverageHashV2, GraphMediaCapacityAssignmentV1 Assignment,
    GraphMediaWorkStateV1 State, Hash256? OutcomeHash, Hash256? ReconciliationHash);
internal sealed record GraphMediaCleanupRecordV1(StableId128 CleanupId, StableId128 WorkId,
    Hash256 RequestHash, byte RegistrationOrdinal, GraphMediaCleanupStateV1 State,
    Hash256? EvidenceHash);
internal sealed record GraphMediaWorkTransitionV1(GraphMediaWorkResultV1 Result,
    GraphMediaWorkLedgerV1 Ledger);

internal sealed class GraphMediaWorkLedgerV1
{
    internal const int MaximumWorkPerRuntime = 64, MaximumCleanupPerRuntime = 64,
        MaximumCleanupPerWork = 16;
    private readonly Dictionary<StableId128, GraphMediaWorkRecordV1> _work;
    private readonly Dictionary<StableId128, GraphMediaCleanupRecordV1> _cleanup;

    private GraphMediaWorkLedgerV1(SessionAuthorityStampV1 session, GraphGenerationId graph,
        Dictionary<StableId128, GraphMediaWorkRecordV1> work,
        Dictionary<StableId128, GraphMediaCleanupRecordV1> cleanup)
    { Session = session; GraphGeneration = graph; _work = work; _cleanup = cleanup; }

    internal SessionAuthorityStampV1 Session { get; }
    internal GraphGenerationId GraphGeneration { get; }
    internal IReadOnlyDictionary<StableId128, GraphMediaWorkRecordV1> Work =>
        new ReadOnlyDictionary<StableId128, GraphMediaWorkRecordV1>(_work);
    internal IReadOnlyDictionary<StableId128, GraphMediaCleanupRecordV1> Cleanup =>
        new ReadOnlyDictionary<StableId128, GraphMediaCleanupRecordV1>(_cleanup);
    internal Hash256 Fingerprint => ComputeFingerprint(Session, GraphGeneration, _work.Values, _cleanup.Values);

    internal static GraphMediaWorkLedgerV1 Create(SessionAuthorityStampV1 session,
        GraphGenerationId graph)
    {
        if (!session.IsValid || !graph.IsValid) throw new ArgumentException("Valid authority is required.");
        return new(session, graph, [], []);
    }

    internal GraphMediaWorkTransitionV1 Register(GraphMediaWorkRegistrationV1 request,
        GraphMediaResidenceLedgerV1 residences, GraphMediaOwnershipLedgerV1 ownership)
    {
        if (request is null || residences is null || ownership is null ||
            request.WorkId.Equals(default) || request.RequestHash == default ||
            request.ResidenceId.Equals(default) || request.Cleanups is null ||
            request.Cleanups.Count is < 1 or > MaximumCleanupPerWork ||
            request.Cleanups.Any(x => x is null || x.CleanupId.Equals(default) || x.RequestHash == default))
            return Fail(GraphMediaWorkResultV1.InvalidRequest);
        if (residences.Session != Session || ownership.Session != Session ||
            residences.GraphGeneration != GraphGeneration || ownership.GraphGeneration != GraphGeneration)
            return Fail(GraphMediaWorkResultV1.StaleGeneration);
        if (_work.TryGetValue(request.WorkId, out var retry))
        {
            var recordedCleanup = _cleanup.Values.Where(x => x.WorkId.Equals(request.WorkId))
                .OrderBy(x => x.RegistrationOrdinal).ToArray();
            var exact = retry.RequestHash == request.RequestHash && retry.ResidenceId.Equals(request.ResidenceId) &&
                request.Cleanups.Count == recordedCleanup.Length && request.Cleanups.Select((x, i) => (x, i)).All(pair =>
                    recordedCleanup[pair.i].CleanupId.Equals(pair.x.CleanupId) &&
                    recordedCleanup[pair.i].RequestHash == pair.x.RequestHash);
            return Fail(exact ? GraphMediaWorkResultV1.IdempotentRegistered : GraphMediaWorkResultV1.ContradictoryDuplicate);
        }
        if (request.Cleanups.Select(x => x.CleanupId).Distinct().Count() != request.Cleanups.Count)
            return Fail(GraphMediaWorkResultV1.InvalidRequest);
        var currentId = new byte[16]; var priorId = new byte[16];
        for (var i = 0; i < request.Cleanups.Count; i++)
        {
            request.Cleanups[i].CleanupId.TryWriteBytes(currentId);
            if (i > 0)
            {
                request.Cleanups[i - 1].CleanupId.TryWriteBytes(priorId);
                if (priorId.AsSpan().SequenceCompareTo(currentId) >= 0)
                    return Fail(GraphMediaWorkResultV1.InvalidRequest);
            }
        }
        if (request.Cleanups.Any(x => _cleanup.ContainsKey(x.CleanupId)))
            return Fail(GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (!residences.Residences.TryGetValue(request.ResidenceId, out var residence))
            return Fail(GraphMediaWorkResultV1.ResidenceNotFound);
        if (residence.Class != GraphMediaResidenceClassV1.Controlled ||
            residence.State != GraphMediaResidenceStateV1.Visible)
            return Fail(GraphMediaWorkResultV1.ResidenceNotVisible);
        if (!ownership.Owners.TryGetValue(residence.OwnerId, out var owner) ||
            owner.State != GraphMediaOwnerStateV1.Owned || owner.Key != residence.OwnerKey ||
            owner.Media != residence.Media)
            return Fail(GraphMediaWorkResultV1.OwnerMismatch);
        if (RegistrationHash(request, residence) != request.RequestHash)
            return Fail(GraphMediaWorkResultV1.InvalidRequest);
        if (_work.Count >= MaximumWorkPerRuntime) return Fail(GraphMediaWorkResultV1.WorkLimitReached);
        if (_cleanup.Count + request.Cleanups.Count > MaximumCleanupPerRuntime)
            return Fail(GraphMediaWorkResultV1.CleanupLimitReached);

        var work = new Dictionary<StableId128, GraphMediaWorkRecordV1>(_work)
        {
            [request.WorkId] = new(request.WorkId, request.RequestHash, residence.ResidenceId,
                residence.OperationId, residence.RequestHash, residence.OwnerId, residence.OwnerKey,
                residence.Media, residence.ParticipantId, residence.BindingFactPosition,
                residence.GrantId, residence.CurrentFact, residence.CoverageHashV2,
                residence.Assignment, GraphMediaWorkStateV1.Registered, null, null)
        };
        var cleanup = new Dictionary<StableId128, GraphMediaCleanupRecordV1>(_cleanup);
        for (var i = 0; i < request.Cleanups.Count; i++)
        {
            var row = request.Cleanups[i];
            cleanup[row.CleanupId] = new(row.CleanupId, request.WorkId, row.RequestHash,
                checked((byte)(_cleanup.Count + i)), GraphMediaCleanupStateV1.Registered, null);
        }
        return new(GraphMediaWorkResultV1.Registered, Next(work, cleanup));
    }

    internal GraphMediaWorkTransitionV1 StartWork(StableId128 workId, Hash256 requestHash)
    {
        if (!_work.TryGetValue(workId, out var row)) return Fail(GraphMediaWorkResultV1.WorkNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (row.State == GraphMediaWorkStateV1.Running) return Fail(GraphMediaWorkResultV1.Running);
        if (row.State != GraphMediaWorkStateV1.Registered) return Fail(GraphMediaWorkResultV1.WrongState);
        return new(GraphMediaWorkResultV1.Running, WithWork(row with { State = GraphMediaWorkStateV1.Running }));
    }

    internal GraphMediaWorkTransitionV1 FinishWork(StableId128 workId, Hash256 requestHash,
        Hash256 outcomeHash)
    {
        if (outcomeHash == default) return Fail(GraphMediaWorkResultV1.InvalidRequest);
        if (!_work.TryGetValue(workId, out var row)) return Fail(GraphMediaWorkResultV1.WorkNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (row.State == GraphMediaWorkStateV1.Terminal)
            return Fail(row.OutcomeHash == outcomeHash ? GraphMediaWorkResultV1.Terminal : GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (row.State == GraphMediaWorkStateV1.Unknown) return Fail(GraphMediaWorkResultV1.WrongState);
        return new(GraphMediaWorkResultV1.Terminal,
            WithWork(row with { State = GraphMediaWorkStateV1.Terminal, OutcomeHash = outcomeHash,
                ReconciliationHash = null }));
    }

    internal GraphMediaWorkTransitionV1 LoseWorkOutcome(StableId128 workId, Hash256 requestHash)
    {
        if (!_work.TryGetValue(workId, out var row)) return Fail(GraphMediaWorkResultV1.WorkNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (row.State == GraphMediaWorkStateV1.Unknown) return Fail(GraphMediaWorkResultV1.OutcomeUnknown);
        if (row.State != GraphMediaWorkStateV1.Running) return Fail(GraphMediaWorkResultV1.WrongState);
        return new(GraphMediaWorkResultV1.OutcomeUnknown,
            WithWork(row with { State = GraphMediaWorkStateV1.Unknown, ReconciliationHash = null }));
    }

    internal GraphMediaWorkTransitionV1 ReconcileWork(StableId128 workId, Hash256 requestHash,
        bool terminal, Hash256 evidenceHash)
    {
        if (evidenceHash == default) return Fail(GraphMediaWorkResultV1.InvalidRequest);
        if (!_work.TryGetValue(workId, out var row)) return Fail(GraphMediaWorkResultV1.WorkNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (row.State == GraphMediaWorkStateV1.Terminal)
            return Fail(terminal && row.OutcomeHash == evidenceHash
                ? GraphMediaWorkResultV1.ReconciledTerminal : GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (row.State == GraphMediaWorkStateV1.Running && row.ReconciliationHash is { } prior)
            return Fail(!terminal && prior == evidenceHash
                ? GraphMediaWorkResultV1.ReconciledRunning : GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (row.State != GraphMediaWorkStateV1.Unknown) return Fail(GraphMediaWorkResultV1.WrongState);
        return terminal
            ? new(GraphMediaWorkResultV1.ReconciledTerminal, WithWork(row with
                { State = GraphMediaWorkStateV1.Terminal, OutcomeHash = evidenceHash, ReconciliationHash = null }))
            : new(GraphMediaWorkResultV1.ReconciledRunning, WithWork(row with
                { State = GraphMediaWorkStateV1.Running, ReconciliationHash = evidenceHash }));
    }

    internal GraphMediaWorkTransitionV1 ClaimCleanup(StableId128 workId, StableId128 cleanupId,
        Hash256 requestHash)
    {
        if (!_work.TryGetValue(workId, out var work)) return Fail(GraphMediaWorkResultV1.WorkNotFound);
        if (!_cleanup.TryGetValue(cleanupId, out var row) || !row.WorkId.Equals(workId))
            return Fail(GraphMediaWorkResultV1.CleanupNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (work.State != GraphMediaWorkStateV1.Terminal) return Fail(GraphMediaWorkResultV1.WrongState);
        if (row.State == GraphMediaCleanupStateV1.Running) return Fail(GraphMediaWorkResultV1.Running);
        if (row.State != GraphMediaCleanupStateV1.Registered) return Fail(GraphMediaWorkResultV1.WrongState);
        if (_cleanup.Values.Any(x => x.RegistrationOrdinal > row.RegistrationOrdinal &&
            x.State != GraphMediaCleanupStateV1.Succeeded))
            return Fail(GraphMediaWorkResultV1.CleanupOrderConflict);
        return new(GraphMediaWorkResultV1.Running,
            WithCleanup(row with { State = GraphMediaCleanupStateV1.Running }));
    }

    internal GraphMediaWorkTransitionV1 FinishCleanup(StableId128 cleanupId, Hash256 requestHash,
        Hash256 evidenceHash)
    {
        if (evidenceHash == default) return Fail(GraphMediaWorkResultV1.InvalidRequest);
        if (!_cleanup.TryGetValue(cleanupId, out var row)) return Fail(GraphMediaWorkResultV1.CleanupNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (row.State == GraphMediaCleanupStateV1.Succeeded)
            return Fail(row.EvidenceHash == evidenceHash ? GraphMediaWorkResultV1.Succeeded : GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (row.State != GraphMediaCleanupStateV1.Running) return Fail(GraphMediaWorkResultV1.WrongState);
        return new(GraphMediaWorkResultV1.Succeeded,
            WithCleanup(row with { State = GraphMediaCleanupStateV1.Succeeded, EvidenceHash = evidenceHash }));
    }

    internal GraphMediaWorkTransitionV1 LoseCleanupOutcome(StableId128 cleanupId,
        Hash256 requestHash)
    {
        if (!_cleanup.TryGetValue(cleanupId, out var row)) return Fail(GraphMediaWorkResultV1.CleanupNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (row.State == GraphMediaCleanupStateV1.Unknown) return Fail(GraphMediaWorkResultV1.OutcomeUnknown);
        if (row.State != GraphMediaCleanupStateV1.Running) return Fail(GraphMediaWorkResultV1.WrongState);
        return new(GraphMediaWorkResultV1.OutcomeUnknown,
            WithCleanup(row with { State = GraphMediaCleanupStateV1.Unknown }));
    }

    internal GraphMediaWorkTransitionV1 ReconcileCleanup(StableId128 cleanupId,
        Hash256 requestHash, bool succeeded, Hash256 evidenceHash)
    {
        if (evidenceHash == default) return Fail(GraphMediaWorkResultV1.InvalidRequest);
        if (!_cleanup.TryGetValue(cleanupId, out var row)) return Fail(GraphMediaWorkResultV1.CleanupNotFound);
        if (row.RequestHash != requestHash) return Fail(GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (row.State == GraphMediaCleanupStateV1.Succeeded)
            return Fail(succeeded && row.EvidenceHash == evidenceHash
                ? GraphMediaWorkResultV1.ReconciledSucceeded : GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (row.State == GraphMediaCleanupStateV1.Running && row.EvidenceHash is not null)
            return Fail(!succeeded && row.EvidenceHash == evidenceHash
                ? GraphMediaWorkResultV1.ReconciledRunning : GraphMediaWorkResultV1.ContradictoryDuplicate);
        if (row.State != GraphMediaCleanupStateV1.Unknown) return Fail(GraphMediaWorkResultV1.WrongState);
        var state = succeeded ? GraphMediaCleanupStateV1.Succeeded : GraphMediaCleanupStateV1.Running;
        var result = succeeded ? GraphMediaWorkResultV1.ReconciledSucceeded : GraphMediaWorkResultV1.ReconciledRunning;
        return new(result, WithCleanup(row with { State = state, EvidenceHash = evidenceHash }));
    }

    internal GraphMediaReleaseEligibilityV1 QueryReleaseEligibility(StableId128 residenceId)
    {
        var rows = _work.Values.Where(x => x.ResidenceId.Equals(residenceId)).ToArray();
        if (rows.Length == 0) return GraphMediaReleaseEligibilityV1.NotFound;
        if (rows.Any(x => x.State != GraphMediaWorkStateV1.Terminal))
            return GraphMediaReleaseEligibilityV1.Encumbered;
        return rows.All(work => _cleanup.Values.Where(x => x.WorkId.Equals(work.WorkId))
                .All(x => x.State == GraphMediaCleanupStateV1.Succeeded))
            ? GraphMediaReleaseEligibilityV1.Eligible : GraphMediaReleaseEligibilityV1.Encumbered;
    }

    internal static Hash256 RegistrationHash(GraphMediaWorkRegistrationV1 request,
        GraphMediaControlledResidenceV1 residence)
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

    private GraphMediaWorkTransitionV1 Fail(GraphMediaWorkResultV1 result) => new(result, this);
    private GraphMediaWorkLedgerV1 WithWork(GraphMediaWorkRecordV1 row)
    { var work = new Dictionary<StableId128, GraphMediaWorkRecordV1>(_work) { [row.WorkId] = row }; return Next(work, new(_cleanup)); }
    private GraphMediaWorkLedgerV1 WithCleanup(GraphMediaCleanupRecordV1 row)
    { var cleanup = new Dictionary<StableId128, GraphMediaCleanupRecordV1>(_cleanup) { [row.CleanupId] = row }; return Next(new(_work), cleanup); }
    private GraphMediaWorkLedgerV1 Next(Dictionary<StableId128, GraphMediaWorkRecordV1> work,
        Dictionary<StableId128, GraphMediaCleanupRecordV1> cleanup) => new(Session, GraphGeneration, work, cleanup);

    private static Hash256 ComputeFingerprint(SessionAuthorityStampV1 session, GraphGenerationId graph,
        IEnumerable<GraphMediaWorkRecordV1> work,
        IEnumerable<GraphMediaCleanupRecordV1> cleanup)
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
            if (value is GraphMediaOwnerKeyV1 key)
            {
                var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(4);
                writer.WriteByteString(Canonical(key.Session.LiveSessionId)); writer.WriteByteString(Canonical(key.GraphGeneration));
                writer.WriteByteString(Canonical(key.Session.RuntimeGenerationId)); writer.WriteByteString(Stable(key.MediaId));
                writer.WriteEndArray(); return writer.Encode();
            }
            if (value is GraphMediaBindingV1 media)
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
                LiveSessionId x => x.TryWriteBytes(bytes), RuntimeGenerationId x => x.TryWriteBytes(bytes),
                GraphGenerationId x => x.TryWriteBytes(bytes), ParticipantId x => x.TryWriteBytes(bytes),
                CapacityGrantId x => x.TryWriteBytes(bytes), CapacityPurposeId x => x.TryWriteBytes(bytes), _ => false
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
