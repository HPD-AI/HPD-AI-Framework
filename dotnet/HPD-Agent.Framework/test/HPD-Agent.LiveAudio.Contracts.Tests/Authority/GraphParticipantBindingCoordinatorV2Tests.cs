using System.Formats.Cbor;
using System.Security.Cryptography;
using HPD.Agent.Audio;
using HPD.Agent.Authority;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.Graph.Runtime;

namespace HPD.Agent.LiveAudio.Contracts.Tests.Authority;

public sealed class GraphParticipantBindingCoordinatorV2Tests
{
    [Fact]
    public async Task Three_histories_bind_and_restart_converges()
    {
        var fixture = CreateAuthenticatedThreeHistoryBindingFixture();
        var raw = await fixture.Coordinator.BindAsync(fixture.Request, CancellationToken.None);
        if (raw is GraphParticipantBindingResultV2.Quarantined quarantine) throw new InvalidOperationException(quarantine.SafeCode.ToString());
        var bound = Assert.IsType<GraphParticipantBindingResultV2.Bound>(raw);
        Assert.Equal(fixture.Request.AppliedReservation.ParticipantId, bound.Binding.ParticipantId);
        Assert.Equal(2, fixture.S1.AppendCalls);
        Assert.Equal(1, fixture.S2.ReadCalls);
        Assert.Equal(1, fixture.Allocator.ReadCalls);
        var restarted = new GraphParticipantBindingCoordinatorV2(fixture.S1, fixture.S2, fixture.Allocator, fixture.Allocator.Lease);
        var existing = Assert.IsType<GraphParticipantBindingResultV2.AlreadyBound>(await restarted.BindAsync(fixture.Request, CancellationToken.None));
        Assert.Equal(bound.FactPosition, existing.FactPosition);
        Assert.Equal(bound.ExactCanonicalFactBytes.ToArray(), existing.ExactCanonicalFactBytes.ToArray());
    }

