using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed class GraphMediaWorkExecutionFoldV1
{
    private readonly SessionAuthorityStampV1 _session;
    private readonly StableId128 _residenceId;
    private readonly AuthorityPayloadAdmissionRegistryV1 _registry;
    private readonly Dictionary<long, AuthorityFactEnvelopeV1> _positions = [];
    private readonly Dictionary<JournalFactId, AuthorityFactEnvelopeV1> _facts = [];
    private readonly Dictionary<OperationId, Entry> _operations = [];
    private GraphMediaWorkExecutionFoldApplyResultV1.InvalidHistory? _invalid;
    private JournalPositionV1? _predecessor;
    private bool _completed;
    private long _through;
    private ulong _records;
    private ulong _bytes;
    private int _targetRecords;

    private GraphMediaWorkExecutionFoldV1(SessionAuthorityStampV1 session, StableId128 residenceId,
        AuthorityPayloadAdmissionRegistryV1 registry)
    { _session = session; _residenceId = residenceId; _registry = registry; }

    internal static GraphMediaWorkExecutionFoldV1 Create(SessionAuthorityStampV1 session,
        StableId128 residenceId, AuthorityPayloadAdmissionRegistryV1 registry)
    {
        if (!session.IsValid) throw new ArgumentException("A valid session is required.", nameof(session));
        if (residenceId.Equals(default(StableId128))) throw new ArgumentException("A valid residence is required.", nameof(residenceId));
        ArgumentNullException.ThrowIfNull(registry);
        return new(session, residenceId, registry);
    }

    internal GraphMediaWorkExecutionFoldApplyResultV1 Apply(AuthorityFactEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (_invalid is not null) return _invalid;
        if (_completed) return Fail("post-terminal-record");
        if (envelope.Position.Session != _session) return Fail("session-mismatch");
        if (_positions.TryGetValue(envelope.Position.Sequence, out var atPosition))
            return SameEnvelope(atPosition, envelope)
                ? IsTarget(envelope) ? new GraphMediaWorkExecutionFoldApplyResultV1.Applied(true) : new GraphMediaWorkExecutionFoldApplyResultV1.Ignored(true)
                : Fail("position-invalid");
        if (_facts.TryGetValue(envelope.FactId, out var withFactId))
            return SameEnvelope(withFactId, envelope)
                ? IsTarget(envelope) ? new GraphMediaWorkExecutionFoldApplyResultV1.Applied(true) : new GraphMediaWorkExecutionFoldApplyResultV1.Ignored(true)
                : Fail("fact-id-mismatch");
        if (envelope.Position.Sequence != _through + 1) return Fail("position-invalid");

        var encodedLength = AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(envelope);
        if (_records == 65_536 || encodedLength > 1_048_576 || _bytes > 67_108_864UL - (ulong)encodedLength)
            return Fail("record-wire-invalid");
        _through++;
        _records++;
        _bytes += (ulong)encodedLength;
        _positions.Add(envelope.Position.Sequence, envelope);
        _facts.Add(envelope.FactId, envelope);

        var proposal = new ProposedAuthorityFactV1(envelope.FactId, envelope.ThreadScope?.ThreadId,
            envelope.Owner, envelope.PayloadSchema, envelope.PayloadBytes, envelope.PayloadHash,
            envelope.Correlation, envelope.ObservedAt);
        if (_registry.Validate(_session, proposal, out _) != AuthorityPayloadAdmissionV1.Exact)
            return Fail("record-wire-invalid");
        if (!IsTarget(envelope)) return new GraphMediaWorkExecutionFoldApplyResultV1.Ignored(false);
        if (++_targetRecords > GraphMediaWorkLedgerV1.MaximumWorkPerRuntime * 2 ||
            envelope.Owner != OwnerSliceId.S1 || envelope.ThreadScope is not null)
            return Fail("record-wire-invalid");
        return envelope.PayloadSchema == GraphMediaWorkExecutionPayloadRegistrationsV1.Command.Schema
            ? ApplyCommand(envelope)
            : ApplyFact(envelope);
    }

    internal GraphMediaWorkExecutionFoldResultV1 Complete()
    {
        _completed = true;
        if (_invalid is not null) return new GraphMediaWorkExecutionFoldResultV1.InvalidHistory(_invalid.SafeCode);
        if (_operations.Count == 0) return new GraphMediaWorkExecutionFoldResultV1.NotFound(_records, _bytes);
        var entry = _operations.Values.OrderBy(x => x.Command.Position.Sequence).Last();
        return Result(entry);
    }

    internal GraphMediaWorkExecutionFoldResultV1 Query(OperationId operationId)
    {
        if (!operationId.IsValid) throw new ArgumentException("A valid operation is required.", nameof(operationId));
        _completed = true;
        if (_invalid is not null) return new GraphMediaWorkExecutionFoldResultV1.InvalidHistory(_invalid.SafeCode);
        return _operations.TryGetValue(operationId, out var entry)
            ? Result(entry)
            : new GraphMediaWorkExecutionFoldResultV1.NotFound(_records, _bytes);
    }

    internal GraphMediaWorkExecutionFoldSnapshotV1 Snapshot()
    {
        _completed = true;
        if (_invalid is not null) return new GraphMediaWorkExecutionFoldSnapshotV1.InvalidHistory(_invalid.SafeCode);
        var entries = _operations.Values.OrderBy(x => x.Command.Position.Sequence).Select(Result).ToArray();
        return new GraphMediaWorkExecutionFoldSnapshotV1.Current(entries, _records, _bytes);
    }

    private GraphMediaWorkExecutionFoldResultV1 Result(Entry entry)
    {
        if (entry.Fact is null)
            return new GraphMediaWorkExecutionFoldResultV1.CommandOnly(entry.Command, entry.CommandBody, _records, _bytes);
        return entry.FactBody!.Outcome switch
        {
            GraphMediaWorkExecutionOutcomeV1.Completed => new GraphMediaWorkExecutionFoldResultV1.Completed(
                entry.Command, entry.Fact, entry.CommandBody, entry.FactBody, entry.FactBody.EvidenceHash!.Value, _records, _bytes),
            GraphMediaWorkExecutionOutcomeV1.Unknown => new GraphMediaWorkExecutionFoldResultV1.Unknown(
                entry.Command, entry.Fact, entry.CommandBody, entry.FactBody, _records, _bytes),
            GraphMediaWorkExecutionOutcomeV1.Rejected => new GraphMediaWorkExecutionFoldResultV1.Rejected(
                entry.Command, entry.Fact, entry.CommandBody, entry.FactBody, entry.FactBody.SafeCode!.Value, _records, _bytes),
            _ => new GraphMediaWorkExecutionFoldResultV1.InvalidHistory(new BoundedAscii("outcome-invalid"))
        };
    }

    private GraphMediaWorkExecutionFoldApplyResultV1 ApplyCommand(AuthorityFactEnvelopeV1 envelope)
    {
        if (!GraphMediaWorkExecutionCodecsV1.TryDecodeOuter(envelope.PayloadMemory, out var outer) || outer is null ||
            !GraphMediaWorkExecutionCodecsV1.TryDecodeCommandBody(outer.BodyMemory, out var body) || body is null)
            return Fail("record-wire-invalid");
        if (outer.Session != _session || outer.ExpectedAuthority.Session != _session) return Fail("session-mismatch");
        if (!body.Work.ResidenceId.Equals(_residenceId)) return Fail("operation-conflict");
        if (envelope.Correlation.OperationId != body.OperationId) return Fail("operation-conflict");
        if (GraphMediaWorkExecutionFactIdsV1.Command(_session, body.OperationId) != envelope.FactId)
            return Fail("fact-id-mismatch");
        if (_operations.ContainsKey(body.OperationId)) return Fail("operation-conflict");
        if (_operations.Count >= GraphMediaWorkLedgerV1.MaximumWorkPerRuntime)
            return Fail("record-wire-invalid");
        if (_operations.Values.Any(x => x.CommandBody.Work.WorkId.Equals(body.Work.WorkId) &&
            x.FactBody?.Outcome is GraphMediaWorkExecutionOutcomeV1.Completed or GraphMediaWorkExecutionOutcomeV1.Unknown))
            return Fail("operation-conflict");
        if (_operations.Values.Any(x => x.Fact is null) || body.ExpectedWorkFact != _predecessor)
            return Fail("predecessor-conflict");
        _operations.Add(body.OperationId, new Entry(envelope, body, null, null));
        return new GraphMediaWorkExecutionFoldApplyResultV1.Applied(false);
    }

    private GraphMediaWorkExecutionFoldApplyResultV1 ApplyFact(AuthorityFactEnvelopeV1 envelope)
    {
        if (!GraphMediaWorkExecutionCodecsV1.TryDecodeOuter(envelope.PayloadMemory, out var outer) || outer is null ||
            !GraphMediaWorkExecutionCodecsV1.TryDecodeFactBody(outer.BodyMemory, out var body) || body is null)
            return Fail("record-wire-invalid");
        if (outer.Session != _session || outer.ExpectedAuthority.Session != _session || body.CommandPosition.Session != _session)
            return Fail("session-mismatch");
        var operation = _operations.SingleOrDefault(x => x.Value.Command.Position == body.CommandPosition);
        if (!operation.Key.IsValid) return Fail("fact-without-command");
        var entry = operation.Value;
        if (entry.Fact is not null || GraphMediaWorkExecutionFactIdsV1.Fact(body.CommandPosition) != envelope.FactId)
            return Fail("command-fact-join-invalid");
        if (!GraphMediaWorkExecutionCodecsV1.TryDecodeOuter(entry.Command.PayloadMemory, out var commandOuter) || commandOuter is null ||
            outer.ExpectedAuthority != commandOuter.ExpectedAuthority || envelope.Correlation != entry.Command.Correlation ||
            envelope.ObservedAt != entry.Command.ObservedAt || body.ObservedAt != entry.CommandBody.ObservedAt ||
            !body.WorkId.Equals(entry.CommandBody.Work.WorkId) || body.WorkRequestHash != entry.CommandBody.Work.RequestHash)
            return Fail("command-fact-join-invalid");
        _operations[operation.Key] = entry with { Fact = envelope, FactBody = body };
        _predecessor = envelope.Position;
        return new GraphMediaWorkExecutionFoldApplyResultV1.Applied(false);
    }

    private GraphMediaWorkExecutionFoldApplyResultV1.InvalidHistory Fail(string code) =>
        _invalid ??= new(new BoundedAscii(code));

    private static bool IsTarget(AuthorityFactEnvelopeV1 envelope) =>
        envelope.PayloadSchema == GraphMediaWorkExecutionPayloadRegistrationsV1.Command.Schema ||
        envelope.PayloadSchema == GraphMediaWorkExecutionPayloadRegistrationsV1.Fact.Schema;

    private static bool SameEnvelope(AuthorityFactEnvelopeV1 left, AuthorityFactEnvelopeV1 right) =>
        left.FactId == right.FactId && left.Position == right.Position && left.ThreadScope == right.ThreadScope &&
        left.Owner == right.Owner && left.PayloadSchema == right.PayloadSchema && left.PayloadHash == right.PayloadHash &&
        left.Correlation == right.Correlation && left.ObservedAt == right.ObservedAt && left.AdmittedAt == right.AdmittedAt &&
        left.PayloadBytes.SequenceEqual(right.PayloadBytes) && left.Integrity.Profile == right.Integrity.Profile &&
        left.Integrity.KeyVersion == right.Integrity.KeyVersion && left.Integrity.Digest == right.Integrity.Digest &&
        left.Integrity.SignatureBytes.SequenceEqual(right.Integrity.SignatureBytes);

    private sealed record Entry(AuthorityFactEnvelopeV1 Command, GraphMediaWorkExecutionCommandBodyV1 CommandBody,
        AuthorityFactEnvelopeV1? Fact, GraphMediaWorkExecutionFactBodyV1? FactBody);
}

