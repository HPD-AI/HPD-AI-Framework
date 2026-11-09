using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed class GraphMediaPhysicalReleaseFold
{
    private readonly SessionAuthorityStampV1 _session;
    private readonly StableId128 _residenceId;
    private readonly AuthorityPayloadAdmissionRegistryV1 _registry;
    private readonly Dictionary<long, AuthorityFactEnvelopeV1> _positions = [];
    private readonly Dictionary<JournalFactId, AuthorityFactEnvelopeV1> _facts = [];
    private readonly Dictionary<OperationId, (AuthorityFactEnvelopeV1 Command,
        GraphMediaPhysicalReleaseCommandBody CommandBody, AuthorityFactEnvelopeV1? Fact,
        GraphMediaPhysicalReleaseFactBody? FactBody)> _operations = [];
    private GraphMediaPhysicalReleaseFoldApplyResult.InvalidHistory? _invalid;
    private JournalPositionV1? _predecessor;
    private bool _terminal;
    private bool _completed;
    private long _through;
    private ulong _records;
    private ulong _bytes;
    private int _targetRecords;

    private GraphMediaPhysicalReleaseFold(SessionAuthorityStampV1 session, StableId128 residenceId,
        AuthorityPayloadAdmissionRegistryV1 registry)
    { _session = session; _residenceId = residenceId; _registry = registry; }

    internal static GraphMediaPhysicalReleaseFold Create(SessionAuthorityStampV1 session,
        StableId128 residenceId, AuthorityPayloadAdmissionRegistryV1 registry)
    {
        if (!session.IsValid) throw new ArgumentException("A valid session is required.", nameof(session));
        if (residenceId.Equals(default(StableId128))) throw new ArgumentException("A valid residence is required.", nameof(residenceId));
        ArgumentNullException.ThrowIfNull(registry);
        return new(session, residenceId, registry);
    }

    internal GraphMediaPhysicalReleaseFoldApplyResult Apply(AuthorityFactEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (_invalid is not null) return _invalid;
        if (_completed) return Fail("post-terminal-record");
        if (envelope.Position.Session != _session) return Fail("session-mismatch");

        if (_positions.TryGetValue(envelope.Position.Sequence, out var positioned))
            return SameEnvelope(positioned, envelope)
                ? IsTarget(envelope) ? new GraphMediaPhysicalReleaseFoldApplyResult.Applied(true) : new GraphMediaPhysicalReleaseFoldApplyResult.Ignored(true)
                : Fail("position-invalid");
        if (_facts.TryGetValue(envelope.FactId, out var identified))
            return SameEnvelope(identified, envelope)
                ? IsTarget(envelope) ? new GraphMediaPhysicalReleaseFoldApplyResult.Applied(true) : new GraphMediaPhysicalReleaseFoldApplyResult.Ignored(true)
                : Fail("record-wire-invalid");
        if (envelope.Position.Sequence != _through + 1) return Fail("position-invalid");

        var encodedLength = AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(envelope);
        if (_records == 65_536 || encodedLength > 1_048_576 || _bytes > 67_108_864UL - (ulong)encodedLength)
            return Fail("record-wire-invalid");
        _through++;
        _records++;
        _bytes += (ulong)encodedLength;
        _positions.Add(envelope.Position.Sequence, envelope);
        _facts.Add(envelope.FactId, envelope);

        if (!IsTarget(envelope))
        {
            var proposal = new ProposedAuthorityFactV1(envelope.FactId, envelope.ThreadScope?.ThreadId,
                envelope.Owner, envelope.PayloadSchema, envelope.PayloadBytes, envelope.PayloadHash,
                envelope.Correlation, envelope.ObservedAt);
            return _registry.Validate(_session, proposal, out _) == AuthorityPayloadAdmissionV1.Exact
                ? new GraphMediaPhysicalReleaseFoldApplyResult.Ignored(false)
                : Fail("record-wire-invalid");
        }
        if (_terminal) return Fail("post-terminal-record");
        if (++_targetRecords > 16) return Fail("record-wire-invalid");
        if (envelope.Owner != OwnerSliceId.S1 || envelope.ThreadScope is not null) return Fail("record-wire-invalid");

        var registration = envelope.PayloadSchema == GraphMediaPhysicalReleasePayloadRegistrations.Command.Schema
            ? GraphMediaPhysicalReleasePayloadRegistrations.Command
            : GraphMediaPhysicalReleasePayloadRegistrations.Fact;
        var proposalTarget = new ProposedAuthorityFactV1(envelope.FactId, null, envelope.Owner,
            envelope.PayloadSchema, envelope.PayloadBytes, envelope.PayloadHash, envelope.Correlation, envelope.ObservedAt);
        var admission = ReferenceEquals(registration, GraphMediaPhysicalReleasePayloadRegistrations.Command)
            ? GraphMediaPhysicalReleasePayloadRegistrations.ValidateCommandEnvelope(_session, proposalTarget)
            : GraphMediaPhysicalReleasePayloadRegistrations.ValidateFactEnvelope(_session, proposalTarget);
        if (admission != AuthorityPayloadAdmissionV1.Exact)
            return Fail("record-wire-invalid");
        return ReferenceEquals(registration, GraphMediaPhysicalReleasePayloadRegistrations.Command)
            ? ApplyCommand(envelope)
            : ApplyFact(envelope);
    }

    internal GraphMediaPhysicalReleaseFoldResult Complete()
    {
        _completed = true;
        if (_invalid is not null) return new GraphMediaPhysicalReleaseFoldResult.InvalidHistory(_invalid.SafeCode);
        if (_operations.Count == 0) return new GraphMediaPhysicalReleaseFoldResult.NotFound(_records, _bytes);
        var entry = _operations.Values.OrderBy(x => x.Command.Position.Sequence).Last();
        if (entry.Fact is null) return new GraphMediaPhysicalReleaseFoldResult.CommandOnly(entry.Command, entry.CommandBody, _records, _bytes);
        var fact = entry.FactBody!;
        return fact.Outcome switch
        {
            GraphMediaPhysicalReleaseOutcome.Released => new GraphMediaPhysicalReleaseFoldResult.Released(entry.Command, entry.Fact, entry.CommandBody, fact, fact.EvidenceHash!.Value, _records, _bytes),
            GraphMediaPhysicalReleaseOutcome.Unknown => new GraphMediaPhysicalReleaseFoldResult.Unknown(entry.Command, entry.Fact, entry.CommandBody, fact, _records, _bytes),
            GraphMediaPhysicalReleaseOutcome.Rejected => new GraphMediaPhysicalReleaseFoldResult.Rejected(entry.Command, entry.Fact, entry.CommandBody, fact, fact.SafeCode!.Value, _records, _bytes),
            _ => new GraphMediaPhysicalReleaseFoldResult.InvalidHistory(new BoundedAscii("outcome-invalid"))
        };
    }

    private GraphMediaPhysicalReleaseFoldApplyResult ApplyCommand(AuthorityFactEnvelopeV1 envelope)
    {
        if (!GraphMediaPhysicalReleaseCodecs.TryDecodeOuter(envelope.PayloadMemory, out var outer) || outer is null ||
            !GraphMediaPhysicalReleaseCodecs.TryDecodeCommandBody(outer.BodyMemory, out var body) || body is null)
            return Fail("record-wire-invalid");
        if (outer.Session != _session || outer.ExpectedAuthority.Session != _session) return Fail("session-mismatch");
        if (!body.Residence.ResidenceId.Equals(_residenceId)) return Fail("operation-conflict");
        if (GraphMediaPhysicalReleaseFactIds.Command(_session, body.OperationId) != envelope.FactId) return Fail("record-wire-invalid");
        if (_operations.ContainsKey(body.OperationId)) return Fail("operation-conflict");
        if (_operations.Count >= 8) return Fail("record-wire-invalid");
        if (_operations.Values.Any(x => x.Fact is null)) return Fail("predecessor-conflict");
        if (body.ExpectedReleaseFact != _predecessor) return Fail("predecessor-conflict");
        _operations.Add(body.OperationId, (envelope, body, null, null));
        return new GraphMediaPhysicalReleaseFoldApplyResult.Applied(false);
    }

    private GraphMediaPhysicalReleaseFoldApplyResult ApplyFact(AuthorityFactEnvelopeV1 envelope)
    {
        if (!GraphMediaPhysicalReleaseCodecs.TryDecodeOuter(envelope.PayloadMemory, out var outer) || outer is null ||
            !GraphMediaPhysicalReleaseCodecs.TryDecodeFactBody(outer.BodyMemory, out var body) || body is null)
            return Fail("record-wire-invalid");
        if (outer.Session != _session || outer.ExpectedAuthority.Session != _session || body.CommandPosition.Session != _session)
            return Fail("session-mismatch");
        var match = _operations.SingleOrDefault(x => x.Value.Command.Position == body.CommandPosition);
        if (!match.Key.IsValid) return Fail("fact-without-command");
        var entry = match.Value;
        if (entry.Fact is not null || GraphMediaPhysicalReleaseFactIds.Fact(body.CommandPosition) != envelope.FactId)
            return Fail("command-fact-join-invalid");
        if (!GraphMediaPhysicalReleaseCodecs.TryDecodeOuter(entry.Command.PayloadMemory, out var commandOuter) || commandOuter is null)
            return Fail("command-fact-join-invalid");
        var command = entry.CommandBody;
        if (outer.ExpectedAuthority != commandOuter.ExpectedAuthority || envelope.Correlation != entry.Command.Correlation ||
            envelope.ObservedAt != entry.Command.ObservedAt || body.ObservedAt != command.ObservedAt ||
            !body.ResidenceId.Equals(command.Residence.ResidenceId) || body.ResidenceRequestHash != command.Residence.RequestHash ||
            body.GrantId != command.Residence.GrantId || body.CurrentFact != command.Residence.CurrentFact ||
            body.Assignment != command.Residence.Assignment)
            return Fail("command-fact-join-invalid");
        _operations[match.Key] = (entry.Command, entry.CommandBody, envelope, body);
        _predecessor = envelope.Position;
        if (body.Outcome is GraphMediaPhysicalReleaseOutcome.Released or GraphMediaPhysicalReleaseOutcome.Unknown)
            _terminal = true;
        return new GraphMediaPhysicalReleaseFoldApplyResult.Applied(false);
    }

    private GraphMediaPhysicalReleaseFoldApplyResult.InvalidHistory Fail(string code) =>
        _invalid ??= new(new BoundedAscii(code));

    private static bool SameEnvelope(AuthorityFactEnvelopeV1 left, AuthorityFactEnvelopeV1 right) =>
        left.FactId == right.FactId && left.Position == right.Position && left.ThreadScope == right.ThreadScope &&
        left.Owner == right.Owner && left.PayloadSchema == right.PayloadSchema && left.PayloadHash == right.PayloadHash &&
        left.Correlation == right.Correlation && left.ObservedAt == right.ObservedAt && left.AdmittedAt == right.AdmittedAt &&
        left.PayloadBytes.SequenceEqual(right.PayloadBytes) && left.Integrity.Profile == right.Integrity.Profile &&
        left.Integrity.KeyVersion == right.Integrity.KeyVersion && left.Integrity.Digest == right.Integrity.Digest &&
        left.Integrity.SignatureBytes.SequenceEqual(right.Integrity.SignatureBytes);

    private static bool IsTarget(AuthorityFactEnvelopeV1 envelope) =>
        envelope.PayloadSchema == GraphMediaPhysicalReleasePayloadRegistrations.Command.Schema ||
        envelope.PayloadSchema == GraphMediaPhysicalReleasePayloadRegistrations.Fact.Schema;

}