    [Fact]
    public async Task Each_authority_mutation_has_closed_disposition()
    {
        await AssertCoordinatorMutationAsync("outcome-unknown", "StoreUnavailable", "authority-store-unavailable", 1, 0, 1, 0, 0, "none");
        await AssertCoordinatorMutationAsync("grant-id", "Quarantined", "capacity-history-invalid", 1, 0, 1, 0, 0, "none");
        await AssertCoordinatorMutationAsync("granted-at", "Quarantined", "capacity-history-invalid", 1, 0, 1, 0, 0, "none");
        await AssertCoordinatorMutationAsync("current-fact", "StoreUnavailable", "authority-store-unavailable", 1, 0, 1, 0, 0, "none");
        await AssertCoordinatorMutationAsync("expiry-before-binding-observed", "StoreUnavailable", "authority-store-unavailable", 1, 0, 1, 0, 0, "none");
        await AssertCoordinatorMutationAsync("expiry-incomparable", "StoreUnavailable", "authority-store-unavailable", 1, 0, 1, 0, 0, "none");
        await AssertCoordinatorMutationAsync("charge-count", "Quarantined", "capacity-history-invalid", 1, 0, 1, 0, 0, "none");
        await AssertCoordinatorMutationAsync("request", "StoreUnavailable", "authority-store-unavailable", 1, 0, 1, 0, 0, "none");
        await AssertCoordinatorMutationAsync("balance", "Quarantined", "capacity-history-invalid", 1, 0, 1, 0, 0, "none");
        await AssertCoordinatorMutationAsync("coverage-bytes", "Quarantined", "capacity-history-invalid", 1, 0, 1, 0, 0, "none");
        await AssertCoordinatorMutationAsync("coverage-hash", "Quarantined", "capacity-history-invalid", 1, 0, 1, 0, 0, "none");
        await AssertCoordinatorMutationAsync("second-pinned-read", "Bound", null, 4, 2, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("realm-fence", "RealmFenced", null, 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("journal-id", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("fold-invalid", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("complete-count", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("complete-bytes", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("owner-missing", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("participant", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("session", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("operation", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("source-command-position", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("source-outer-payload-hash", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("source-body-fingerprint", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("source-fingerprint", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("claim-position", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("claim-hash", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("integrity", "Quarantined", "allocator-snapshot-invalid", 1, 0, 1, 1, 0, "none");
    }

    [Fact]
    public async Task Cas_lost_ack_cancellation_and_call_bounds_are_closed()
    {
        await AssertCoordinatorMutationAsync("pre-cancel", "OperationCanceledException", null, 0, 0, 0, 0, 0, "propagated");
        await AssertCoordinatorMutationAsync("command-cas", "Bound", null, 4, 2, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("command-lost-ack", "Bound", null, 5, 2, 1, 1, 1, "none");
        await AssertCoordinatorMutationAsync("fact-cas", "Bound", null, 4, 2, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("fact-lost-ack", "Bound", null, 4, 2, 1, 1, 1, "none");
        await AssertCoordinatorMutationAsync("post-invocation-cancel", "Bound", null, 4, 2, 1, 1, 1, "reconciled");
        await AssertCoordinatorMutationAsync("restart", "AlreadyBound", null, 1, 0, 0, 0, 0, "none");
        await AssertCoordinatorMutationAsync("duplicate", "AlreadyBound", null, 1, 0, 0, 0, 0, "none");
        await AssertCoordinatorMutationAsync("contradiction", "Quarantined", "binding-contradiction", 2, 1, 1, 1, 0, "none");
        await AssertCoordinatorMutationAsync("durable-rejection-precedence", "Rejected", "participant-binding-already-applied", 1, 0, 0, 0, 0, "none");
        await AssertCoordinatorMutationAsync("command-attempt-bound", "RetryRequired", "binding-predecessor-conflict", 5, 3, 1, 1, 3, "none");
        await AssertCoordinatorMutationAsync("fact-attempt-bound", "OutcomeUnknown", "binding-outcome-unknown", 6, 4, 1, 1, 3, "none");
        await AssertCoordinatorMutationAsync("s1-read-bound", "StoreUnavailable", "authority-store-unavailable", 10, 0, 0, 0, 0, "none");
        await AssertCoordinatorMutationAsync("s1-append-bound", "OutcomeUnknown", "binding-outcome-unknown", 9, 6, 1, 1, 6, "none");
    }

    [Fact]
    public void Exact_internal_coordinator_inventory_is_closed()
    {
        Assert.Equal(8, typeof(GraphParticipantBindingResultV2).GetNestedTypes(System.Reflection.BindingFlags.NonPublic).Length);
        Assert.Equal(8, typeof(GraphParticipantBindingRequestV2).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly).Count(property => property.Name != "EqualityContract"));
        var input = new byte[] { 1, 2, 3 };
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var binding = new GraphParticipantBindingV1(ParticipantId.Create(), new("factory"), [new("node")]);
        var proof = new CapacityGrantBindingProofV1(CapacityGrantId.Create(), new(session, 1), new(session, 2), 1, Hash256.Compute([1]));
        var result = new GraphParticipantBindingResultV2.Bound(new(session, 3), new(session, 4), input, binding, proof);
        input[0] = 9;
        var first = result.ExactCanonicalFactBytes.ToArray();
        first[0] = 8;
        Assert.Equal((byte)1, result.ExactCanonicalFactBytes.Span[0]);
        Assert.Throws<ArgumentNullException>(() => new GraphParticipantBindingRequestV2(null!, null!, null!, default, default, default, 0, 0));
    }

    private static async Task AssertCoordinatorMutationAsync(string label, string expectedArm, string? expectedCode, int expectedS1Reads, int expectedS1Appends, int expectedS2Reads, int expectedAllocatorReads, int expectedReconciles, string expectedCancellation)
    {
        var baseFixture = CreateAuthenticatedThreeHistoryBindingFixture();
        var mutatedFixture = label switch
        {
            "outcome-unknown" => MutateOutcomeUnknown(baseFixture), "grant-id" => MutateGrantId(baseFixture), "granted-at" => MutateGrantedAt(baseFixture), "current-fact" => MutateCurrentFact(baseFixture), "expiry-before-binding-observed" => MutateExpiryBeforeBindingObserved(baseFixture), "expiry-incomparable" => MutateExpiryIncomparable(baseFixture), "charge-count" => MutateChargeCount(baseFixture), "request" => MutateRequest(baseFixture), "balance" => MutateBalance(baseFixture), "coverage-bytes" => MutateCoverageBytes(baseFixture), "coverage-hash" => MutateCoverageHash(baseFixture), "second-pinned-read" => MutateSecondPinnedRead(baseFixture),
            "realm-fence" => MutateRealmFence(baseFixture), "journal-id" => MutateJournalId(baseFixture), "fold-invalid" => MutateFoldInvalid(baseFixture), "complete-count" => MutateCompleteCount(baseFixture), "complete-bytes" => MutateCompleteBytes(baseFixture), "owner-missing" => MutateOwnerMissing(baseFixture), "participant" => MutateParticipant(baseFixture), "session" => MutateSession(baseFixture), "operation" => MutateOperation(baseFixture), "source-command-position" => MutateSourceCommandPosition(baseFixture), "source-outer-payload-hash" => MutateSourceOuterPayloadHash(baseFixture), "source-body-fingerprint" => MutateSourceBodyFingerprint(baseFixture), "source-fingerprint" => MutateSourceFingerprint(baseFixture), "claim-position" => MutateClaimPosition(baseFixture), "claim-hash" => MutateClaimHash(baseFixture), "integrity" => MutateIntegrity(baseFixture),
            "pre-cancel" => MutatePreCancel(baseFixture), "command-cas" => MutateCommandCas(baseFixture), "command-lost-ack" => MutateCommandLostAck(baseFixture), "fact-cas" => MutateFactCas(baseFixture), "fact-lost-ack" => MutateFactLostAck(baseFixture), "post-invocation-cancel" => MutatePostInvocationCancel(baseFixture), "restart" => MutateRestart(baseFixture), "duplicate" => MutateDuplicate(baseFixture), "contradiction" => MutateContradiction(baseFixture), "durable-rejection-precedence" => MutateDurableRejectionPrecedence(baseFixture), "command-attempt-bound" => MutateCommandAttemptBound(baseFixture), "fact-attempt-bound" => MutateFactAttemptBound(baseFixture), "s1-read-bound" => MutateS1ReadBound(baseFixture), "s1-append-bound" => MutateS1AppendBound(baseFixture),
            _ => throw new InvalidOperationException(label)
        };
        GraphParticipantBindingResultV2? result = null;
        var propagated = false;
        try { result = await mutatedFixture.Coordinator.BindAsync(mutatedFixture.Request, mutatedFixture.S1.CancellationToken); }
        catch (OperationCanceledException) { propagated = true; }
        if (expectedArm == "OperationCanceledException") Assert.True(propagated); else Assert.False(propagated);
        if (!propagated)
        {
            GraphParticipantBindingResultV2 typed = expectedArm switch
            {
                "Bound" => Assert.IsType<GraphParticipantBindingResultV2.Bound>(result), "AlreadyBound" => Assert.IsType<GraphParticipantBindingResultV2.AlreadyBound>(result), "Rejected" => Assert.IsType<GraphParticipantBindingResultV2.Rejected>(result), "RetryRequired" => Assert.IsType<GraphParticipantBindingResultV2.RetryRequired>(result), "StoreUnavailable" => Assert.IsType<GraphParticipantBindingResultV2.StoreUnavailable>(result), "OutcomeUnknown" => Assert.IsType<GraphParticipantBindingResultV2.OutcomeUnknown>(result), "RealmFenced" => Assert.IsType<GraphParticipantBindingResultV2.RealmFenced>(result), "Quarantined" => Assert.IsType<GraphParticipantBindingResultV2.Quarantined>(result), _ => throw new InvalidOperationException(expectedArm)
            };
            Assert.NotNull(typed);
            if (expectedCode is not null)
            {
                BoundedAscii codedSafeCode = expectedArm switch
                {
                    "Rejected" => Assert.IsType<GraphParticipantBindingResultV2.Rejected>(result).SafeCode, "RetryRequired" => Assert.IsType<GraphParticipantBindingResultV2.RetryRequired>(result).SafeCode, "StoreUnavailable" => Assert.IsType<GraphParticipantBindingResultV2.StoreUnavailable>(result).SafeCode, "OutcomeUnknown" => Assert.IsType<GraphParticipantBindingResultV2.OutcomeUnknown>(result).SafeCode, "Quarantined" => Assert.IsType<GraphParticipantBindingResultV2.Quarantined>(result).SafeCode, _ => throw new InvalidOperationException(expectedArm)
                };
                Assert.Equal(expectedCode, codedSafeCode.ToString());
            }
        }
        var observedCancellation = propagated ? "propagated" : mutatedFixture.S1.CancelAfterAppend && result is not null ? "reconciled" : "none";
        Assert.Equal(expectedCancellation, observedCancellation);
        Assert.Equal(expectedS1Reads, mutatedFixture.S1.ReadCalls);
        Assert.Equal(expectedS1Appends, mutatedFixture.S1.AppendCalls);
        Assert.Equal(expectedS2Reads, mutatedFixture.S2.ReadCalls);
        Assert.Equal(expectedAllocatorReads, mutatedFixture.Allocator.ReadCalls);
        Assert.Equal(expectedReconciles, mutatedFixture.S1.ReconcileCalls);
    }

    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) CreateAuthenticatedThreeHistoryBindingFixture()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)1, 16).ToArray())), LiveSessionId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)2, 16).ToArray())));
        var operation = OperationId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)3, 16).ToArray()));
        var graph = GraphGenerationId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)4, 16).ToArray()));
        var planFingerprint = Hash256.Compute([5]);
        var descriptor = new LiveAudioParticipantDescriptorV1(new("factory"), OwnerSliceId.S2, AuthorityAxisId.Graph, [], [new CapacityDimensionId(1)], new DurationNs(1), new DurationNs(1), new DurationNs(1));
        var carrier = LiveAudioParticipantCatalogManifestV1.EncodeGraphParticipantAllocationDeclaration("factory", ["b", "a"], [1], ["00000000000000000000000000000001"], [1], [1]);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hasher.AppendData("hpd-graph-participant-allocation-declaration-v1\0"u8); hasher.AppendData(carrier); var carrierFingerprint = Hash256.FromBytes(hasher.GetHashAndReset());
        var registration = new LiveAudioParticipantFactoryRegistrationV1(typeof(GraphParticipantBindingCoordinatorV2Tests), "tests:factory", descriptor, carrier, carrierFingerprint);
        var catalog = LiveAudioParticipantCatalogManifestV1.Create([registration]);
        var plan = new LiveAudioParticipantPlanV1(LiveAudioPlanId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)6, 16).ToArray())), [descriptor], [], planFingerprint);
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(graph)]);
        var clock = ClockDomainId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)7, 16).ToArray())); var boot = BootId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)8, 16).ToArray()));
        var stamp = new MonotonicStampV1(clock, boot, 10);
        var correlation = new CorrelationEnvelopeV1(TenantId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)9, 16).ToArray())), sessionId: SessionId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)10, 16).ToArray())), operationId: operation);
        var commandBody = new GraphParticipantReservationCommandBodyV2(operation, null, session.RuntimeGenerationId, graph, planFingerprint, carrierFingerprint, new("factory"), [new("a"), new("b")], stamp);
        var commandPayload = GraphParticipantReservationCodecsV2.Encode(new GraphParticipantReservationCommandV2(session, authority, GraphParticipantReservationCodecsV2.Encode(commandBody)));
        var commandRegistration = GraphParticipantReservationPayloadRegistrationsV2.ReservationCommand;
        var command = new AuthorityFactEnvelopeV1(GraphParticipantReservationFactIdsV2.ReservationCommand(session, operation), new(session, 1), null, OwnerSliceId.S1, commandRegistration.Schema, commandPayload, AuthorityPayloadHashV1.Compute(commandRegistration.SchemaToken, commandRegistration.Schema, commandPayload), correlation, new UtcInstant(10), new UtcInstant(10), new IntegrityEnvelopeV1(1, 1, Hash256.Compute([11]), []));
        var source = new GlobalParticipantAllocationSourceV1(session.LiveSessionId, command.Position, command.PayloadHash, Hash256.Compute(GraphParticipantReservationCodecsV2.Encode(commandBody)));
        var sourceFingerprint = GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(source);
        var participant = GlobalParticipantAllocatorFactIdsV1.Participant(session.LiveSessionId, operation, sourceFingerprint);
        var reservation = new GraphParticipantReservationV1(participant, new("factory"), [new("a"), new("b")]);
        var factBody = new GraphParticipantReservationFactBodyV2(operation, command.Position, null, 1, session.RuntimeGenerationId, graph, planFingerprint, carrierFingerprint, reservation, null, stamp);
        var factPayload = GraphParticipantReservationCodecsV2.Encode(new GraphParticipantReservationFactV2(session, authority, GraphParticipantReservationCodecsV2.Encode(factBody)));
        var factRegistration = GraphParticipantReservationPayloadRegistrationsV2.ReservationFact;
        var fact = new AuthorityFactEnvelopeV1(GraphParticipantReservationFactIdsV2.ReservationFact(command.Position), new(session, 2), null, OwnerSliceId.S1, factRegistration.Schema, factPayload, AuthorityPayloadHashV1.Compute(factRegistration.SchemaToken, factRegistration.Schema, factPayload), correlation, new UtcInstant(10), new UtcInstant(10), new IntegrityEnvelopeV1(1, 1, Hash256.Compute([12]), []));
        var authenticated = new GraphParticipantReservationFoldV2.AppliedReservation(command, fact, reservation);
        var allocatorJournal = GlobalParticipantAllocatorJournalId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)13, 16).ToArray()));
        var claimPosition = new GlobalParticipantAuthorityPositionV1(allocatorJournal, 1);
        var ownerProof = new ParticipantIdOwnerProofV1(participant, null, 1, null, GlobalParticipantAllocatorCodecsV1.EmptyIndexRoot(), GlobalParticipantAllocatorCodecsV1.CreateEmptyProofPath(), 0);
        var claimBody = new GlobalParticipantClaimRecordBodyV1(operation, source, participant, null, ownerProof, new ParticipantIdClaimOutcomeV1(1, null, null), claimPosition, new MonotonicStampV1(clock, boot, 11));
        var claimBodyBytes = GlobalParticipantAllocatorCodecsV1.Encode(claimBody);
        var claimRecord = GlobalParticipantAllocatorCodecsV1.Encode(new GlobalParticipantClaimRecordV1(session, authority, claimBodyBytes));
        var allocatorFold = new GlobalParticipantAllocatorFoldAccumulatorV1(allocatorJournal); Assert.IsType<GlobalParticipantAllocatorFoldApplyResultV1.Accepted>(allocatorFold.Apply(claimRecord)); var allocatorCurrent = Assert.IsType<GlobalParticipantAllocatorFoldResultV1.Current>(allocatorFold.Complete()).Snapshot;
        var applied = new GraphParticipantReservationResultV2.Applied(command.Position, fact.Position, participant, allocatorCurrent.Head!.Value, fact.PayloadMemory);
        var build = Assert.IsType<GraphParticipantCapacityPlanBuildResultV2.Found>(GraphParticipantCapacityPlanCompilerV2.BuildCapacityRequest(plan, catalog, registration, applied, authenticated, new MonotonicStampV1(clock, boot, 20), CapacityPriorityV1.Normal));
        var initialization = new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph); var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartMap(3); writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, session); writer.WriteUInt64(2); Span<byte> graphBytes = stackalloc byte[16]; graph.TryWriteBytes(graphBytes); writer.WriteByteString(graphBytes); writer.WriteUInt64(3); writer.WriteUInt64((ushort)OwnerSliceId.S2); writer.WriteEndMap();
        var s2Inner = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([initialization, new CapacityReservationPayloadRegistrationV1(), new CapacitySettlementPayloadRegistrationV1()]), () => new UtcInstant(20), new AuthorityJournalCapacityV1(1, 128, 8_000_000)); var initBytes = writer.Encode(); Assert.IsType<AppendAuthorityResultV1.Committed>(s2Inner.AppendAsync(new(session, 0, [], [new(JournalFactId.Create(), null, OwnerSliceId.S2, initialization.Schema, initBytes, AuthorityPayloadHashV1.Compute(initialization.SchemaToken, initialization.Schema, initBytes), correlation, new UtcInstant(20))], 1_000_000)).AsTask().GetAwaiter().GetResult());
        var granted = Assert.IsType<CapacityAdmissionResultV1.Granted>(CapacityAdmissionCoordinatorV1.ReserveAsync(s2Inner, build.Plan.Request, new CapacityGrantExpiryV1.At(new MonotonicStampV1(clock, boot, 40)), correlation, new MonotonicStampV1(clock, boot, 15), new UtcInstant(21)).AsTask().GetAwaiter().GetResult());
        var executableCatalog = Assert.IsType<GraphRuntimeExecutableCatalogResultV1.Created>(GraphRuntimeExecutableFactoryCatalogV1.FromGeneratedApplicationManifest([new GraphRuntimeExecutableFactoryDeclarationV1(new("a"), "tests:a@1", 1), new GraphRuntimeExecutableFactoryDeclarationV1(new("b"), "tests:b@1", 1)]));
        var topology = new GraphTopologyPlanV1(session, graph, granted.Grant.GrantId, [new(new("a")), new(new("b"))], [], [new(1)]);
        var attached = Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Attached>(new GraphParticipantCapacityPlanEvidenceProviderV2(s2Inner, session).AttachAsync(build.Plan, granted.Grant.GrantId, granted.Grant.CurrentFact, topology, executableCatalog).AsTask().GetAwaiter().GetResult());
        var request = new GraphParticipantBindingRequestV2(applied, authenticated, attached.Evidence, correlation, new MonotonicStampV1(clock, boot, 25), new UtcInstant(25), 64, 4_000_000);
        var s1Inner = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([commandRegistration, factRegistration, GraphParticipantBindingPayloadRegistrationsV1.BindingCommand, GraphParticipantBindingPayloadRegistrationsV1.BindingFact]), () => new UtcInstant(30), new AuthorityJournalCapacityV1(1, 128, 8_000_000)); Assert.IsType<AppendAuthorityResultV1.Committed>(s1Inner.AppendAsync(new(session, 0, [], [new(command.FactId, null, command.Owner, command.PayloadSchema, command.PayloadMemory.Span, command.PayloadHash, command.Correlation, command.ObservedAt), new(fact.FactId, null, fact.Owner, fact.PayloadSchema, fact.PayloadMemory.Span, fact.PayloadHash, fact.Correlation, fact.ObservedAt)], 1_000_000)).AsTask().GetAwaiter().GetResult());
        var s1 = new ScriptedAuthorityJournal(s1Inner); var s2 = new ScriptedAuthorityJournal(s2Inner); var allocator = new ScriptedAllocatorSnapshotPort(allocatorCurrent, claimRecord); var coordinator = new GraphParticipantBindingCoordinatorV2(s1, s2, allocator, allocator.Lease); return (request, s1, s2, allocator, coordinator);
    }

    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateOutcomeUnknown((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S2.ReadResult = new ReadAuthorityRangeResultV1.StoreUnavailable(new("OutcomeUnknown")); return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateGrantId((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { var e=fixture.Request.BindingEvidence;var changed=new GraphParticipantBindingPlanEvidenceV2(e.PreGrantPlan,CapacityGrantId.Create(),e.GrantedAt,e.CurrentFact,e.ExpiresAt,e.CanonicalProjection,e.CoverageHashV2,e.Topology,e.ExecutablePlan,e.TopologyFingerprint,e.ExecutableFingerprint);var request=new GraphParticipantBindingRequestV2(fixture.Request.AppliedReservation,fixture.Request.AuthenticatedReservation,changed,fixture.Request.Correlation,fixture.Request.BindingObservedAt,fixture.Request.ObservedAt,fixture.Request.MaximumSessionRecords,fixture.Request.MaximumSessionCanonicalBytes);return(request,fixture.S1,fixture.S2,fixture.Allocator,new(fixture.S1,fixture.S2,fixture.Allocator,fixture.Allocator.Lease)); }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateGrantedAt((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { var e=fixture.Request.BindingEvidence;var changed=new GraphParticipantBindingPlanEvidenceV2(e.PreGrantPlan,e.GrantId,new(e.GrantedAt.Session,e.GrantedAt.Sequence+1),e.CurrentFact,e.ExpiresAt,e.CanonicalProjection,e.CoverageHashV2,e.Topology,e.ExecutablePlan,e.TopologyFingerprint,e.ExecutableFingerprint);var request=new GraphParticipantBindingRequestV2(fixture.Request.AppliedReservation,fixture.Request.AuthenticatedReservation,changed,fixture.Request.Correlation,fixture.Request.BindingObservedAt,fixture.Request.ObservedAt,fixture.Request.MaximumSessionRecords,fixture.Request.MaximumSessionCanonicalBytes);return(request,fixture.S1,fixture.S2,fixture.Allocator,new(fixture.S1,fixture.S2,fixture.Allocator,fixture.Allocator.Lease)); }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateCurrentFact((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { var e=fixture.Request.BindingEvidence;var changed=new GraphParticipantBindingPlanEvidenceV2(e.PreGrantPlan,e.GrantId,e.GrantedAt,new(e.CurrentFact.Session,e.CurrentFact.Sequence+1),e.ExpiresAt,e.CanonicalProjection,e.CoverageHashV2,e.Topology,e.ExecutablePlan,e.TopologyFingerprint,e.ExecutableFingerprint);var request=new GraphParticipantBindingRequestV2(fixture.Request.AppliedReservation,fixture.Request.AuthenticatedReservation,changed,fixture.Request.Correlation,fixture.Request.BindingObservedAt,fixture.Request.ObservedAt,fixture.Request.MaximumSessionRecords,fixture.Request.MaximumSessionCanonicalBytes);return(request,fixture.S1,fixture.S2,fixture.Allocator,new(fixture.S1,fixture.S2,fixture.Allocator,fixture.Allocator.Lease)); }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateExpiryBeforeBindingObserved((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S2.MutateExpiryBefore = true; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateExpiryIncomparable((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S2.MutateExpiryIncomparable = true; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateChargeCount((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S2.MutateChargeCount = true; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateRequest((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S2.MutateRequest = true; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateBalance((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S2.MutateBalance = true; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateCoverageBytes((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { var e=fixture.Request.BindingEvidence;var bytes=e.CanonicalProjection;bytes[0]^=1;var changed=new GraphParticipantBindingPlanEvidenceV2(e.PreGrantPlan,e.GrantId,e.GrantedAt,e.CurrentFact,e.ExpiresAt,bytes,e.CoverageHashV2,e.Topology,e.ExecutablePlan,e.TopologyFingerprint,e.ExecutableFingerprint);var request=new GraphParticipantBindingRequestV2(fixture.Request.AppliedReservation,fixture.Request.AuthenticatedReservation,changed,fixture.Request.Correlation,fixture.Request.BindingObservedAt,fixture.Request.ObservedAt,fixture.Request.MaximumSessionRecords,fixture.Request.MaximumSessionCanonicalBytes);return(request,fixture.S1,fixture.S2,fixture.Allocator,new(fixture.S1,fixture.S2,fixture.Allocator,fixture.Allocator.Lease)); }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateCoverageHash((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { var e=fixture.Request.BindingEvidence;var changed=new GraphParticipantBindingPlanEvidenceV2(e.PreGrantPlan,e.GrantId,e.GrantedAt,e.CurrentFact,e.ExpiresAt,e.CanonicalProjection,Hash256.Compute([99]),e.Topology,e.ExecutablePlan,e.TopologyFingerprint,e.ExecutableFingerprint);var request=new GraphParticipantBindingRequestV2(fixture.Request.AppliedReservation,fixture.Request.AuthenticatedReservation,changed,fixture.Request.Correlation,fixture.Request.BindingObservedAt,fixture.Request.ObservedAt,fixture.Request.MaximumSessionRecords,fixture.Request.MaximumSessionCanonicalBytes);return(request,fixture.S1,fixture.S2,fixture.Allocator,new(fixture.S1,fixture.S2,fixture.Allocator,fixture.Allocator.Lease)); }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateSecondPinnedRead((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S2.FailOnSecondRead = true; return fixture; }

    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateRealmFence((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Result = new GlobalParticipantAllocatorDurableSnapshotResultV1.RealmFenced(2); return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateJournalId((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "journal-id"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateFoldInvalid((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "fold-invalid"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateCompleteCount((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "complete-count"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateCompleteBytes((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "complete-bytes"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateOwnerMissing((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "owner-missing"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateParticipant((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "participant"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateSession((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "session"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateOperation((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "operation"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateSourceCommandPosition((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "source-command-position"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateSourceOuterPayloadHash((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "source-outer-payload-hash"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateSourceBodyFingerprint((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "source-body-fingerprint"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateSourceFingerprint((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "source-fingerprint"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateClaimPosition((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "claim-position"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateClaimHash((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "claim-hash"; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateIntegrity((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.Allocator.Mutation = "integrity"; return fixture; }

    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutatePreCancel((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S1.CancelBeforeEntry = true; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateCommandCas((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S1.CommandCommitted = true; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateCommandLostAck((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S1.LostAckOnAppend = 1; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateFactCas((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S1.FactCommitted = true; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateFactLostAck((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S1.LostAckOnAppend = 2; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutatePostInvocationCancel((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S1.LostAckOnAppend = 2; fixture.S1.CancelAfterAppend = true; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateRestart((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { _ = fixture.Coordinator.BindAsync(fixture.Request, CancellationToken.None).AsTask().GetAwaiter().GetResult(); fixture.S1.ResetCounters(); fixture.S2.ResetCounters(); fixture.Allocator.ReadCalls = 0; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateDuplicate((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { _ = fixture.Coordinator.BindAsync(fixture.Request, CancellationToken.None).AsTask().GetAwaiter().GetResult(); fixture.S1.ResetCounters(); fixture.S2.ResetCounters(); fixture.Allocator.ReadCalls = 0; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateContradiction((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S1.Contradiction = true; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateDurableRejectionPrecedence((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture)
    {
        var request = fixture.Request;
        Assert.True(GraphParticipantReservationCodecsV2.TryDecodeReservationCommand(request.AuthenticatedReservation.Command.PayloadMemory, out var reservationOuter));
        Assert.NotNull(reservationOuter);
        var evidence = request.BindingEvidence;
        var proof = new CapacityGrantBindingProofV1(evidence.GrantId, evidence.GrantedAt, evidence.CurrentFact, checked((ushort)evidence.PreGrantPlan.Request.Charges.Count), evidence.CoverageHashV2);
        var commandBody = new GraphParticipantBindingCommandBodyV1(evidence.PreGrantPlan.OperationId, request.AuthenticatedReservation.Fact.Position, null, evidence.PreGrantPlan.GraphGeneration, request.AuthenticatedReservation.Command.Position.Session.RuntimeGenerationId, evidence.PreGrantPlan.ParticipantPlanFingerprint, evidence.TopologyFingerprint, evidence.ExecutableFingerprint, proof, request.BindingObservedAt);
        var commandBodyBytes = GraphParticipantBindingCodecsV1.Encode(commandBody);
        var commandPayload = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(request.AuthenticatedReservation.Command.Position.Session, reservationOuter.ExpectedAuthority, commandBodyBytes));
        var commandRegistration = GraphParticipantBindingPayloadRegistrationsV1.BindingCommand;
        var commandProposal = new ProposedAuthorityFactV1(GraphParticipantBindingFactIdsV1.BindingCommand(request.AuthenticatedReservation.Command.Position.Session, commandBody.OperationId), null, OwnerSliceId.S1, commandRegistration.Schema, commandPayload, AuthorityPayloadHashV1.Compute(commandRegistration.SchemaToken, commandRegistration.Schema, commandPayload), request.Correlation, request.ObservedAt);
        var initial = fixture.S1.ReadAsync(new(request.AuthenticatedReservation.Command.Position.Session, 0, long.MaxValue, 256, 1_048_576), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        var head = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(initial).SnapshotThrough;
        var commandCommit = fixture.S1.AppendAsync(new(request.AuthenticatedReservation.Command.Position.Session, head, [], [commandProposal], 1_048_576), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        var commandEnvelope = Assert.IsType<AppendAuthorityResultV1.Committed>(commandCommit).Envelopes.Single();
        var factBody = new GraphParticipantBindingFactBodyV1(commandBody.OperationId, commandEnvelope.Position, commandBody.ReservationFact, null, 2, commandBody.GraphGeneration, commandBody.RuntimeGeneration, commandBody.ParticipantPlanFingerprint, commandBody.TopologyFingerprint, commandBody.ExecutablePlanFingerprint, null, null, new("participant-binding-already-applied"), commandBody.ObservedAt);
        var factBodyBytes = GraphParticipantBindingCodecsV1.Encode(factBody);
        var factPayload = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingFactV1(request.AuthenticatedReservation.Command.Position.Session, reservationOuter.ExpectedAuthority, factBodyBytes));
        var factRegistration = GraphParticipantBindingPayloadRegistrationsV1.BindingFact;
        var factProposal = new ProposedAuthorityFactV1(GraphParticipantBindingFactIdsV1.BindingFact(commandEnvelope.Position), null, OwnerSliceId.S1, factRegistration.Schema, factPayload, AuthorityPayloadHashV1.Compute(factRegistration.SchemaToken, factRegistration.Schema, factPayload), request.Correlation, request.ObservedAt);
        Assert.IsType<AppendAuthorityResultV1.Committed>(fixture.S1.AppendAsync(new(request.AuthenticatedReservation.Command.Position.Session, commandEnvelope.Position.Sequence, [], [factProposal], 1_048_576), CancellationToken.None).AsTask().GetAwaiter().GetResult());
        fixture.S1.DurableRejection = true;
        fixture.S1.ResetCounters();
        return fixture;
    }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateCommandAttemptBound((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S1.AlwaysAmbiguousCommand = true; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateFactAttemptBound((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S1.AlwaysAmbiguousFact = true; return fixture; }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateS1ReadBound((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S1.NonProgressReads = true; var request = new GraphParticipantBindingRequestV2(fixture.Request.AppliedReservation, fixture.Request.AuthenticatedReservation, fixture.Request.BindingEvidence, fixture.Request.Correlation, fixture.Request.BindingObservedAt, fixture.Request.ObservedAt, 10, fixture.Request.MaximumSessionCanonicalBytes); return (request, fixture.S1, fixture.S2, fixture.Allocator, new(fixture.S1, fixture.S2, fixture.Allocator, fixture.Allocator.Lease)); }
    private static (GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) MutateS1AppendBound((GraphParticipantBindingRequestV2 Request, ScriptedAuthorityJournal S1, ScriptedAuthorityJournal S2, ScriptedAllocatorSnapshotPort Allocator, GraphParticipantBindingCoordinatorV2 Coordinator) fixture) { fixture.S1.AlwaysAmbiguousCommand = true; fixture.S1.AlwaysAmbiguousFact = true; return fixture; }

    private sealed class ScriptedAuthorityJournal : IAuthorityJournalV1
    {
        private readonly IAuthorityJournalV1 _inner; private readonly CancellationTokenSource _cancellation = new(); private bool _reconcilePending;
        internal ScriptedAuthorityJournal(IAuthorityJournalV1 inner) => _inner = inner;
        internal int ReadCalls { get; private set; } internal int AppendCalls { get; private set; } internal int ReconcileCalls { get; private set; }
        internal ReadAuthorityRangeResultV1? ReadResult { get; set; } internal bool MutateGrantId { get; set; } internal bool MutateGrantedAt { get; set; } internal bool MutateCurrentFact { get; set; } internal bool MutateExpiryBefore { get; set; } internal bool MutateExpiryIncomparable { get; set; } internal bool MutateChargeCount { get; set; } internal bool MutateRequest { get; set; } internal bool MutateBalance { get; set; } internal bool MutateCoverageBytes { get; set; } internal bool MutateCoverageHash { get; set; } internal bool FailOnSecondRead { get; set; }
        internal int LostAckOnAppend { get; set; } internal bool CancelAfterAppend { get; set; } internal bool CancelBeforeEntry { get; set; } internal bool CommandCommitted { get; set; } internal bool FactCommitted { get; set; } internal bool Contradiction { get; set; } internal bool DurableRejection { get; set; } internal bool AlwaysAmbiguousCommand { get; set; } internal bool AlwaysAmbiguousFact { get; set; } internal bool NonProgressReads { get; set; }
        internal CancellationToken CancellationToken { get { if (CancelBeforeEntry) _cancellation.Cancel(); return _cancellation.Token; } }
        internal void ResetCounters() { ReadCalls = 0; AppendCalls = 0; ReconcileCalls = 0; }
        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default)
        {
            AppendCalls++; if (Contradiction) return new AppendAuthorityResultV1.ContradictoryDuplicate(request.Facts[0].FactId, Hash256.Compute([1]), Hash256.Compute([2]));
            var isFact = request.Facts[0].PayloadSchema == GraphParticipantBindingPayloadRegistrationsV1.BindingFact.Schema;
            if (DurableRejection && isFact)
            {
                var proposed = request.Facts[0];
                Assert.True(GraphParticipantBindingCodecsV1.TryDecodeBindingFact(proposed.Payload.ToArray(), out var outer));
                Assert.NotNull(outer);
                Assert.True(GraphParticipantBindingCodecsV1.TryDecodeBindingFactBody(outer.BodyBytes.ToArray(), out var body));
                Assert.NotNull(body);
                var rejectedBody = new GraphParticipantBindingFactBodyV1(body.OperationId, body.CommandPosition, body.ReservationFact, body.ActualPredecessor, 2, body.GraphGeneration, body.RuntimeGeneration, body.ParticipantPlanFingerprint, body.TopologyFingerprint, body.ExecutablePlanFingerprint, null, null, new("participant-binding-already-applied"), body.ObservedAt);
                var bodyBytes = GraphParticipantBindingCodecsV1.Encode(rejectedBody);
                var rejectedOuter = new GraphParticipantBindingFactV1(outer.Session, outer.ExpectedAuthority, bodyBytes);
                var payload = GraphParticipantBindingCodecsV1.Encode(rejectedOuter);
                var registration = GraphParticipantBindingPayloadRegistrationsV1.BindingFact;
                var replacement = new ProposedAuthorityFactV1(proposed.FactId, proposed.ThreadId, proposed.Owner, proposed.PayloadSchema, payload, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload), proposed.Correlation, proposed.ObservedAt);
                request = new AppendAuthorityBatchV1(request.Session, request.ExpectedSessionHead, request.ExpectedThreadHeads, [replacement], request.MaximumEncodedBytes);
            }
            if (AlwaysAmbiguousCommand && !isFact || AlwaysAmbiguousFact && isFact)
            {
                if (AlwaysAmbiguousCommand && AlwaysAmbiguousFact && !isFact && AppendCalls == 3) _ = await _inner.AppendAsync(request, cancellationToken);
                _reconcilePending = true;
                return new AppendAuthorityResultV1.OutcomeUnknown(request.Facts[0].Correlation.OperationId!.Value);
            }
            var result = await _inner.AppendAsync(request, cancellationToken);
            if (LostAckOnAppend == AppendCalls) { _reconcilePending = true; if (CancelAfterAppend) _cancellation.Cancel(); return new AppendAuthorityResultV1.OutcomeUnknown(request.Facts[0].Correlation.OperationId!.Value); }
            return result;
        }
        public async ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default)
        {
            ReadCalls++; if (_reconcilePending) { ReconcileCalls++; _reconcilePending = false; }
            if (ReadResult is not null) return ReadResult;
            if (NonProgressReads)
            {
                var next = request.AfterExclusive + 1;
                var payload = new byte[] { 0x80 };
                var unrelated = new AuthorityFactEnvelopeV1(JournalFactId.Create(), new(request.Session, next), null, OwnerSliceId.S4, new(SchemaId.Create(), 1, 0), payload, Hash256.Compute(payload), new(TenantId.Create()), new(next), new(next), new(1, 1, Hash256.Compute([1]), []));
                return new ReadAuthorityRangeResultV1.Batch(request.Session, 11, request.AfterExclusive, 11, [unrelated], true);
            }
            if (FailOnSecondRead && ReadCalls > 1) return new ReadAuthorityRangeResultV1.StoreUnavailable(new("second-pinned-read"));
            var read = await _inner.ReadAsync(request, cancellationToken);
            if (read is not ReadAuthorityRangeResultV1.Batch batch || !(MutateGrantId || MutateExpiryBefore || MutateExpiryIncomparable || MutateChargeCount || MutateRequest || MutateBalance)) return read;
            var facts = batch.Facts.ToArray();
            for (var i = 0; i < facts.Length; i++)
            {
                var envelope = facts[i];
                if (envelope.PayloadSchema != new CapacityReservationPayloadRegistrationV1().Schema || !CapacityLedgerCodecsV1.TryDecodeReservation(envelope.PayloadMemory, out var decoded) || decoded is null) continue;
                var grantId = MutateGrantId ? CapacityGrantId.Create() : decoded.GrantId;
                var capacityRequest = decoded.Request;
                if (MutateRequest) capacityRequest = new CapacityRequestV1(OperationId.Create(), capacityRequest.Authority, capacityRequest.Charges, capacityRequest.Deadline, capacityRequest.Priority);
                if (MutateChargeCount) { var first=capacityRequest.Charges[0];var extra=new CapacityChargeV1(new CapacityDimensionId(4),first.Scope,first.Amount,CapacityPurposeId.Create(),first.Window);capacityRequest=new CapacityRequestV1(capacityRequest.OperationId,capacityRequest.Authority,[.. capacityRequest.Charges,extra],capacityRequest.Deadline,capacityRequest.Priority); }
                if (MutateBalance) { var first=capacityRequest.Charges[0];var changed=new CapacityChargeV1(first.DimensionId,first.Scope,first.Amount+1,first.Purpose,first.Window);capacityRequest=new CapacityRequestV1(capacityRequest.OperationId,capacityRequest.Authority,[changed],capacityRequest.Deadline,capacityRequest.Priority); }
                CapacityGrantExpiryV1 expiry = decoded.ExpiresAt;
                if (MutateExpiryBefore) expiry = new CapacityGrantExpiryV1.At(new MonotonicStampV1(capacityRequest.Deadline.ClockDomainId,capacityRequest.Deadline.BootId,1));
                if (MutateExpiryIncomparable) expiry = new CapacityGrantExpiryV1.At(new MonotonicStampV1(ClockDomainId.Create(),BootId.Create(),40));
                var changedBody = new CapacityReservationFactBodyV1(grantId,capacityRequest,expiry);var payload=CapacityLedgerCodecsV1.EncodeReservation(changedBody);var registration=new CapacityReservationPayloadRegistrationV1();
                facts[i]=new AuthorityFactEnvelopeV1(envelope.FactId,envelope.Position,envelope.ThreadScope,envelope.Owner,envelope.PayloadSchema,payload,AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,payload),envelope.Correlation,envelope.ObservedAt,envelope.AdmittedAt,envelope.Integrity);
            }
            return new ReadAuthorityRangeResultV1.Batch(batch.Session,batch.SnapshotHead,batch.AfterExclusive,batch.SnapshotThrough,facts,batch.HasMore);
        }
    }

    private sealed class ScriptedAllocatorSnapshotPort : IGlobalParticipantAllocatorDurableSnapshotPortV1, IGlobalParticipantAllocatorDurableCustodyV1
    {
        private readonly GlobalParticipantAllocatorExactRecordSnapshotV1 _baseline;
        internal ScriptedAllocatorSnapshotPort(GlobalParticipantAllocatorCompletedFoldV1 baseline, ReadOnlyMemory<byte> claimRecord)
        {
            _baseline = new(baseline.JournalId, baseline.Head, baseline.RecordCount, baseline.TotalCanonicalRecordBytes, [claimRecord]);
            var store = Hash256.Compute([14]); var manifest = new GlobalParticipantAllocatorRealmManifestV1(baseline.JournalId, 1, 1, store, default, GlobalParticipantAllocatorRealmManifestV1.ComputeManifestHash(baseline.JournalId, 1, 1, store, default)); Lease = new(manifest, this); Result = new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(_baseline);
        }
        internal GlobalParticipantAllocatorRealmLeaseV1 Lease { get; } internal GlobalParticipantAllocatorDurableSnapshotResultV1 Result { get; set; } internal string? Mutation { get; set; } internal int ReadCalls { get; set; }
        public ValueTask<GlobalParticipantAllocatorDurableSnapshotResultV1> ReadAsync(GlobalParticipantAllocatorDurableSnapshotRequestV1 request, CancellationToken cancellationToken)
        {
            ReadCalls++;
            if (Mutation == "journal-id") return ValueTask.FromResult<GlobalParticipantAllocatorDurableSnapshotResultV1>(new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(GlobalParticipantAllocatorJournalId.Create(), _baseline.Head, _baseline.RecordCount, _baseline.TotalCanonicalRecordBytes, _baseline.ExactCanonicalRecords)));
            if (Mutation == "fold-invalid") { var bytes = _baseline.ExactCanonicalRecords[0].ToArray(); bytes[0] ^= 1; return ValueTask.FromResult<GlobalParticipantAllocatorDurableSnapshotResultV1>(new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(_baseline.JournalId, _baseline.Head, 1, (ulong)bytes.Length, [bytes]))); }
            if (Mutation == "complete-count") return ValueTask.FromResult<GlobalParticipantAllocatorDurableSnapshotResultV1>(new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(_baseline.JournalId, _baseline.Head, 2, _baseline.TotalCanonicalRecordBytes, _baseline.ExactCanonicalRecords)));
            if (Mutation == "complete-bytes") return ValueTask.FromResult<GlobalParticipantAllocatorDurableSnapshotResultV1>(new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(_baseline.JournalId, _baseline.Head, _baseline.RecordCount, _baseline.TotalCanonicalRecordBytes + 1, _baseline.ExactCanonicalRecords)));
            if (Mutation == "owner-missing") return ValueTask.FromResult<GlobalParticipantAllocatorDurableSnapshotResultV1>(new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(_baseline.JournalId, null, 0, 0, [])));
            if (Mutation == "integrity")
            {
                var changedHead = new GlobalParticipantAuthorityHeadV1(_baseline.Head!.Value.Position, Hash256.Compute([94]));
                return ValueTask.FromResult<GlobalParticipantAllocatorDurableSnapshotResultV1>(new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(_baseline.JournalId, changedHead, _baseline.RecordCount, _baseline.TotalCanonicalRecordBytes, _baseline.ExactCanonicalRecords)));
            }
            if (Mutation == "claim-hash")
            {
                var changedRecord = _baseline.ExactCanonicalRecords[0].ToArray(); changedRecord[^1] ^= 1;
                return ValueTask.FromResult<GlobalParticipantAllocatorDurableSnapshotResultV1>(new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(_baseline.JournalId, _baseline.Head, 1, (ulong)changedRecord.Length, [changedRecord])));
            }
            if (Mutation is "participant" or "session" or "operation" or "source-command-position" or "source-outer-payload-hash" or "source-body-fingerprint" or "source-fingerprint" or "claim-position")
            {
                Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(_baseline.ExactCanonicalRecords[0], out var outer)); Assert.NotNull(outer);
                Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer.BodyBytes.ToArray(), out var body)); Assert.NotNull(body);
                var operation = Mutation == "operation" ? OperationId.Create() : body.OperationId;
                var source = body.Source;
                if (Mutation == "session") { var changedSession = new SessionAuthorityStampV1(body.Source.SourceFactPosition.Session.RuntimeGenerationId, LiveSessionId.Create()); source = new(changedSession.LiveSessionId, new(changedSession, body.Source.SourceFactPosition.Sequence), body.Source.SourceOuterIntegrityHash, body.Source.SourceBodyHash); }
                if (Mutation == "source-command-position") source = new(source.LiveSessionId, new(source.SourceFactPosition.Session, source.SourceFactPosition.Sequence + 1), source.SourceOuterIntegrityHash, source.SourceBodyHash);
                if (Mutation is "source-outer-payload-hash" or "source-fingerprint") source = new(source.LiveSessionId, source.SourceFactPosition, Hash256.Compute([91]), source.SourceBodyHash);
                if (Mutation == "source-body-fingerprint") source = new(source.LiveSessionId, source.SourceFactPosition, source.SourceOuterIntegrityHash, Hash256.Compute([92]));
                var sourceFingerprint = GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(source);
                var participant = Mutation == "participant" ? ParticipantId.Create() : Mutation == "source-fingerprint" ? GlobalParticipantAllocatorFactIdsV1.Participant(source.LiveSessionId, operation, sourceFingerprint) : body.ParticipantId;
                var proof = new ParticipantIdOwnerProofV1(participant, null, 1, null, GlobalParticipantAllocatorCodecsV1.EmptyIndexRoot(), GlobalParticipantAllocatorCodecsV1.CreateEmptyProofPath(), 0);
                var position = Mutation == "claim-position" ? new GlobalParticipantAuthorityPositionV1(GlobalParticipantAllocatorJournalId.Create(), 1) : body.AssignedPosition;
                var changedBody = new GlobalParticipantClaimRecordBodyV1(operation, source, participant, null, proof, new ParticipantIdClaimOutcomeV1(1, null, null), position, body.ObservedAt);
                var changedRecord = GlobalParticipantAllocatorCodecsV1.Encode(new GlobalParticipantClaimRecordV1(outer.SourceSession, outer.SourceExpectedAuthority, GlobalParticipantAllocatorCodecsV1.Encode(changedBody)));
                var changedFold = new GlobalParticipantAllocatorFoldAccumulatorV1(_baseline.JournalId);
                if (changedFold.Apply(changedRecord) is GlobalParticipantAllocatorFoldApplyResultV1.Accepted && changedFold.Complete() is GlobalParticipantAllocatorFoldResultV1.Current current)
                    return ValueTask.FromResult<GlobalParticipantAllocatorDurableSnapshotResultV1>(new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(current.Snapshot.JournalId, current.Snapshot.Head, current.Snapshot.RecordCount, current.Snapshot.TotalCanonicalRecordBytes, [changedRecord])));
                return ValueTask.FromResult<GlobalParticipantAllocatorDurableSnapshotResultV1>(new GlobalParticipantAllocatorDurableSnapshotResultV1.Current(new(_baseline.JournalId, _baseline.Head, 1, (ulong)changedRecord.Length, [changedRecord])));
            }
            return ValueTask.FromResult(Result);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