internal abstract record GraphMediaWorkExecutionFoldApplyResultV1
{
    private GraphMediaWorkExecutionFoldApplyResultV1() { }
    internal sealed record Applied(bool Duplicate) : GraphMediaWorkExecutionFoldApplyResultV1;
    internal sealed record Ignored(bool Duplicate) : GraphMediaWorkExecutionFoldApplyResultV1;
    internal sealed record InvalidHistory(BoundedAscii SafeCode) : GraphMediaWorkExecutionFoldApplyResultV1;
}

internal abstract record GraphMediaWorkExecutionFoldResultV1
{
    private GraphMediaWorkExecutionFoldResultV1() { }
    internal sealed record NotFound(ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaWorkExecutionFoldResultV1;
    internal sealed record CommandOnly(AuthorityFactEnvelopeV1 Command, GraphMediaWorkExecutionCommandBodyV1 Body,
        ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaWorkExecutionFoldResultV1;
    internal sealed record Completed(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        GraphMediaWorkExecutionCommandBodyV1 CommandBody, GraphMediaWorkExecutionFactBodyV1 FactBody,
        Hash256 EvidenceHash, ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaWorkExecutionFoldResultV1;
    internal sealed record Unknown(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        GraphMediaWorkExecutionCommandBodyV1 CommandBody, GraphMediaWorkExecutionFactBodyV1 FactBody,
        ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaWorkExecutionFoldResultV1;
    internal sealed record Rejected(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        GraphMediaWorkExecutionCommandBodyV1 CommandBody, GraphMediaWorkExecutionFactBodyV1 FactBody,
        BoundedAscii SafeCode, ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaWorkExecutionFoldResultV1;
    internal sealed record InvalidHistory(BoundedAscii SafeCode) : GraphMediaWorkExecutionFoldResultV1;
}

internal abstract record GraphMediaWorkExecutionFoldSnapshotV1
{
    private GraphMediaWorkExecutionFoldSnapshotV1() { }
    internal sealed record Current : GraphMediaWorkExecutionFoldSnapshotV1
    {
        private readonly GraphMediaWorkExecutionFoldResultV1[] _operations;
        internal Current(IReadOnlyList<GraphMediaWorkExecutionFoldResultV1> operations,
            ulong recordCount, ulong totalCanonicalRecordBytes)
        {
            ArgumentNullException.ThrowIfNull(operations);
            _operations = operations.ToArray();
            Operations = Array.AsReadOnly(_operations);
            RecordCount = recordCount;
            TotalCanonicalRecordBytes = totalCanonicalRecordBytes;
        }
        internal IReadOnlyList<GraphMediaWorkExecutionFoldResultV1> Operations { get; }
        internal ulong RecordCount { get; }
        internal ulong TotalCanonicalRecordBytes { get; }
    }
    internal sealed record InvalidHistory(BoundedAscii SafeCode) : GraphMediaWorkExecutionFoldSnapshotV1;
}
