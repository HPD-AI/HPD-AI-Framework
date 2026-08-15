using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests.Authority;

public sealed class GraphParticipantReservationCoordinatorV1Tests
{
    [Fact]
    public void Request_owns_bytes_and_checks_bounds()
    {
        var operation = OperationId.FromValue(Id(1)); var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(2)), LiveSessionId.FromValue(Id(3))); var stamp = new MonotonicStampV1(ClockDomainId.FromValue(Id(4)), BootId.FromValue(Id(5)), 1); var body = new GraphParticipantReservationCommandBodyV1(operation, null, session.RuntimeGenerationId, Hash256.Compute([1]), Hash256.Compute([2]), Hash256.Compute([3]), new("factory"), [new("node")], stamp); var bytes = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(session, ExpectedAuthorityVectorV1.Create(session, []), GraphParticipantBindingCodecsV1.Encode(body))); var correlation = new CorrelationEnvelopeV1(TenantId.FromValue(Id(6)), operationId: operation);
        var request = new GraphParticipantReservationRequestV1(bytes, correlation, new UtcInstant(long.MinValue), 1, 1);
        bytes[0] = 9;
        Assert.NotEqual(9, request.ExactCanonicalCommandBytes.Span[0]);
        Assert.Equal(long.MinValue, request.ObservedAt.NanosecondsSinceUnixEpoch);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphParticipantReservationRequestV1(default, correlation, default, 1, 1));
        Assert.Throws<ArgumentException>(() => new GraphParticipantReservationRequestV1(request.ExactCanonicalCommandBytes, new CorrelationEnvelopeV1(TenantId.FromValue(Id(7))), default, 1, 1));
    }

    [Fact]
    public void Result_arms_are_closed_and_fact_bytes_are_owned()
    {
        Assert.IsAssignableFrom<GraphParticipantReservationResultV1>(new GraphParticipantReservationResultV1.RetryRequired(new BoundedAscii("allocator-head-advanced")));
        Assert.IsAssignableFrom<GraphParticipantReservationResultV1>(new GraphParticipantReservationResultV1.StoreUnavailable(new BoundedAscii("allocator-store-unavailable")));
        Assert.IsAssignableFrom<GraphParticipantReservationResultV1>(new GraphParticipantReservationResultV1.Quarantined(new BoundedAscii("allocator-record-invalid")));
    }
    [Fact]
    public async Task Scripted_read_arms_and_allocator_arms_are_closed()
    {
        var journal = new ScriptedAuthorityJournal(); var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(8)), LiveSessionId.FromValue(Id(9)));
        journal.Reads.Enqueue(new ReadAuthorityRangeResultV1.Batch(session, 0, 0, 0, [], false));
        Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await journal.ReadAsync(new(session, 0, 1, 1, 1)));
        var ports = new ScriptedAllocatorPorts(); ports.Snapshots.Enqueue(new GlobalParticipantAllocatorDurableSnapshotResultV1.StoreUnavailable(new("fixture")));
        Assert.IsType<GlobalParticipantAllocatorDurableSnapshotResultV1.StoreUnavailable>(await ports.ReadAsync(null!, default));
        Assert.Equal(0, journal.AppendCalls);
    }

    [Fact]
    public async Task Empty_history_commits_command_claim_and_result_end_to_end()
    {
        var operation = OperationId.FromValue(Id(20));
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(21)), LiveSessionId.FromValue(Id(22)));
        var expected = ExpectedAuthorityVectorV1.Create(session, []);
        var stamp = new MonotonicStampV1(ClockDomainId.FromValue(Id(23)), BootId.FromValue(Id(24)), 7);
        var body = new GraphParticipantReservationCommandBodyV1(operation, null, session.RuntimeGenerationId, Hash256.Compute([31]), Hash256.Compute([32]), Hash256.Compute([33]), new("factory"), [new("node")], stamp);
        var commandBytes = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(session, expected, GraphParticipantBindingCodecsV1.Encode(body)));
        Assert.True(GraphParticipantBindingCodecsV1.TryDecodeReservationCommand(commandBytes, out var decodedOuter));
        Assert.True(GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(decodedOuter!.BodyBytes.ToArray(), out var decodedBody));
        Assert.Null(decodedBody!.ExpectedReservationFact);
        var correlation = new CorrelationEnvelopeV1(TenantId.FromValue(Id(25)), operationId: operation);
        var request = new GraphParticipantReservationRequestV1(commandBytes, correlation, new UtcInstant(99), 32, 1_048_576);
        var registry = new AuthorityPayloadAdmissionRegistryV1([GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand, GraphParticipantBindingPayloadRegistrationsV1.ReservationFact]);
        var innerJournal = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(100), new AuthorityJournalCapacityV1(1, 32, 2_000_000));
        var journal = new ScriptedAuthorityJournal { Inner = innerJournal, FailReadsAfter = 2 };
        var allocatorJournal = GlobalParticipantAllocatorJournalId.FromValue(Id(26));
        var store = Hash256.Compute([34]);
        var created = new UtcInstant(35);
        var manifest = new GlobalParticipantAllocatorRealmManifestV1(allocatorJournal, 1, 1, store, created, GlobalParticipantAllocatorRealmManifestV1.ComputeManifestHash(allocatorJournal, 1, 1, store, created));
        var lease = new GlobalParticipantAllocatorRealmLeaseV1(manifest, new TestRealmCustody());
        ReadOnlyMemory<byte> retainedAllocatorRecord = default; GlobalParticipantAuthorityHeadV1? retainedAllocatorHead = null; var corruptSnapshotTuple = false;
        var ports = new ScriptedAllocatorPorts
        {
            SnapshotHandler = _ => retainedAllocatorRecord.IsEmpty ? new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(allocatorJournal, null, 0, 0, [])) : new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(allocatorJournal, retainedAllocatorHead, corruptSnapshotTuple ? 2UL : 1UL, (ulong)retainedAllocatorRecord.Length, [retainedAllocatorRecord])),
            ClaimHandler = claim =>
            {
                Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(claim.ExactCanonicalRecordBytes, out var outer));
                Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer!.BodyBytes.ToArray(), out var claimBody));
                var hash = GlobalParticipantAllocatorFactIdsV1.RecordHash(claimBody!.AssignedPosition, null, claim.FactId, outer.SourceSession, outer.SourceExpectedAuthority, outer.BodyBytes);
                retainedAllocatorRecord = claim.ExactCanonicalRecordBytes; retainedAllocatorHead = new(claimBody.AssignedPosition, hash);
                return new GlobalParticipantAllocatorDurableClaimResultV1.Committed(retainedAllocatorHead.Value, 1, claim.ExactCanonicalRecordBytes);
            }
        };
        var coordinator = new GraphParticipantReservationCoordinatorV1(journal, ports, ports, ports, lease);

        var rawResult = await coordinator.ReserveAsync(request, CancellationToken.None);
        if (rawResult is GraphParticipantReservationResultV1.Quarantined quarantined) throw new Xunit.Sdk.XunitException(quarantined.SafeCode.ToString());
        var result = Assert.IsType<GraphParticipantReservationResultV1.Applied>(rawResult);

        Assert.Equal(operation, body.OperationId);
        Assert.Equal(1, result.CommandPosition.Sequence);
        Assert.Equal(2, result.FactPosition.Sequence);
        Assert.Equal(1, ports.ClaimCalls);
        Assert.Equal(1, ports.SnapshotCalls);
        Assert.Equal(2, journal.AppendCalls);
        Assert.Equal(2, journal.ReadCalls);
        journal.FailReadsAfter = 0;
        var committedFacts = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await innerJournal.ReadAsync(new(session, 0, long.MaxValue, 256, 1_048_576)));
        Assert.Equal(2, committedFacts.Facts.Count);
        var canonicalFold = GraphParticipantReservationFoldV1.Create(session); Assert.IsType<GraphParticipantReservationFoldV1.Accepted>(canonicalFold.Apply(committedFacts.Facts[0])); Assert.IsType<GraphParticipantReservationFoldV1.Accepted>(canonicalFold.Apply(committedFacts.Facts[1])); canonicalFold.Complete(); Assert.IsType<GraphParticipantReservationFoldV1.AppliedReservation>(canonicalFold.Query(operation));
        AssertFoldJoinInvalid(session, committedFacts.Facts[0], MutateReservationFact(committedFacts.Facts[1], "predecessor"));
        AssertFoldJoinInvalid(session, committedFacts.Facts[0], MutateReservationFact(committedFacts.Facts[1], "factory"));
        AssertFoldJoinInvalid(session, committedFacts.Facts[0], MutateReservationFact(committedFacts.Facts[1], "node"));
        AssertFoldJoinInvalid(session, committedFacts.Facts[0], MutateReservationFact(committedFacts.Facts[1], "authority"));
        var rejectedFold = GraphParticipantReservationFoldV1.Create(session); Assert.IsType<GraphParticipantReservationFoldV1.Accepted>(rejectedFold.Apply(committedFacts.Facts[0])); Assert.IsType<GraphParticipantReservationFoldV1.Accepted>(rejectedFold.Apply(MutateReservationFact(committedFacts.Facts[1], "rejected"))); rejectedFold.Complete(); var rejectedQuery = Assert.IsType<GraphParticipantReservationFoldV1.RejectedReservation>(rejectedFold.Query(operation)); Assert.Equal("participant-id-collision", rejectedQuery.SafeCode.ToString());
        var restarted = Assert.IsType<GraphParticipantReservationResultV1.Applied>(await coordinator.ReserveAsync(request, CancellationToken.None));
        Assert.Equal(result.AllocatorHead, restarted.AllocatorHead);
        var authenticRecord = retainedAllocatorRecord; var authenticHead = retainedAllocatorHead; var altered = MutateAllocatorExpectedAuthority(authenticRecord); retainedAllocatorRecord = altered.Record; retainedAllocatorHead = altered.Head; var authorityMismatch = Assert.IsType<GraphParticipantReservationResultV1.Quarantined>(await coordinator.ReserveAsync(request, CancellationToken.None)); Assert.Equal("allocator-snapshot-invalid", authorityMismatch.SafeCode.ToString()); retainedAllocatorRecord = authenticRecord; retainedAllocatorHead = authenticHead;
        corruptSnapshotTuple = true; var corruptRestart = Assert.IsType<GraphParticipantReservationResultV1.Quarantined>(await coordinator.ReserveAsync(request, CancellationToken.None)); Assert.Equal("allocator-snapshot-invalid", corruptRestart.SafeCode.ToString());
        Assert.Equal(1, ports.ClaimCalls);
        Assert.Equal(4, ports.SnapshotCalls);
        await lease.DisposeAsync();
    }

    [Fact]
    public void Durable_and_session_result_arm_inventories_are_closed()
    {
        Assert.Equal(10, new[] { typeof(GlobalParticipantAllocatorDurableClaimResultV1.Committed), typeof(GlobalParticipantAllocatorDurableClaimResultV1.AlreadyCommitted), typeof(GlobalParticipantAllocatorDurableClaimResultV1.ContradictoryDuplicate), typeof(GlobalParticipantAllocatorDurableClaimResultV1.HeadConflict), typeof(GlobalParticipantAllocatorDurableClaimResultV1.InvalidRecord), typeof(GlobalParticipantAllocatorDurableClaimResultV1.LifetimeExhausted), typeof(GlobalParticipantAllocatorDurableClaimResultV1.RealmFenced), typeof(GlobalParticipantAllocatorDurableClaimResultV1.StoreUnavailable), typeof(GlobalParticipantAllocatorDurableClaimResultV1.OutcomeUnknown), typeof(GlobalParticipantAllocatorDurableClaimResultV1.Quarantined) }.Distinct().Count());
        Assert.Equal(6, new[] { typeof(GlobalParticipantAllocatorReconcileResultV1.Committed), typeof(GlobalParticipantAllocatorReconcileResultV1.NotFound), typeof(GlobalParticipantAllocatorReconcileResultV1.RealmFenced), typeof(GlobalParticipantAllocatorReconcileResultV1.StoreUnavailable), typeof(GlobalParticipantAllocatorReconcileResultV1.OutcomeUnknown), typeof(GlobalParticipantAllocatorReconcileResultV1.Quarantined) }.Distinct().Count());
        Assert.Equal(4, new[] { typeof(GlobalParticipantAllocatorDurableSnapshotResultV1.Current), typeof(GlobalParticipantAllocatorDurableSnapshotResultV1.RealmFenced), typeof(GlobalParticipantAllocatorDurableSnapshotResultV1.StoreUnavailable), typeof(GlobalParticipantAllocatorDurableSnapshotResultV1.Quarantined) }.Distinct().Count());
        Assert.Equal(7, new[] { typeof(GraphParticipantReservationResultV1.Applied), typeof(GraphParticipantReservationResultV1.Rejected), typeof(GraphParticipantReservationResultV1.RetryRequired), typeof(GraphParticipantReservationResultV1.StoreUnavailable), typeof(GraphParticipantReservationResultV1.OutcomeUnknown), typeof(GraphParticipantReservationResultV1.RealmFenced), typeof(GraphParticipantReservationResultV1.Quarantined) }.Distinct().Count());
    }

    [Fact]
    public void Request_enforces_all_frozen_count_and_byte_bounds()
    {
        var operation = OperationId.FromValue(Id(40)); var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(41)), LiveSessionId.FromValue(Id(42))); var stamp = new MonotonicStampV1(ClockDomainId.FromValue(Id(43)), BootId.FromValue(Id(44)), 1);
        var body = new GraphParticipantReservationCommandBodyV1(operation, null, session.RuntimeGenerationId, Hash256.Compute([1]), Hash256.Compute([2]), Hash256.Compute([3]), new("f"), [new("n")], stamp);
        var bytes = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(session, ExpectedAuthorityVectorV1.Create(session, []), GraphParticipantBindingCodecsV1.Encode(body)));
        var correlation = new CorrelationEnvelopeV1(TenantId.FromValue(Id(45)), operationId: operation);
        Assert.NotNull(new GraphParticipantReservationRequestV1(bytes, correlation, default, 65_536, 536_870_912));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphParticipantReservationRequestV1(bytes, correlation, default, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphParticipantReservationRequestV1(bytes, correlation, default, 65_537, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphParticipantReservationRequestV1(bytes, correlation, default, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphParticipantReservationRequestV1(bytes, correlation, default, 1, 536_870_913));
    }

    [Fact]
    public async Task Snapshot_closed_failure_arms_map_through_full_coordinator()
    {
        var unavailable = Assert.IsType<GraphParticipantReservationResultV1.StoreUnavailable>(await RunSnapshotArm(new GlobalParticipantAllocatorDurableSnapshotResultV1.StoreUnavailable(new("store")), 50));
        Assert.Equal("allocator-store-unavailable", unavailable.SafeCode.ToString());
        var quarantined = Assert.IsType<GraphParticipantReservationResultV1.Quarantined>(await RunSnapshotArm(new GlobalParticipantAllocatorDurableSnapshotResultV1.Quarantined(new("bad")), 60));
        Assert.Equal("allocator-quarantined", quarantined.SafeCode.ToString());
        var fenced = Assert.IsType<GraphParticipantReservationResultV1.RealmFenced>(await RunSnapshotArm(new GlobalParticipantAllocatorDurableSnapshotResultV1.RealmFenced(77), 70));
        Assert.Equal(77UL, fenced.CurrentFenceEpoch);
    }

    private static async Task<GraphParticipantReservationResultV1> RunSnapshotArm(GlobalParticipantAllocatorDurableSnapshotResultV1 arm, byte seed)
    {
        var operation = OperationId.FromValue(Id(seed)); var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id((byte)(seed + 1))), LiveSessionId.FromValue(Id((byte)(seed + 2)))); var stamp = new MonotonicStampV1(ClockDomainId.FromValue(Id((byte)(seed + 3))), BootId.FromValue(Id((byte)(seed + 4))), 1);
        var body = new GraphParticipantReservationCommandBodyV1(operation, null, session.RuntimeGenerationId, Hash256.Compute([1]), Hash256.Compute([2]), Hash256.Compute([3]), new("f"), [new("n")], stamp); var bytes = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(session, ExpectedAuthorityVectorV1.Create(session, []), GraphParticipantBindingCodecsV1.Encode(body))); var correlation = new CorrelationEnvelopeV1(TenantId.FromValue(Id((byte)(seed + 5))), operationId: operation); var request = new GraphParticipantReservationRequestV1(bytes, correlation, default, 32, 1_048_576);
        var journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand, GraphParticipantBindingPayloadRegistrationsV1.ReservationFact]), () => default, new AuthorityJournalCapacityV1(1, 16, 2_000_000));
        var allocatorJournal = GlobalParticipantAllocatorJournalId.FromValue(Id((byte)(seed + 6))); var store = Hash256.Compute([seed]); var created = default(UtcInstant); var manifest = new GlobalParticipantAllocatorRealmManifestV1(allocatorJournal, 1, 1, store, created, GlobalParticipantAllocatorRealmManifestV1.ComputeManifestHash(allocatorJournal, 1, 1, store, created)); var lease = new GlobalParticipantAllocatorRealmLeaseV1(manifest, new TestRealmCustody()); var ports = new ScriptedAllocatorPorts(); ports.Snapshots.Enqueue(arm);
        try { return await new GraphParticipantReservationCoordinatorV1(journal, ports, ports, ports, lease).ReserveAsync(request, CancellationToken.None); } finally { await lease.DisposeAsync(); }
    }

    [Theory]
    [InlineData("invalid", "allocator-record-invalid")]
    [InlineData("lifetime", "allocator-lifetime-exhausted")]
    [InlineData("duplicate", "contradictory-duplicate")]
    [InlineData("quarantine", "allocator-quarantined")]
    [InlineData("store", "allocator-store-unavailable")]
    [InlineData("fenced", "77")]
    [InlineData("conflict", "allocator-head-advanced")]
    [InlineData("cancel-committed", "allocator-outcome-unknown")]
    public async Task Claim_failure_arms_map_through_full_coordinator(string arm, string expected)
    {
        var result = await RunClaimArm(arm, 90);
        var actual = result switch { GraphParticipantReservationResultV1.Quarantined q => q.SafeCode.ToString(), GraphParticipantReservationResultV1.StoreUnavailable s => s.SafeCode.ToString(), GraphParticipantReservationResultV1.RealmFenced f => f.CurrentFenceEpoch.ToString(), GraphParticipantReservationResultV1.RetryRequired r => r.SafeCode.ToString(), GraphParticipantReservationResultV1.OutcomeUnknown u => u.SafeCode.ToString(), _ => result.GetType().Name };
        Assert.Equal(expected, actual);
    }

    private static async Task<GraphParticipantReservationResultV1> RunClaimArm(string arm, byte seed)
    {
        using var cancel = new CancellationTokenSource();
        var operation = OperationId.FromValue(Id(seed)); var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id((byte)(seed + 1))), LiveSessionId.FromValue(Id((byte)(seed + 2)))); var stamp = new MonotonicStampV1(ClockDomainId.FromValue(Id((byte)(seed + 3))), BootId.FromValue(Id((byte)(seed + 4))), 1); var body = new GraphParticipantReservationCommandBodyV1(operation, null, session.RuntimeGenerationId, Hash256.Compute([1]), Hash256.Compute([2]), Hash256.Compute([3]), new("f"), [new("n")], stamp); var bytes = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(session, ExpectedAuthorityVectorV1.Create(session, []), GraphParticipantBindingCodecsV1.Encode(body))); var request = new GraphParticipantReservationRequestV1(bytes, new CorrelationEnvelopeV1(TenantId.FromValue(Id((byte)(seed + 5))), operationId: operation), default, 32, 1_048_576); var journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand, GraphParticipantBindingPayloadRegistrationsV1.ReservationFact]), () => default, new AuthorityJournalCapacityV1(1, 16, 2_000_000)); var allocatorJournal = GlobalParticipantAllocatorJournalId.FromValue(Id((byte)(seed + 6))); var store = Hash256.Compute([seed]); var manifest = new GlobalParticipantAllocatorRealmManifestV1(allocatorJournal, 1, 1, store, default, GlobalParticipantAllocatorRealmManifestV1.ComputeManifestHash(allocatorJournal, 1, 1, store, default)); var lease = new GlobalParticipantAllocatorRealmLeaseV1(manifest, new TestRealmCustody()); var ports = new ScriptedAllocatorPorts
        {
            SnapshotHandler = _ => new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(allocatorJournal, null, 0, 0, [])),
            ClaimHandler = claim => arm switch { "cancel-committed" => CancelCommitted(claim,cancel), "invalid" => new GlobalParticipantAllocatorDurableClaimResultV1.InvalidRecord(new("x")), "lifetime" => new GlobalParticipantAllocatorDurableClaimResultV1.LifetimeExhausted(65_536, 1), "duplicate" => new GlobalParticipantAllocatorDurableClaimResultV1.ContradictoryDuplicate(JournalFactId.FromValue(Id(99)), new("x")), "quarantine" => new GlobalParticipantAllocatorDurableClaimResultV1.Quarantined(new("x")), "store" => new GlobalParticipantAllocatorDurableClaimResultV1.StoreUnavailable(new("x")), "fenced" => new GlobalParticipantAllocatorDurableClaimResultV1.RealmFenced(77), _ => new GlobalParticipantAllocatorDurableClaimResultV1.HeadConflict(null) }
        };
        try { return await new GraphParticipantReservationCoordinatorV1(journal, ports, ports, ports, lease).ReserveAsync(request, cancel.Token); } finally { await lease.DisposeAsync(); }
    }

    private static GlobalParticipantAllocatorDurableClaimResultV1 CancelCommitted(GlobalParticipantAllocatorDurableClaimRequestV1 claim,CancellationTokenSource cancel){var committed=CandidateCommitted(claim);cancel.Cancel();return committed;}

    [Theory]
    [InlineData("notfound", "allocator-outcome-unknown")]
    [InlineData("store", "allocator-store-unavailable")]
    [InlineData("fenced", "88")]
    [InlineData("quarantine", "allocator-quarantined")]
    [InlineData("unknown", "allocator-outcome-unknown")]
    [InlineData("committed", "Applied")]
    [InlineData("corrupt", "returned-candidate-mismatch")]
    [InlineData("corrupt-sequence", "returned-candidate-mismatch")]
    [InlineData("corrupt-head", "returned-candidate-mismatch")]
    public async Task Reconcile_failure_arms_map_after_outcome_unknown(string arm, string expected)
    {
        var result = await RunReconcileArm(arm, 120); var actual = result switch { GraphParticipantReservationResultV1.RetryRequired r => r.SafeCode.ToString(), GraphParticipantReservationResultV1.StoreUnavailable s => s.SafeCode.ToString(), GraphParticipantReservationResultV1.RealmFenced f => f.CurrentFenceEpoch.ToString(), GraphParticipantReservationResultV1.Quarantined q => q.SafeCode.ToString(), GraphParticipantReservationResultV1.OutcomeUnknown u => u.SafeCode.ToString(), _ => result.GetType().Name }; Assert.Equal(expected, actual);
    }

    private static async Task<GraphParticipantReservationResultV1> RunReconcileArm(string arm, byte seed)
    {
        var operation = OperationId.FromValue(Id(seed)); var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id((byte)(seed + 1))), LiveSessionId.FromValue(Id((byte)(seed + 2)))); var stamp = new MonotonicStampV1(ClockDomainId.FromValue(Id((byte)(seed + 3))), BootId.FromValue(Id((byte)(seed + 4))), 1); var body = new GraphParticipantReservationCommandBodyV1(operation, null, session.RuntimeGenerationId, Hash256.Compute([1]), Hash256.Compute([2]), Hash256.Compute([3]), new("f"), [new("n")], stamp); var bytes = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(session, ExpectedAuthorityVectorV1.Create(session, []), GraphParticipantBindingCodecsV1.Encode(body))); var request = new GraphParticipantReservationRequestV1(bytes, new CorrelationEnvelopeV1(TenantId.FromValue(Id((byte)(seed + 5))), operationId: operation), default, 32, 1_048_576); var journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand, GraphParticipantBindingPayloadRegistrationsV1.ReservationFact]), () => default, new AuthorityJournalCapacityV1(1, 16, 2_000_000)); var allocatorJournal = GlobalParticipantAllocatorJournalId.FromValue(Id((byte)(seed + 6))); var store = Hash256.Compute([seed]); var manifest = new GlobalParticipantAllocatorRealmManifestV1(allocatorJournal, 1, 1, store, default, GlobalParticipantAllocatorRealmManifestV1.ComputeManifestHash(allocatorJournal, 1, 1, store, default)); var lease = new GlobalParticipantAllocatorRealmLeaseV1(manifest, new TestRealmCustody()); GlobalParticipantAllocatorDurableClaimRequestV1? captured = null; var ports = new ScriptedAllocatorPorts { SnapshotHandler = _ => new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(allocatorJournal, null, 0, 0, [])), ClaimHandler = x => { captured = x; return new GlobalParticipantAllocatorDurableClaimResultV1.OutcomeUnknown(x.FactId, new("x")); }, ReconcileHandler = _ => arm switch { "notfound" => new GlobalParticipantAllocatorReconcileResultV1.NotFound(), "store" => new GlobalParticipantAllocatorReconcileResultV1.StoreUnavailable(new("x")), "fenced" => new GlobalParticipantAllocatorReconcileResultV1.RealmFenced(88), "quarantine" => new GlobalParticipantAllocatorReconcileResultV1.Quarantined(new("x")), "committed" => CommittedReconcile(captured!, "none"), "corrupt" => CommittedReconcile(captured!, "bytes"), "corrupt-sequence" => CommittedReconcile(captured!, "sequence"), "corrupt-head" => CommittedReconcile(captured!, "head"), _ => new GlobalParticipantAllocatorReconcileResultV1.OutcomeUnknown(captured!.FactId, new("x")) } };
        try { var result = await new GraphParticipantReservationCoordinatorV1(journal, ports, ports, ports, lease).ReserveAsync(request, CancellationToken.None); if (arm == "notfound") { Assert.Equal(1, ports.SnapshotCalls); Assert.Equal(3, ports.ClaimRequests.Count); Assert.All(ports.ClaimRequests, x => { Assert.Same(ports.ClaimRequests[0], x); Assert.Equal(ports.ClaimRequests[0].ExpectedHead, x.ExpectedHead); Assert.Equal(ports.ClaimRequests[0].FactId, x.FactId); Assert.True(ports.ClaimRequests[0].ExactCanonicalRecordBytes.Span.SequenceEqual(x.ExactCanonicalRecordBytes.Span)); }); } return result; } finally { await lease.DisposeAsync(); }
    }
    private static GlobalParticipantAllocatorReconcileResultV1 CommittedReconcile(GlobalParticipantAllocatorDurableClaimRequestV1 candidate, string mutation)
    {
        Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(candidate.ExactCanonicalRecordBytes, out var outer)); Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer!.BodyBytes.ToArray(), out var body)); var hash = GlobalParticipantAllocatorFactIdsV1.RecordHash(body!.AssignedPosition, body.PriorHead, candidate.FactId, outer.SourceSession, outer.SourceExpectedAuthority, outer.BodyBytes); var head = mutation == "head" ? new GlobalParticipantAuthorityHeadV1(body.AssignedPosition, Hash256.Compute([9])) : new GlobalParticipantAuthorityHeadV1(body.AssignedPosition, hash); return new GlobalParticipantAllocatorReconcileResultV1.Committed(head, mutation == "sequence" ? body.AssignedPosition.Sequence + 1 : body.AssignedPosition.Sequence, mutation == "bytes" ? new byte[] { 9 } : candidate.ExactCanonicalRecordBytes);
    }
    [Fact]
    public async Task Cancellation_before_any_journal_or_allocator_invocation_throws()
    {
        var operation=OperationId.FromValue(Id(150));var session=new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(151)),LiveSessionId.FromValue(Id(152)));var body=new GraphParticipantReservationCommandBodyV1(operation,null,session.RuntimeGenerationId,Hash256.Compute([1]),Hash256.Compute([2]),Hash256.Compute([3]),new("f"),[new("n")],new(ClockDomainId.FromValue(Id(153)),BootId.FromValue(Id(154)),1));var bytes=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(session,ExpectedAuthorityVectorV1.Create(session,[]),GraphParticipantBindingCodecsV1.Encode(body)));var request=new GraphParticipantReservationRequestV1(bytes,new CorrelationEnvelopeV1(TenantId.FromValue(Id(155)),operationId:operation),default,2,100_000);var journal=new ScriptedAuthorityJournal();var ports=new ScriptedAllocatorPorts();var allocatorJournal=GlobalParticipantAllocatorJournalId.FromValue(Id(156));var store=Hash256.Compute([1]);var manifest=new GlobalParticipantAllocatorRealmManifestV1(allocatorJournal,1,1,store,default,GlobalParticipantAllocatorRealmManifestV1.ComputeManifestHash(allocatorJournal,1,1,store,default));var lease=new GlobalParticipantAllocatorRealmLeaseV1(manifest,new TestRealmCustody());using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(async()=>await new GraphParticipantReservationCoordinatorV1(journal,ports,ports,ports,lease).ReserveAsync(request,cts.Token));Assert.Equal(0,journal.AppendCalls);Assert.Equal(0,ports.SnapshotCalls);var general=new ScriptedAuthorityJournal{ReadHandler=(_,_)=>throw new InvalidOperationException("port")};var generalResult=Assert.IsType<GraphParticipantReservationResultV1.StoreUnavailable>(await new GraphParticipantReservationCoordinatorV1(general,ports,ports,ports,lease).ReserveAsync(request,CancellationToken.None));Assert.Equal("session-journal-store-unavailable",generalResult.SafeCode.ToString());using var duringRead=new CancellationTokenSource();var canceledRead=new ScriptedAuthorityJournal{ReadHandler=(_,_)=>{duringRead.Cancel();throw new OperationCanceledException(duringRead.Token);}};await Assert.ThrowsAsync<OperationCanceledException>(async()=>await new GraphParticipantReservationCoordinatorV1(canceledRead,ports,ports,ports,lease).ReserveAsync(request,duringRead.Token));await lease.DisposeAsync();
    }
    [Fact]
    public async Task Command_append_lost_ack_rereads_exact_commit_without_duplicate()
    {
        var operation = OperationId.FromValue(Id(170)); var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(171)), LiveSessionId.FromValue(Id(172))); var body = new GraphParticipantReservationCommandBodyV1(operation, null, session.RuntimeGenerationId, Hash256.Compute([1]), Hash256.Compute([2]), Hash256.Compute([3]), new("f"), [new("n")], new(ClockDomainId.FromValue(Id(173)), BootId.FromValue(Id(174)), 1)); var bytes = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(session, ExpectedAuthorityVectorV1.Create(session, []), GraphParticipantBindingCodecsV1.Encode(body))); var request = new GraphParticipantReservationRequestV1(bytes, new CorrelationEnvelopeV1(TenantId.FromValue(Id(175)), operationId: operation), default, 8, 1_000_000); var inner = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand, GraphParticipantBindingPayloadRegistrationsV1.ReservationFact]), () => default, new AuthorityJournalCapacityV1(1, 8, 2_000_000)); var journal = new ScriptedAuthorityJournal { Inner = inner, CommitThenOutcomeUnknown = 1 }; var allocatorJournal = GlobalParticipantAllocatorJournalId.FromValue(Id(176)); var store = Hash256.Compute([7]); var manifest = new GlobalParticipantAllocatorRealmManifestV1(allocatorJournal, 1, 1, store, default, GlobalParticipantAllocatorRealmManifestV1.ComputeManifestHash(allocatorJournal, 1, 1, store, default)); var lease = new GlobalParticipantAllocatorRealmLeaseV1(manifest, new TestRealmCustody()); var ports = new ScriptedAllocatorPorts(); ports.Snapshots.Enqueue(new GlobalParticipantAllocatorDurableSnapshotResultV1.StoreUnavailable(new("x"))); var result = Assert.IsType<GraphParticipantReservationResultV1.StoreUnavailable>(await new GraphParticipantReservationCoordinatorV1(journal, ports, ports, ports, lease).ReserveAsync(request, CancellationToken.None)); Assert.Equal("allocator-store-unavailable", result.SafeCode.ToString()); Assert.Equal(1, journal.AppendCalls); var read = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await inner.ReadAsync(new(session, 0, long.MaxValue, 256, 1_048_576))); Assert.Single(read.Facts); await lease.DisposeAsync();
    }
    [Fact]
    public async Task Cross_instance_singleton_race_leaves_one_allocator_orphan_and_one_S1_applied()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(181)), LiveSessionId.FromValue(Id(182))); var expected = ExpectedAuthorityVectorV1.Create(session, []); var journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand, GraphParticipantBindingPayloadRegistrationsV1.ReservationFact]), () => default, new AuthorityJournalCapacityV1(1, 32, 4_000_000)); var allocatorJournal = GlobalParticipantAllocatorJournalId.FromValue(Id(183)); var store = Hash256.Compute([8]); var manifest = new GlobalParticipantAllocatorRealmManifestV1(allocatorJournal, 1, 1, store, default, GlobalParticipantAllocatorRealmManifestV1.ComputeManifestHash(allocatorJournal, 1, 1, store, default)); var lease = new GlobalParticipantAllocatorRealmLeaseV1(manifest, new TestRealmCustody()); using var barrier = new CountdownEvent(2); var ports = new ScriptedAllocatorPorts { SnapshotHandler = _ => new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(allocatorJournal, null, 0, 0, [])), ClaimHandler = x => { barrier.Signal(); Assert.True(barrier.Wait(TimeSpan.FromSeconds(10))); return CandidateCommitted(x); } };
        GraphParticipantReservationRequestV1 Request(byte seed) { var operation = OperationId.FromValue(Id(seed)); var body = new GraphParticipantReservationCommandBodyV1(operation, null, session.RuntimeGenerationId, Hash256.Compute([seed]), Hash256.Compute([(byte)(seed + 1)]), Hash256.Compute([(byte)(seed + 2)]), new("f"), [new("n")], new(ClockDomainId.FromValue(Id((byte)(seed + 3))), BootId.FromValue(Id((byte)(seed + 4))), 1)); var bytes = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(session, expected, GraphParticipantBindingCodecsV1.Encode(body))); return new(bytes, new CorrelationEnvelopeV1(TenantId.FromValue(Id((byte)(seed + 5))), operationId: operation), default, 16, 2_000_000); }
        var first = new GraphParticipantReservationCoordinatorV1(journal, ports, ports, ports, lease); var second = new GraphParticipantReservationCoordinatorV1(journal, ports, ports, ports, lease); var results = await Task.WhenAll(Task.Run(async () => await first.ReserveAsync(Request(190), CancellationToken.None)), Task.Run(async () => await second.ReserveAsync(Request(200), CancellationToken.None))); Assert.Single(results.OfType<GraphParticipantReservationResultV1.Applied>()); var loser = Assert.Single(results.OfType<GraphParticipantReservationResultV1.Quarantined>()); Assert.Equal("session-singleton-changed", loser.SafeCode.ToString()); Assert.Equal(0, barrier.CurrentCount); var read = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await journal.ReadAsync(new(session, 0, long.MaxValue, 256, 1_048_576))); Assert.Equal(3, read.Facts.Count); await lease.DisposeAsync();
    }
    private static GlobalParticipantAllocatorDurableClaimResultV1 CandidateCommitted(GlobalParticipantAllocatorDurableClaimRequestV1 candidate)
    { Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(candidate.ExactCanonicalRecordBytes, out var outer)); Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer!.BodyBytes.ToArray(), out var body)); var hash = GlobalParticipantAllocatorFactIdsV1.RecordHash(body!.AssignedPosition, body.PriorHead, candidate.FactId, outer.SourceSession, outer.SourceExpectedAuthority, outer.BodyBytes); return new GlobalParticipantAllocatorDurableClaimResultV1.Committed(new(body.AssignedPosition, hash), body.AssignedPosition.Sequence, candidate.ExactCanonicalRecordBytes); }
    private static (ReadOnlyMemory<byte> Record, GlobalParticipantAuthorityHeadV1 Head) MutateAllocatorExpectedAuthority(ReadOnlyMemory<byte> record)
    { Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(record, out var outer)); Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer!.BodyBytes.ToArray(), out var body)); var authority = ExpectedAuthorityVectorV1.Create(outer.SourceSession, [new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(Id(218)))]); var changed = GlobalParticipantAllocatorCodecsV1.Encode(new GlobalParticipantClaimRecordV1(outer.SourceSession, authority, outer.BodyBytes)); var fingerprint = GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(body!.Source); var fact = GlobalParticipantAllocatorFactIdsV1.Fact(outer.SourceSession.LiveSessionId, body.OperationId, fingerprint); var hash = GlobalParticipantAllocatorFactIdsV1.RecordHash(body.AssignedPosition, body.PriorHead, fact, outer.SourceSession, authority, outer.BodyBytes); return (changed, new(body.AssignedPosition, hash)); }
    private static void AssertFoldJoinInvalid(SessionAuthorityStampV1 session, AuthorityFactEnvelopeV1 command, AuthorityFactEnvelopeV1 fact) { var fold = GraphParticipantReservationFoldV1.Create(session); Assert.IsType<GraphParticipantReservationFoldV1.Accepted>(fold.Apply(command)); var invalid = Assert.IsType<GraphParticipantReservationFoldV1.InvalidHistory>(fold.Apply(fact)); Assert.Contains(invalid.SafeCode.ToString(), new[] { "command-fact-join-invalid", "singleton-duplicate" }); }
    private static AuthorityFactEnvelopeV1 MutateReservationFact(AuthorityFactEnvelopeV1 envelope, string mutation)
    { Assert.True(GraphParticipantBindingCodecsV1.TryDecodeReservationFact(envelope.PayloadMemory, out var outer)); Assert.True(GraphParticipantBindingCodecsV1.TryDecodeReservationFactBody(outer!.BodyBytes.ToArray(), out var body)); var reservation = body!.Reservation!; body = mutation switch { "predecessor" => body with { ActualPredecessor = body.CommandPosition }, "factory" => body with { Reservation = new GraphParticipantReservationV1(reservation.ParticipantId, new("changed"), reservation.OrderedTopologyNodeKeys) }, "node" => body with { Reservation = new GraphParticipantReservationV1(reservation.ParticipantId, reservation.ParticipantFactoryKey, [new("changed")]) }, "rejected" => body with { Outcome = 2, Reservation = null, SafeCode = new BoundedAscii("participant-id-collision") }, _ => body }; var authority = mutation == "authority" ? ExpectedAuthorityVectorV1.Create(outer.Session, [new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(Id(217)))]) : outer.ExpectedAuthority; var payload = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationFactV1(outer.Session, authority, GraphParticipantBindingCodecsV1.Encode(body))); var registration = GraphParticipantBindingPayloadRegistrationsV1.ReservationFact; return new AuthorityFactEnvelopeV1(envelope.FactId, envelope.Position, envelope.ThreadScope, envelope.Owner, envelope.PayloadSchema, payload, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload), envelope.Correlation, envelope.ObservedAt, envelope.AdmittedAt, envelope.Integrity); }
    [Theory]
    [InlineData("store", "session-journal-store-unavailable")]
    [InlineData("invalid", "invalid-payload")]
    [InlineData("thread", "unexpected-thread-conflict")]
    [InlineData("duplicate", "contradictory-duplicate")]
    [InlineData("schema", "schema-unavailable")]
    [InlineData("capacity", "journal-capacity-refused")]
    public async Task Command_append_refusal_arms_use_normalized_codes(string arm, string expected)
    {
        var operation = OperationId.FromValue(Id(210)); var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(211)), LiveSessionId.FromValue(Id(212))); var body = new GraphParticipantReservationCommandBodyV1(operation, null, session.RuntimeGenerationId, Hash256.Compute([1]), Hash256.Compute([2]), Hash256.Compute([3]), new("f"), [new("n")], new(ClockDomainId.FromValue(Id(213)), BootId.FromValue(Id(214)), 1)); var bytes = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(session, ExpectedAuthorityVectorV1.Create(session, []), GraphParticipantBindingCodecsV1.Encode(body))); var request = new GraphParticipantReservationRequestV1(bytes, new CorrelationEnvelopeV1(TenantId.FromValue(Id(215)), operationId: operation), default, 8, 1_000_000); var journal = new ScriptedAuthorityJournal { AppendHandler = _ => arm switch { "store" => new AppendAuthorityResultV1.StoreUnavailable(new("secret")), "thread" => new AppendAuthorityResultV1.ThreadConflict(ThreadId.FromValue(Id(1)), 0, 1), "duplicate" => new AppendAuthorityResultV1.ContradictoryDuplicate(JournalFactId.FromValue(Id(2)), Hash256.Compute([1]), Hash256.Compute([2])), "schema" => new AppendAuthorityResultV1.UnknownSchema(GraphParticipantBindingPayloadRegistrationsV1.ReservationFact.Schema), "capacity" => new AppendAuthorityResultV1.CapacityRefused(new CapacityDimensionId(3), 2, 1), _ => new AppendAuthorityResultV1.InvalidPayload(new("secret")) } }; journal.Reads.Enqueue(new ReadAuthorityRangeResultV1.Batch(session, 0, 0, 0, [], false)); var allocatorJournal = GlobalParticipantAllocatorJournalId.FromValue(Id(216)); var store = Hash256.Compute([4]); var manifest = new GlobalParticipantAllocatorRealmManifestV1(allocatorJournal, 1, 1, store, default, GlobalParticipantAllocatorRealmManifestV1.ComputeManifestHash(allocatorJournal, 1, 1, store, default)); var lease = new GlobalParticipantAllocatorRealmLeaseV1(manifest, new TestRealmCustody()); var ports = new ScriptedAllocatorPorts(); var result = await new GraphParticipantReservationCoordinatorV1(journal, ports, ports, ports, lease).ReserveAsync(request, CancellationToken.None); var actual = result switch { GraphParticipantReservationResultV1.StoreUnavailable s => s.SafeCode.ToString(), GraphParticipantReservationResultV1.Quarantined q => q.SafeCode.ToString(), _ => result.GetType().Name }; Assert.Equal(expected, actual); Assert.Equal(1, journal.AppendCalls); await lease.DisposeAsync();
    }
    private static StableId128 Id(byte value) { var bytes = new byte[16]; bytes[^1] = value; return StableId128.FromBytes(bytes); }
    private sealed class ScriptedAuthorityJournal : IAuthorityJournalV1
    {
        internal IAuthorityJournalV1? Inner { get; init; }
        internal Func<ReadAuthorityRangeV1, CancellationToken, ReadAuthorityRangeResultV1>? ReadHandler { get; init; }
        internal Func<AppendAuthorityBatchV1, AppendAuthorityResultV1>? AppendHandler { get; init; }
        internal int CommitThenOutcomeUnknown { get; set; }
        internal int OutcomeUnknownOnAppendCall { get; init; }
        internal Queue<ReadAuthorityRangeResultV1> Reads { get; } = [];
        internal Queue<AppendAuthorityResultV1> Appends { get; } = [];
        internal int AppendCalls { get; private set; }
        internal int ReadCalls { get; private set; }
        internal int FailReadsAfter { get; set; }
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) { ReadCalls++; if (FailReadsAfter > 0 && ReadCalls > FailReadsAfter) throw new IOException("unexpected post-commit read"); return ReadHandler is not null ? ValueTask.FromResult(ReadHandler(request, cancellationToken)) : Inner is null ? ValueTask.FromResult(Reads.Dequeue()) : Inner.ReadAsync(request, cancellationToken); }
        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) { AppendCalls++; if (AppendHandler is not null) return AppendHandler(request); if (Inner is not null) { var committed = await Inner.AppendAsync(request, cancellationToken); if (CommitThenOutcomeUnknown-- > 0 || OutcomeUnknownOnAppendCall == AppendCalls) return new AppendAuthorityResultV1.OutcomeUnknown(request.Facts[0].Correlation.OperationId!.Value); return committed; } return Appends.Dequeue(); }
    }
    private sealed class ScriptedAllocatorPorts : IGlobalParticipantAllocatorDurableClaimPortV1, IGlobalParticipantAllocatorReconciliationPortV1, IGlobalParticipantAllocatorDurableSnapshotPortV1
    {
        internal Queue<GlobalParticipantAllocatorDurableSnapshotResultV1> Snapshots { get; } = [];
        internal Queue<GlobalParticipantAllocatorDurableClaimResultV1> Claims { get; } = [];
        internal List<GlobalParticipantAllocatorDurableClaimRequestV1> ClaimRequests { get; } = [];
        internal Queue<GlobalParticipantAllocatorReconcileResultV1> Reconciliations { get; } = [];
        internal Func<GlobalParticipantAllocatorDurableSnapshotRequestV1, GlobalParticipantAllocatorDurableSnapshotResultV1>? SnapshotHandler { get; init; }
        internal Func<GlobalParticipantAllocatorDurableClaimRequestV1, GlobalParticipantAllocatorDurableClaimResultV1>? ClaimHandler { get; init; }
        internal Func<GlobalParticipantAllocatorReconcileRequestV1, GlobalParticipantAllocatorReconcileResultV1>? ReconcileHandler { get; init; }
        internal int SnapshotCalls { get; private set; }
        internal int ClaimCalls { get; private set; }
        public ValueTask<GlobalParticipantAllocatorDurableSnapshotResultV1> ReadAsync(GlobalParticipantAllocatorDurableSnapshotRequestV1 request, CancellationToken cancellationToken) { SnapshotCalls++; return ValueTask.FromResult(SnapshotHandler is null ? Snapshots.Dequeue() : SnapshotHandler(request)); }
        public ValueTask<GlobalParticipantAllocatorDurableClaimResultV1> ClaimAsync(GlobalParticipantAllocatorDurableClaimRequestV1 request, CancellationToken cancellationToken) { ClaimCalls++; ClaimRequests.Add(request); return ValueTask.FromResult(ClaimHandler is null ? Claims.Dequeue() : ClaimHandler(request)); }
        public ValueTask<GlobalParticipantAllocatorReconcileResultV1> ReconcileAsync(GlobalParticipantAllocatorReconcileRequestV1 request, CancellationToken cancellationToken) => ValueTask.FromResult(ReconcileHandler is null ? Reconciliations.Dequeue() : ReconcileHandler(request));
    }
    private sealed class TestRealmCustody : IGlobalParticipantAllocatorDurableCustodyV1
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
