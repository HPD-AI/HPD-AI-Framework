namespace HPD.Agent.Authority;

internal sealed class GraphParticipantReservationFoldV1
{
    private readonly SessionAuthorityStampV1 _session;
    private readonly Dictionary<JournalFactId, byte[]> _facts = new(65_536);
    private readonly Dictionary<OperationId, (AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1? Fact, GraphParticipantReservationV1? Reservation, BoundedAscii? Code)> _operations = new(65_536);
    private InvalidHistory? _invalid;
    private bool _completed;
    private long _through;
    private ulong _records;
    private ulong _bytes;
    private JournalPositionV1? _appliedSingleton;
    private JournalPositionV1? _appliedPredecessor;

    private GraphParticipantReservationFoldV1(SessionAuthorityStampV1 session) => _session = session;

    internal static GraphParticipantReservationFoldV1 Create(SessionAuthorityStampV1 session)
    {
        if (!session.IsValid) throw new ArgumentException("A valid session is required.", nameof(session));
        return new(session);
    }

    internal ApplyResultV1 Apply(AuthorityFactEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (_invalid is not null) return _invalid;
        if (_completed) return Fail("safe-code-invalid");
        if (envelope.Position.Session != _session) return Fail("session-mismatch");
        var exact = envelope.PayloadBytes.ToArray();
        if (_facts.TryGetValue(envelope.FactId, out _)) return Fail("duplicate-changed");
        if (envelope.Position.Sequence != _through + 1) return Fail("position-invalid");
        var encodedLength=AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(envelope);
        if (_records == 65_536 || encodedLength > 8_192 || _bytes > 536_870_912UL - encodedLength) return Fail("record-wire-invalid");
        _facts.Add(envelope.FactId, exact); _through++; _records++; _bytes += encodedLength;

        var commandSchema = GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand;
        var factSchema = GraphParticipantBindingPayloadRegistrationsV1.ReservationFact;
        if (envelope.PayloadSchema != commandSchema.Schema && envelope.PayloadSchema != factSchema.Schema) return new Accepted();
        if (envelope.ThreadScope is not null || envelope.Owner != OwnerSliceId.S1 || envelope.PayloadHash != AuthorityPayloadHashV1.Compute(
                envelope.PayloadSchema == commandSchema.Schema ? commandSchema.SchemaToken : factSchema.SchemaToken,
                envelope.PayloadSchema, exact)) return Fail("record-wire-invalid");

        if (envelope.PayloadSchema == commandSchema.Schema)
        {
            if (!GraphParticipantBindingCodecsV1.TryDecodeReservationCommand(exact, out var outer) || outer is null ||
                !GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(outer.BodyBytes.ToArray(), out var body) || body is null)
                return Fail("record-wire-invalid");
            if (outer.Session != _session || outer.ExpectedAuthority.Session != _session) return Fail("session-mismatch");
            if (GraphParticipantBindingFactIdsV1.ReservationCommand(_session, body.OperationId) != envelope.FactId) return Fail("fact-id-mismatch");
            if (_operations.ContainsKey(body.OperationId)) return Fail("duplicate-changed");
            _operations.Add(body.OperationId, (envelope, null, null, null));
            return new Accepted();
        }

        if (!GraphParticipantBindingCodecsV1.TryDecodeReservationFact(exact, out var factOuter) || factOuter is null ||
            !GraphParticipantBindingCodecsV1.TryDecodeReservationFactBody(factOuter.BodyBytes.ToArray(), out var fact) || fact is null)
            return Fail("record-wire-invalid");
        if (factOuter.Session != _session || factOuter.ExpectedAuthority.Session != _session || fact.CommandPosition.Session != _session)
            return Fail("session-mismatch");
        if (!_operations.TryGetValue(fact.OperationId, out var entry) || entry.Command.Position != fact.CommandPosition || entry.Fact is not null ||
            GraphParticipantBindingFactIdsV1.ReservationFact(fact.CommandPosition) != envelope.FactId)
            return Fail("command-fact-join-invalid");
        if(envelope.Correlation!=entry.Command.Correlation||envelope.ObservedAt!=entry.Command.ObservedAt)return Fail("command-fact-join-invalid");
        if(!GraphParticipantBindingCodecsV1.TryDecodeReservationCommand(entry.Command.PayloadMemory,out var storedOuter)||storedOuter is null||!GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(storedOuter.BodyBytes.ToArray(),out var stored)||stored is null||factOuter.ExpectedAuthority!=storedOuter.ExpectedAuthority||fact.ActualPredecessor!=stored.ExpectedReservationFact||fact.ActualPredecessor!=_appliedSingleton||fact.RuntimeGeneration!=stored.RuntimeGeneration||fact.ParticipantPlanFingerprint!=stored.ParticipantPlanFingerprint||fact.TopologyFingerprint!=stored.TopologyFingerprint||fact.ExecutablePlanFingerprint!=stored.ExecutablePlanFingerprint||fact.ObservedAt!=stored.ObservedAt)return Fail("command-fact-join-invalid");
        if (fact.Outcome == 1)
        {
            if (fact.Reservation is null || fact.SafeCode is not null || _appliedSingleton is not null || fact.Reservation.ParticipantFactoryKey!=stored.ParticipantFactoryKey || !fact.Reservation.OrderedTopologyNodeKeys.SequenceEqual(stored.OrderedTopologyNodeKeys)) return Fail("singleton-duplicate");
            _appliedSingleton = envelope.Position;
            _appliedPredecessor = fact.ActualPredecessor;
            _operations[fact.OperationId] = (entry.Command, envelope, fact.Reservation, null);
        }
        else if (fact.Outcome == 2 && fact.Reservation is null && fact.SafeCode?.ToString()=="participant-id-collision")
            _operations[fact.OperationId] = (entry.Command, envelope, null, fact.SafeCode);
        else return Fail("outcome-invalid");
        return new Accepted();
    }

    internal Completed Complete()
    {
        _completed = true;
        return new(_session, _through, _records, _bytes,_appliedSingleton,_appliedPredecessor);
    }

    internal QueryResultV1 Query(OperationId operationId)
    {
        if (!_completed || _invalid is not null || !operationId.IsValid) throw new InvalidOperationException("Only a valid completed history is queryable.");
        if (!_operations.TryGetValue(operationId, out var x)) return new NotFound();
        if (x.Fact is null) return new CommandOnly(x.Command);
        return x.Reservation is not null ? new AppliedReservation(x.Command, x.Fact, x.Reservation) : new RejectedReservation(x.Command, x.Fact, x.Code!.Value);
    }

    private InvalidHistory Fail(string code) => _invalid ??= new(new BoundedAscii(code));

    internal abstract record ApplyResultV1;
    internal sealed record Accepted : ApplyResultV1;
    internal sealed record InvalidHistory(BoundedAscii SafeCode) : ApplyResultV1;
    internal sealed record Completed(SessionAuthorityStampV1 Session, long SnapshotThrough, ulong RecordCount, ulong TotalCanonicalBytes,JournalPositionV1? AppliedReservationFact,JournalPositionV1? AppliedReservationPredecessor);
    internal abstract record QueryResultV1;
    internal sealed record NotFound : QueryResultV1;
    internal sealed record CommandOnly(AuthorityFactEnvelopeV1 Command) : QueryResultV1;
    internal sealed record AppliedReservation(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact, GraphParticipantReservationV1 Reservation) : QueryResultV1;
    internal sealed record RejectedReservation(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact, BoundedAscii SafeCode) : QueryResultV1;
}