internal abstract record GraphMediaPhysicalReleaseFoldApplyResult
{
    private GraphMediaPhysicalReleaseFoldApplyResult() { }
    internal sealed record Applied(bool Duplicate) : GraphMediaPhysicalReleaseFoldApplyResult;
    internal sealed record Ignored(bool Duplicate) : GraphMediaPhysicalReleaseFoldApplyResult;
    internal sealed record InvalidHistory(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseFoldApplyResult;
}

internal abstract record GraphMediaPhysicalReleaseFoldResult
{
    private GraphMediaPhysicalReleaseFoldResult() { }
    internal sealed record NotFound(ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaPhysicalReleaseFoldResult;
    internal sealed record CommandOnly(AuthorityFactEnvelopeV1 Command, GraphMediaPhysicalReleaseCommandBody Body, ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaPhysicalReleaseFoldResult;
    internal sealed record Released(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact, GraphMediaPhysicalReleaseCommandBody CommandBody, GraphMediaPhysicalReleaseFactBody FactBody, Hash256 EvidenceHash, ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaPhysicalReleaseFoldResult;
    internal sealed record Unknown(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact, GraphMediaPhysicalReleaseCommandBody CommandBody, GraphMediaPhysicalReleaseFactBody FactBody, ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaPhysicalReleaseFoldResult;
    internal sealed record Rejected(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact, GraphMediaPhysicalReleaseCommandBody CommandBody, GraphMediaPhysicalReleaseFactBody FactBody, BoundedAscii SafeCode, ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaPhysicalReleaseFoldResult;
    internal sealed record InvalidHistory(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseFoldResult;
}
