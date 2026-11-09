using HPD.Agent.Audio;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.Graph.Runtime;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaResidenceTests
{
    [Fact]
    public void Derived_copy_plans_without_mutation_then_commits_new_media_owner_and_residence_atomically()
    {
        var fixture = CreateControlledFixture();
        Assert.True(GraphMediaBinding.TryCreate(0, 1_000, Id(210), 1, 16_000, 1, 2,
            Id(211), 1, 0, GraphMediaDiscontinuityKind.ResetBefore, 400, 200, null, out var media));
        var destinationKey = new GraphMediaOwnerKey(Session(), Graph(), Id(212));
        var destination = new GraphMediaOwnerRecord(fixture.Request.DestinationOwnerId,
            destinationKey, media!, GraphMediaOwnerState.Owned, 1);
        var authority = fixture.Request with
        {
            RequestHash = GraphMediaResidenceLedger.DerivedCopyHash(
                fixture.Request, destination, fixture.Assignment, fixture.Source.Version),
        };
        var request = new GraphMediaDerivedResidenceRequest(authority, fixture.Source.Version,
            destinationKey, media!);
        var residenceBefore = fixture.Ledger.Fingerprint;
        var ownershipBefore = fixture.Ownership.Fingerprint;

        var prepared = Assert.IsType<VoiceActivityDerivedResidencePreparationResult.Prepared>(
            VoiceActivityDerivedResidenceCommit.Prepare(request, fixture.Ledger, fixture.Ownership));
        var commit = prepared.Commit;

        Assert.False(commit.IsCommitted);
        Assert.Same(fixture.Ledger, commit.Residences);
        Assert.Same(fixture.Ownership, commit.Ownership);
        Assert.Equal(residenceBefore, commit.Residences.Fingerprint);
        Assert.Equal(ownershipBefore, commit.Ownership.Fingerprint);

        Assert.True(commit.TryCommit());
        Assert.True(commit.IsCommitted);
        Assert.NotEqual(residenceBefore, commit.Residences.Fingerprint);
        Assert.NotEqual(ownershipBefore, commit.Ownership.Fingerprint);
        var committedResidence = commit.Residences.Fingerprint;
        var committedOwnership = commit.Ownership.Fingerprint;
        Assert.True(commit.TryCommit());
        Assert.Equal(committedResidence, commit.Residences.Fingerprint);
        Assert.Equal(committedOwnership, commit.Ownership.Fingerprint);
        Assert.Equal(media, commit.Ownership.Owners[authority.DestinationOwnerId].Media);
        var visible = commit.Residences.Residences[authority.ResidenceId];
        Assert.Equal(GraphMediaResidenceState.Visible, visible.State);
        Assert.Equal(media, visible.Media);
        var firstBorrow = commit.Ownership.Acquire(Session(), Graph(), authority.DestinationOwnerId,
            Id(213), Hash(213));
        var secondBorrow = firstBorrow.Ledger.Acquire(Session(), Graph(), authority.DestinationOwnerId,
            Id(214), Hash(214));
        Assert.Equal(GraphMediaBorrowResult.Borrowed, firstBorrow.Result);
        Assert.Equal(GraphMediaBorrowResult.Borrowed, secondBorrow.Result);
        Assert.Collection(secondBorrow.Ledger.Borrows, _ => { }, _ => { });
        Assert.Single(commit.Residences.Residences);
        Assert.Single(commit.Residences.Residences.Values.Select(x => x.Assignment).Distinct());
        Assert.Equal(GraphMediaDerivedCopyResult.IdempotentCommitted,
            commit.Residences.PlanDerivedCopy(request, commit.Ownership).Result);
        var changedVersion = request with { ExpectedSourceVersion = request.ExpectedSourceVersion + 1 };
        Assert.Equal(GraphMediaDerivedCopyResult.ContradictoryDuplicate,
            commit.Residences.PlanDerivedCopy(changedVersion, commit.Ownership).Result);
    }

    [Fact]
    public void Derived_copy_discard_and_active_borrow_leave_both_ledgers_byte_identical()
    {
        var fixture = CreateControlledFixture();
        Assert.True(GraphMediaBinding.TryCreate(0, 1_000, Id(220), 1, 16_000, 1, 2,
            Id(221), 1, 0, GraphMediaDiscontinuityKind.ResetBefore, 400, 200, null, out var media));
        var destinationKey = new GraphMediaOwnerKey(Session(), Graph(), Id(222));
        var destination = new GraphMediaOwnerRecord(fixture.Request.DestinationOwnerId,
            destinationKey, media!, GraphMediaOwnerState.Owned, 1);
        var authority = fixture.Request with
        {
            RequestHash = GraphMediaResidenceLedger.DerivedCopyHash(
                fixture.Request, destination, fixture.Assignment, fixture.Source.Version),
        };
        var request = new GraphMediaDerivedResidenceRequest(authority, fixture.Source.Version,
            destinationKey, media!);
        var planned = fixture.Ledger.PlanDerivedCopy(request, fixture.Ownership);
        Assert.Equal(GraphMediaDerivedCopyResult.Planned, planned.Result);
        Assert.Empty(fixture.Ledger.Residences);
        Assert.Single(fixture.Ownership.Owners);

        var borrowed = fixture.Ownership.Acquire(Session(), Graph(), fixture.Source.OwnerId, Id(223), Hash(224));
        Assert.Equal(GraphMediaBorrowResult.Borrowed, borrowed.Result);
        var residenceBefore = fixture.Ledger.Fingerprint;
        var ownershipBefore = borrowed.Ledger.Fingerprint;
        var rejected = fixture.Ledger.PlanDerivedCopy(request, borrowed.Ledger);
        Assert.Equal(GraphMediaDerivedCopyResult.BorrowOutstanding, rejected.Result);
        Assert.Equal(residenceBefore, rejected.ResidenceLedger.Fingerprint);
        Assert.Equal(ownershipBefore, rejected.OwnershipLedger.Fingerprint);
    }

    [Fact]
    public void Explicit_unknown_ingress_requires_exact_schema_capacity_and_is_never_publishable()
    {
        var fixture = CreateQuarantineFixture(); var before = fixture.Ledger.Fingerprint;
        var transition = fixture.Ledger.Quarantine(fixture.Request, fixture.Ownership);
        Assert.Equal(GraphMediaResidenceResult.Quarantined, transition.Result);
        Assert.NotEqual(before, transition.Ledger.Fingerprint);
        var row = Assert.Single(transition.Ledger.Quarantines).Value;
        Assert.Equal(GraphMediaResidenceClass.Quarantine, row.Class);
        Assert.Equal(GraphMediaResidenceState.Quarantined, row.State);
        Assert.Equal((ushort)12, row.Charge.DimensionId.Value);
        Assert.Equal(CapacityScopeKindV1.Schema, row.Charge.Scope.Kind);
        Assert.Equal(fixture.Source.Media.ByteLength, row.Charge.Amount);
        Assert.Equal(GraphMediaResidenceResult.IdempotentQuarantined,
            transition.Ledger.Quarantine(fixture.Request, fixture.Ownership).Result);
        Assert.Equal(GraphMediaResidenceResult.WrongState,
            transition.Ledger.MakeVisible(fixture.Request.OperationId, fixture.Request.RequestHash, fixture.Ownership).Result);
    }

    [Fact]
    public void Quarantine_wrong_source_scope_amount_authority_and_duplicate_fail_before_mutation()
    {
        var fixture = CreateQuarantineFixture();
        var cases = new List<(GraphMediaQuarantineIngressRequest Request, GraphMediaOwnershipLedger Ownership,
            GraphMediaResidenceLedger Ledger, GraphMediaResidenceResult Expected)>();
        cases.Add((fixture.Request with { SourceResidenceId = Id(199) }, fixture.Ownership, fixture.Ledger,
            GraphMediaResidenceResult.WrongState));
        var wrongSchemaCharge = new CapacityChargeV1(new(12), new(TenantId.FromValue(Id(17)),
            SessionId.FromValue(Id(18)), new CapacitySubjectV1.Schema(SchemaId.FromValue(Id(16)))),
            fixture.Source.Media.ByteLength, CapacityPurposeId.FromValue(Id(19)), new CapacityChargeWindowV1.NoWindow());
        cases.Add((WithGrant(fixture.Request, Grant(fixture, wrongSchemaCharge)), fixture.Ownership, fixture.Ledger,
            GraphMediaResidenceResult.CapacityMismatch));
        var schemaCharge = fixture.Request.Grant.Balances[0].Charge;
        var wrongAmount = new CapacityChargeV1(schemaCharge.DimensionId, schemaCharge.Scope, schemaCharge.Amount + 1,
            schemaCharge.Purpose, schemaCharge.Window);
        cases.Add((WithGrant(fixture.Request, Grant(fixture, wrongAmount)), fixture.Ownership, fixture.Ledger,
            GraphMediaResidenceResult.CapacityMismatch));
        var staleAuthority = ExpectedAuthorityVectorV1.Create(new(RuntimeGenerationId.FromValue(Id(201)),
            fixture.Ownership.Session.LiveSessionId), [new AuthorityAxisValueV1.Graph(Graph())]);
        cases.Add((WithGrant(fixture.Request, Grant(fixture, schemaCharge, staleAuthority)), fixture.Ownership,
            fixture.Ledger, GraphMediaResidenceResult.StaleGeneration));
        foreach (var item in cases)
        {
            var before = item.Ledger.Fingerprint; var result = item.Ledger.Quarantine(item.Request, item.Ownership);
            Assert.Equal(item.Expected, result.Result); Assert.Same(item.Ledger, result.Ledger);
            Assert.Equal(before, result.Ledger.Fingerprint);
        }
        var accepted = fixture.Ledger.Quarantine(fixture.Request, fixture.Ownership);
        var changed = fixture.Request with { RequestHash = Hash(202) };
        Assert.Equal(GraphMediaResidenceResult.ContradictoryDuplicate,
            accepted.Ledger.Quarantine(changed, fixture.Ownership).Result);
    }

    [Fact]
    public void Quarantine_has_an_exact_sixteen_item_ceiling()
    {
        var fixture = CreateQuarantineFixture();
        var ledger = fixture.Ledger;
        for (byte index = 0; index < GraphMediaResidenceLedger.MaximumQuarantine; index++)
        {
            var request = QuarantineRequest(fixture, (byte)(110 + index));
            var transition = ledger.Quarantine(request, fixture.Ownership);
            Assert.Equal(GraphMediaResidenceResult.Quarantined, transition.Result);
            ledger = transition.Ledger;
        }
        Assert.Equal(GraphMediaResidenceLedger.MaximumQuarantine, ledger.Quarantines.Count);
        var before = ledger.Fingerprint;
        var overflow = ledger.Quarantine(QuarantineRequest(fixture, 140), fixture.Ownership);
        Assert.Equal(GraphMediaResidenceResult.ResidenceLimitReached, overflow.Result);
        Assert.Same(ledger, overflow.Ledger);
        Assert.Equal(before, overflow.Ledger.Fingerprint);
    }

    [Fact]
    public void Qualified_opaque_ingress_retains_finite_credits_without_claiming_physical_size()
    {
        var fixture = CreateOpaqueFixture(); var before = fixture.Ledger.Fingerprint;
        var accepted = fixture.Ledger.AdmitOpaque(fixture.Request, fixture.Ownership);
        Assert.Equal(GraphMediaResidenceResult.OpaqueAdmitted, accepted.Result);
        Assert.NotEqual(before, accepted.Ledger.Fingerprint);
        var row = Assert.Single(accepted.Ledger.Opaques).Value;
        Assert.Equal(GraphMediaResidenceClass.Opaque, row.Class);
        Assert.Equal((ushort)2, row.SubmittedOperations); Assert.Equal(400UL, row.SubmittedBytes);
        Assert.Equal(1_000L, row.MaximumAge.Nanoseconds);
        Assert.Equal(fixture.Provider.ProviderId, row.ProviderId);
        Assert.Null(typeof(GraphMediaOpaqueResidence).GetProperty("ByteLength"));
        Assert.Equal(GraphMediaResidenceResult.IdempotentOpaqueAdmitted,
            accepted.Ledger.AdmitOpaque(fixture.Request, fixture.Ownership).Result);
        Assert.Equal(GraphMediaResidenceResult.WrongState,
            accepted.Ledger.MakeVisible(fixture.Request.OperationId, fixture.Request.RequestHash, fixture.Ownership).Result);
    }

    [Fact]
    public void Opaque_provider_catalog_qualification_credit_and_capacity_mismatches_fail_without_mutation()
    {
        var fixture = CreateOpaqueFixture();
        var wrongProvider = ProviderContribution(ProviderId.FromValue(Id(161)));
        var cases = new[]
        {
            fixture.Request with { SelectedProvider = wrongProvider },
            fixture.Request with { SubmittedOperations = 9, RequestHash = Hash(162) },
            fixture.Request with { SubmittedBytes = 4_194_305, RequestHash = Hash(163) },
            fixture.Request with { MaximumAge = new DurationNs(10_000_000_001), RequestHash = Hash(164) },
            fixture.Request with { Grant = CloneGrant(fixture.Request.Grant, state: CapacityGrantStateV1.Unknown), RequestHash = Hash(165) },
        };
        foreach (var request in cases)
        {
            var before = fixture.Ledger.Fingerprint; var result = fixture.Ledger.AdmitOpaque(request, fixture.Ownership);
            Assert.Contains(result.Result, new[] { GraphMediaResidenceResult.AuthorityMismatch, GraphMediaResidenceResult.CapacityMismatch });
            Assert.Same(fixture.Ledger, result.Ledger); Assert.Equal(before, result.Ledger.Fingerprint);
        }
        var accepted = fixture.Ledger.AdmitOpaque(fixture.Request, fixture.Ownership);
        Assert.Equal(GraphMediaResidenceResult.ContradictoryDuplicate,
            accepted.Ledger.AdmitOpaque(fixture.Request with { RequestHash = Hash(166) }, fixture.Ownership).Result);
    }

    [Fact]
    public void Opaque_has_an_exact_sixteen_item_ceiling()
    {
        var fixture = CreateOpaqueFixture(); var ledger = fixture.Ledger;
        for (byte index = 0; index < GraphMediaResidenceLedger.MaximumOpaque; index++)
        {
            var request = OpaqueRequest(fixture, (byte)(170 + index));
            var accepted = ledger.AdmitOpaque(request, fixture.Ownership);
            Assert.Equal(GraphMediaResidenceResult.OpaqueAdmitted, accepted.Result); ledger = accepted.Ledger;
        }
        Assert.Equal(GraphMediaResidenceLedger.MaximumOpaque, ledger.Opaques.Count);
        var overflow = ledger.AdmitOpaque(OpaqueRequest(fixture, 190), fixture.Ownership);
        Assert.Equal(GraphMediaResidenceResult.ResidenceLimitReached, overflow.Result);
        Assert.Same(ledger, overflow.Ledger);
    }

    [Fact]
    public void Controlled_all_three_representation_arms_are_exact()
    {
        AssertControlledCase("bytes", "MutateNone", "Prepared", "PrepareControlled");
        AssertControlledCase("samples", "MutateArmSamples", "Prepared", "PrepareControlled");
        AssertControlledCase("timed-buffer", "MutateArmTimedBuffer", "Prepared", "PrepareControlled");
    }

    [Fact]
    public void Controlled_every_authority_join_fails_before_mutation()
    {
        AssertControlledCase("binding-command-position","MutateBindingCommandPosition","AuthorityMismatch","PrepareControlled");AssertControlledCase("binding-fact-position","MutateBindingFactPosition","AuthorityMismatch","PrepareControlled");AssertControlledCase("binding-fact-bytes","MutateBindingFactBytes","AuthorityMismatch","PrepareControlled");AssertControlledCase("binding-value","MutateBindingValue","AuthorityMismatch","PrepareControlled");AssertControlledCase("binding-proof","MutateBindingProof","AuthorityMismatch","PrepareControlled");AssertControlledCase("reservation-command-position","MutateReservationCommandPosition","AuthorityMismatch","PrepareControlled");AssertControlledCase("reservation-fact-position","MutateReservationFactPosition","AuthorityMismatch","PrepareControlled");AssertControlledCase("operation","MutateOperation","AuthorityMismatch","PrepareControlled");AssertControlledCase("participant","MutateParticipant","AuthorityMismatch","PrepareControlled");AssertControlledCase("factory","MutateFactory","AuthorityMismatch","PrepareControlled");AssertControlledCase("nodes","MutateNodes","AuthorityMismatch","PrepareControlled");AssertControlledCase("graph","MutateGraph","AuthorityMismatch","PrepareControlled");AssertControlledCase("session","MutateSession","StaleGeneration","PrepareControlled");AssertControlledCase("grant-id","MutateGrantId","AuthorityMismatch","PrepareControlled");AssertControlledCase("granted-at","MutateGrantedAt","AuthorityMismatch","PrepareControlled");AssertControlledCase("current-fact","MutateCurrentFact","AuthorityMismatch","PrepareControlled");AssertControlledCase("charge-count","MutateChargeCount","AuthorityMismatch","PrepareControlled");AssertControlledCase("coverage","MutateCoverage","AuthorityMismatch","PrepareControlled");AssertControlledCase("topology","MutateTopology","AuthorityMismatch","PrepareControlled");AssertControlledCase("executable","MutateExecutable","AuthorityMismatch","PrepareControlled");AssertControlledCase("destination-node","MutateDestinationNode","AuthorityMismatch","PrepareControlled");AssertControlledCase("charge-missing","MutateChargeMissing","CapacityMismatch","PrepareControlled");AssertControlledCase("charge-scope","MutateChargeScope","CapacityMismatch","PrepareControlled");AssertControlledCase("charge-amount","MutateChargeAmount","CapacityMismatch","PrepareControlled");
    }

    [Fact]
    public void Controlled_retry_contradiction_and_capacity_assignment_are_closed()
    {
        AssertControlledCase("exact-retry","MutateExactRetry","IdempotentPrepared","PrepareControlled");AssertControlledCase("contradictory-retry","MutateContradictoryRetry","ContradictoryDuplicate","PrepareControlled");AssertControlledCase("assignment-conflict","MutateAssignmentConflict","CapacityAssignmentConflict","PrepareControlled");
        var fixture = CreateControlledFixture();
        var first = fixture.Ledger.PrepareControlled(fixture.Request, fixture.Ownership);
        Assert.Equal(GraphMediaResidenceResult.Prepared, first.Result);
        var collision = Retarget(fixture, Operation(93), fixture.Request.ResidenceId, Id(94));
        var rejected = first.Ledger.PrepareControlled(collision.Request, collision.Ownership);
        Assert.Equal(GraphMediaResidenceResult.InvalidRequest, rejected.Result);
        Assert.Same(first.Ledger, rejected.Ledger);
    }

    [Fact]
    public void Controlled_bounds_and_unknown_reconciliation_are_closed()
    {
        AssertControlledCase("residence-limit","MutateResidenceLimit","ResidenceLimitReached","PrepareControlled");AssertControlledCase("unknown-reconcile","MutateUnknownReconcile","Reconciled","Reconcile");
    }

    [Fact]
    public void Copy_fanout_commits_all_destinations_atomically() => AssertFanoutCase("copy","MutateCopy","Committed","CommitFanout");

    [Fact]
    public void Transfer_fanout_uses_owner_linearization() => AssertFanoutCase("transfer","MutateTransfer","Committed","CommitFanout");

    [Fact]
    public void Fanout_precommit_failure_unwinds_in_reverse_without_release() => AssertFanoutCase("precommit-failure","MutatePrecommitFailure","Unwinding","FailFanout");

    [Fact]
    public void Fanout_retry_unknown_reconcile_and_bounds_are_closed()
    {
        AssertFanoutCase("destination-order","MutateDestinationOrder","DestinationOrderInvalid","PrepareFanout");AssertFanoutCase("destination-duplicate","MutateDestinationDuplicate","DestinationCollision","PrepareFanout");AssertFanoutCase("residence-authority","MutateResidenceAuthority","ResidenceMismatch","PrepareFanout");AssertFanoutCase("capacity-overlap","MutateCapacityOverlap","CapacityMismatch","PrepareFanout");AssertFanoutCase("owner-limit","MutateOwnerLimit","OwnerLimitReached","CommitFanout");AssertFanoutCase("residence-limit","MutateFanoutResidenceLimit","ResidenceLimitReached","PrepareFanout");AssertFanoutCase("exact-retry","MutateFanoutExactRetry","IdempotentPrepared","PrepareFanout");AssertFanoutCase("contradictory-retry","MutateFanoutContradiction","ContradictoryDuplicate","PrepareFanout");AssertFanoutCase("commit-drift","MutateCommitDrift","WrongState","CommitFanout");AssertFanoutCase("unknown-reconcile","MutateFanoutUnknownReconcile","Reconciled","ReconcileFanout");AssertFanoutCase("destination-max","MutateDestinationMax","Prepared","PrepareFanout");AssertFanoutCase("destination-max-plus-one","MutateDestinationMaxPlusOne","InvalidRequest","PrepareFanout");

        var fixture = CreateFanoutFixture();
        var prepared = fixture.Ledger.PrepareFanout(fixture.Request, fixture.Ownership);
        var committed = prepared.ResidenceLedger.CommitFanout(fixture.Request.OperationId, fixture.Request.RequestHash, fixture.Ownership);
        Assert.Equal(GraphMediaFanoutResult.Committed, committed.Result);
        Assert.Equal(GraphMediaFanoutResult.WrongState,
            committed.ResidenceLedger.CommitFanout(fixture.Request.OperationId, fixture.Request.RequestHash, fixture.Ownership).Result);
        Assert.Equal(GraphMediaFanoutResult.WrongState,
            committed.ResidenceLedger.PrepareFanout(fixture.Request, fixture.Ownership).Result);

        var controlled = CreateControlledFixture();
        var foreignSession = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(95)), LiveSessionId.FromValue(Id(96)));
        var foreignOwnership = GraphMediaOwnershipLedger.Create(
            new GraphMediaOwnerKey(foreignSession, Graph(), Id(14)), controlled.Request.SourceOwnerId, controlled.Source.Media);
        Assert.Equal(GraphMediaFanoutResult.WrongState,
            prepared.ResidenceLedger.LoseFanoutOutcome(fixture.Request.OperationId, fixture.Request.RequestHash, foreignOwnership).Result);
        Assert.Equal(GraphMediaFanoutResult.WrongState,
            prepared.ResidenceLedger.FailFanout(fixture.Request.OperationId, fixture.Request.RequestHash, foreignOwnership).Result);

        var two = FanoutDestinations(CreateFanoutFixture(), 2);
        var preparedTwo = two.Ledger.PrepareFanout(two.Request, two.Ownership);
        var partial = two.Ownership.CopyOwners(Session(), Graph(), two.Request.SourceOwnerId,
            [two.Request.Destinations[0].DestinationOwnerId]);
        Assert.Equal(GraphMediaOwnershipBatchCopyResult.Copied, partial.Result);
        Assert.Equal(GraphMediaFanoutResult.WrongState,
            preparedTwo.ResidenceLedger.LoseFanoutOutcome(two.Request.OperationId, two.Request.RequestHash, partial.Ledger).Result);
        Assert.Equal(GraphMediaFanoutResult.WrongState,
            preparedTwo.ResidenceLedger.FailFanout(two.Request.OperationId, two.Request.RequestHash, partial.Ledger).Result);

        var unknown = MutateFanoutUnknownReconcile(CreateFanoutFixture());
        var reconciled = unknown.Ledger.ReconcileFanout(unknown.Request.OperationId, unknown.Request.RequestHash, unknown.Ownership);
        Assert.Equal(GraphMediaFanoutResult.Reconciled, reconciled.Result);
        Assert.All(unknown.Request.Destinations, destination =>
            Assert.Equal(GraphMediaResidenceState.Visible,
                reconciled.ResidenceLedger.Residences[destination.Residence.ResidenceId].State));
    }

    [Fact]
    public void Surface_and_forbidden_effect_inventory_is_exact()
    {
        Assert.False(typeof(GraphMediaResidenceLedger).IsPublic);
        Assert.False(typeof(GraphMediaControlledResidenceRequest).IsPublic);
        Assert.Equal(64, GraphMediaResidenceLedger.MaximumControlled);
        Assert.Equal(16, GraphMediaResidenceLedger.MaximumDestinations);
    }

    private static void AssertControlledCase(string label, string mutator, string expectedResult, string expectedApi)
    {
        var fixture = CreateControlledFixture();
        fixture = label switch
        {
            "bytes" => MutateNone(fixture), "samples" => MutateArmSamples(fixture), "timed-buffer" => MutateArmTimedBuffer(fixture),
            "binding-command-position" => MutateBindingCommandPosition(fixture), "binding-fact-position" => MutateBindingFactPosition(fixture), "binding-fact-bytes" => MutateBindingFactBytes(fixture), "binding-value" => MutateBindingValue(fixture), "binding-proof" => MutateBindingProof(fixture), "reservation-command-position" => MutateReservationCommandPosition(fixture), "reservation-fact-position" => MutateReservationFactPosition(fixture), "operation" => MutateOperation(fixture), "participant" => MutateParticipant(fixture), "factory" => MutateFactory(fixture), "nodes" => MutateNodes(fixture), "graph" => MutateGraph(fixture), "session" => MutateSession(fixture), "grant-id" => MutateGrantId(fixture), "granted-at" => MutateGrantedAt(fixture), "current-fact" => MutateCurrentFact(fixture), "charge-count" => MutateChargeCount(fixture), "coverage" => MutateCoverage(fixture), "topology" => MutateTopology(fixture), "executable" => MutateExecutable(fixture), "destination-node" => MutateDestinationNode(fixture), "charge-missing" => MutateChargeMissing(fixture), "charge-scope" => MutateChargeScope(fixture), "charge-amount" => MutateChargeAmount(fixture), "exact-retry" => MutateExactRetry(fixture), "contradictory-retry" => MutateContradictoryRetry(fixture), "assignment-conflict" => MutateAssignmentConflict(fixture), "residence-limit" => MutateResidenceLimit(fixture), "unknown-reconcile" => MutateUnknownReconcile(fixture),
            _ => throw new InvalidOperationException(label)
        };
        var beforeLedger=fixture.Ledger;var beforeFingerprint=fixture.Ledger.Fingerprint;
        var result = expectedApi switch { "PrepareControlled"=>fixture.Ledger.PrepareControlled(fixture.Request,fixture.Ownership),"Reconcile"=>fixture.Ledger.Reconcile(fixture.Request.OperationId,fixture.Request.RequestHash,false,fixture.Ownership),_=>throw new InvalidOperationException(expectedApi)};
        var afterFingerprint=result.Ledger.Fingerprint;Assert.Equal(expectedResult,result.Result.ToString());Assert.False(string.IsNullOrWhiteSpace(mutator));
        if(expectedResult is "AuthorityMismatch" or "StaleGeneration" or "CapacityMismatch" or "IdempotentPrepared" or "ContradictoryDuplicate" or "CapacityAssignmentConflict" or "ResidenceLimitReached")
        {Assert.Equal(beforeFingerprint,afterFingerprint);Assert.Same(beforeLedger,result.Ledger);}
        else {Assert.NotEqual(beforeFingerprint,afterFingerprint);Assert.NotSame(beforeLedger,result.Ledger);}
    }

    private static void AssertFanoutCase(string label, string mutator, string expectedResult, string expectedApi)
    {
        var fixture = CreateFanoutFixture();
        fixture = label switch
        {
            "copy" => MutateCopy(fixture), "transfer" => MutateTransfer(fixture), "destination-order" => MutateDestinationOrder(fixture), "destination-duplicate" => MutateDestinationDuplicate(fixture), "residence-authority" => MutateResidenceAuthority(fixture), "capacity-overlap" => MutateCapacityOverlap(fixture), "owner-limit" => MutateOwnerLimit(fixture), "residence-limit" => MutateFanoutResidenceLimit(fixture), "exact-retry" => MutateFanoutExactRetry(fixture), "contradictory-retry" => MutateFanoutContradiction(fixture), "precommit-failure" => MutatePrecommitFailure(fixture), "commit-drift" => MutateCommitDrift(fixture), "unknown-reconcile" => MutateFanoutUnknownReconcile(fixture), "destination-max" => MutateDestinationMax(fixture), "destination-max-plus-one" => MutateDestinationMaxPlusOne(fixture),
            _ => throw new InvalidOperationException(label)
        };
        var beforeResidenceLedger=fixture.Ledger;var beforeOwnershipLedger=fixture.Ownership;var beforeFingerprint=fixture.Ledger.Fingerprint;
        var result=expectedApi switch{"PrepareFanout"=>fixture.Ledger.PrepareFanout(fixture.Request,fixture.Ownership),"CommitFanout"=>fixture.Ledger.CommitFanout(fixture.Request.OperationId,fixture.Request.RequestHash,fixture.Ownership),"FailFanout"=>fixture.Ledger.FailFanout(fixture.Request.OperationId,fixture.Request.RequestHash,fixture.Ownership),"ReconcileFanout"=>fixture.Ledger.ReconcileFanout(fixture.Request.OperationId,fixture.Request.RequestHash,fixture.Ownership),_=>throw new InvalidOperationException(expectedApi)};
        var afterFingerprint=result.ResidenceLedger.Fingerprint;Assert.Equal(expectedResult,result.Result.ToString());Assert.False(string.IsNullOrWhiteSpace(mutator));
        if(expectedResult is "DestinationOrderInvalid" or "DestinationCollision" or "ResidenceMismatch" or "CapacityMismatch" or "OwnerLimitReached" or "ResidenceLimitReached" or "IdempotentPrepared" or "ContradictoryDuplicate" or "WrongState" or "InvalidRequest")
        {Assert.Equal(beforeFingerprint,afterFingerprint);Assert.Same(beforeResidenceLedger,result.ResidenceLedger);Assert.Same(beforeOwnershipLedger,result.OwnershipLedger);}
        else {Assert.NotEqual(beforeFingerprint,afterFingerprint);Assert.NotSame(beforeResidenceLedger,result.ResidenceLedger);}
    }

    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) CreateControlledFixture()
    {
        var session = Session(); var graph = Graph(); var sourceId = Id(10); var destinationId = Id(11);
        Assert.True(GraphMediaBinding.TryCreate(0, 1_000, Id(12), 1, 48_000, 2, 2, Id(13), 1, 0, GraphMediaDiscontinuityKind.ResetBefore, 400, 100, null, out var media));
        var ownership = GraphMediaOwnershipLedger.Create(new(session, graph, Id(14)), sourceId, media!);
        var source = ownership.Owners[sourceId]; var operation = Operation(15); var participant = ParticipantId.FromValue(Id(16));
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(graph)]);
        var scope = new CapacityScopeV1(TenantId.FromValue(Id(17)), SessionId.FromValue(Id(18)), new CapacitySubjectV1.Participant(participant));
        var charges = new[] { Charge(1, 400, 19, scope), Charge(4, 200, 20, scope), Charge(5, 1_000, 21, scope) };
        var request = new CapacityRequestV1(operation, authority, charges, Stamp(30), CapacityPriorityV1.Normal);
        var reservationCommand = Position(1); var reservationFact = Position(2);
        var preGrant = new GraphParticipantPreGrantPlan(participant, operation, reservationCommand, reservationFact, graph, Hash(22), new("factory"), [1], Hash(23), [new("node")], [2], Hash(24), request);
        var grantId = CapacityGrantId.FromValue(Id(25));
        var topology = new GraphTopologyPlan(session, graph, grantId, [new(new("node"))], [], [new(1), new(4), new(5)]);
        var catalog = Assert.IsType<GraphRuntimeExecutableCatalogResult.Created>(GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest([new(new("node"), "tests:node@1", 1)]));
        var executable = Assert.IsType<GraphRuntimeExecutableCompileResult.Compiled>(GraphRuntimeExecutablePlan.Compile(topology, topology.Fingerprint, catalog, charges)).Plan;
        var evidence = new GraphParticipantBindingPlanEvidence(preGrant, grantId, Position(30), Position(31), new CapacityGrantExpiryV1.NoExpiry(), [3], Hash(26), topology, executable, topology.Fingerprint, executable.Fingerprint);
        var reservation = new GraphParticipantReservationV1(participant, new("factory"), [new("node")]);
        var reservationApplied = new GraphParticipantReservationFoldV2.AppliedReservation(Envelope(reservationCommand, [1]), Envelope(reservationFact, [2]), reservation);
        var proof = new CapacityGrantBindingProofV1(grantId, evidence.GrantedAt, evidence.CurrentFact, 3, evidence.CoverageHashV2);
        var binding = new GraphParticipantBindingV1(participant, new("factory"), [new("node")]);
        var bindingCommandBody = new GraphParticipantBindingCommandBodyV1(operation, reservationFact, null, graph, session.RuntimeGenerationId, preGrant.ParticipantPlanFingerprint, evidence.TopologyFingerprint, evidence.ExecutableFingerprint, proof, Stamp(40));
        var bindingCommandPayload = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(session, authority, GraphParticipantBindingCodecsV1.Encode(bindingCommandBody)));
        var bindingCommand = Envelope(Position(3), bindingCommandPayload, GraphParticipantBindingPayloadRegistrationsV1.BindingCommand);
        var bindingFact = Envelope(Position(4), [9], GraphParticipantBindingPayloadRegistrationsV1.BindingFact);
        var bound = new GraphParticipantBindingResult.Bound(bindingCommand.Position, bindingFact.Position, bindingFact.PayloadMemory, binding, proof);
        var foldBound = new GraphParticipantBindingFoldQueryResultV2.Bound(reservationApplied, bindingCommand, bindingFact, binding, proof);
        var controlled = new GraphMediaControlledResidenceRequest(operation, Hash(27), Id(28), sourceId, destinationId, new("node"), GraphMediaRepresentationArm.ResidentBytes, bound, foldBound, evidence);
        var assignment = new GraphMediaCapacityAssignment(charges[0], controlled.Arm);
        controlled = controlled with { RequestHash = GraphMediaResidenceLedger.ResidenceHash(controlled, source, assignment) };
        return (controlled, ownership, GraphMediaResidenceLedger.Create(session, graph), assignment, source);
    }

    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) CreateFanoutFixture()
    {
        var controlled = CreateControlledFixture();
        var destination = new GraphMediaFanoutDestination(controlled.Request.DestinationOwnerId, controlled.Request.DestinationNodeKey, controlled.Request);
        var request = new GraphMediaFanoutRequest(Operation(50), Hash(51), controlled.Request.SourceOwnerId, 1, GraphMediaFanoutMode.Copy, [destination]);
        request = request with { RequestHash = GraphMediaResidenceLedger.FanoutHash(request, controlled.Source) };
        return (request, controlled.Ownership, controlled.Ledger);
    }

    private static (GraphMediaQuarantineIngressRequest Request, GraphMediaOwnershipLedger Ownership,
        GraphMediaResidenceLedger Ledger, GraphMediaOwnerRecord Source) CreateQuarantineFixture()
    {
        var controlled = CreateControlledFixture();
        var copied = controlled.Ownership.CopyOwners(Session(), Graph(), controlled.Request.SourceOwnerId,
            [controlled.Request.DestinationOwnerId]);
        Assert.Equal(GraphMediaOwnershipBatchCopyResult.Copied, copied.Result);
        var prepared = controlled.Ledger.PrepareControlled(controlled.Request, copied.Ledger);
        Assert.Equal(GraphMediaResidenceResult.Prepared, prepared.Result);
        var unknown = prepared.Ledger.LoseOutcome(controlled.Request.OperationId, controlled.Request.RequestHash);
        Assert.Equal(GraphMediaResidenceResult.OutcomeUnknown, unknown.Result);
        var source = copied.Ledger.Owners[controlled.Request.DestinationOwnerId];
        var schema = SchemaId.FromValue(Id(90));
        var charge = new CapacityChargeV1(new(12), new(TenantId.FromValue(Id(17)), SessionId.FromValue(Id(18)),
            new CapacitySubjectV1.Schema(schema)), source.Media.ByteLength, CapacityPurposeId.FromValue(Id(91)),
            new CapacityChargeWindowV1.NoWindow());
        var placeholder = new GraphMediaQuarantineIngressRequest(Operation(92), Hash(1), Id(93),
            controlled.Request.ResidenceId, source.OwnerId, schema,
            new CapacityGrantSnapshotV1(CapacityGrantId.FromValue(Id(94)), Operation(95),
                ExpectedAuthorityVectorV1.Create(Session(), [new AuthorityAxisValueV1.Graph(Graph())]),
                Position(40), Position(41), new CapacityGrantExpiryV1.NoExpiry(), CapacityGrantStateV1.Reserved,
                [new CapacityChargeBalanceV1(charge, charge.Amount, 0, charge.Amount, 0, 0, 0, 0, 0, 0, charge.Amount, 0)]));
        var proof = GraphMediaResidenceLedger.QuarantineCapacityProofHash(placeholder.Grant);
        var request = placeholder with { RequestHash = GraphMediaResidenceLedger.QuarantineHash(placeholder, source, charge, proof) };
        return (request, copied.Ledger, unknown.Ledger, source);
    }

    private static (GraphMediaOpaqueIngressRequest Request, GraphMediaOwnershipLedger Ownership,
        GraphMediaResidenceLedger Ledger, GraphMediaOwnerRecord Source, ProviderContributionV1 Provider) CreateOpaqueFixture()
    {
        var controlled = CreateControlledFixture();
        var copied = controlled.Ownership.CopyOwners(Session(), Graph(), controlled.Request.SourceOwnerId,
            [controlled.Request.DestinationOwnerId]);
        Assert.Equal(GraphMediaOwnershipBatchCopyResult.Copied, copied.Result);
        var prepared = controlled.Ledger.PrepareControlled(controlled.Request, copied.Ledger);
        Assert.Equal(GraphMediaResidenceResult.Prepared, prepared.Result);
        var visible = prepared.Ledger.MakeVisible(controlled.Request.OperationId, controlled.Request.RequestHash, copied.Ledger);
        Assert.Equal(GraphMediaResidenceResult.Visible, visible.Result);
        var providerId = ProviderId.FromValue(Id(150)); var provider = ProviderContribution(providerId);
        var qualification = new LiveAudioOpaqueResidenceQualification(providerId, 8, 4_194_304,
            new DurationNs(10_000_000_000), LiveAudioOpaqueResidenceControl.ObservationOnly);
        var descriptor = new LiveAudioParticipantDescriptor(new("factory"), OwnerSliceId.S2,
            AuthorityAxisId.Graph, [], [new CapacityDimensionId(1)], new(1), new(1), new(1));
        var registration = new LiveAudioParticipantFactoryRegistration(typeof(GraphMediaResidenceTests),
            "tests:opaque", descriptor, ReadOnlyMemory<byte>.Empty, null, qualification);
        var participantCatalog = LiveAudioParticipantCatalogManifest.Create([registration]);
        var providerCatalog = new ProviderCatalogV1([provider]); var operation = Operation(151);
        var admittedAt = Stamp(100); var maximumAge = new DurationNs(1_000);
        var expiry = new MonotonicStampV1(admittedAt.ClockDomainId, admittedAt.BootId, admittedAt.Nanoseconds + 1_000);
        var tenant = TenantId.FromValue(Id(152)); var sessionId = SessionId.FromValue(Id(153));
        var providerCharge = new CapacityChargeV1(new(6), new(tenant, sessionId, new CapacitySubjectV1.Provider(providerId)),
            2, CapacityPurposeId.FromValue(Id(154)), new CapacityChargeWindowV1.NoWindow());
        var bytesCharge = new CapacityChargeV1(new(2), new(tenant, sessionId, new CapacitySubjectV1.Operation(operation)),
            400, CapacityPurposeId.FromValue(Id(155)), new CapacityChargeWindowV1.NoWindow());
        var grant = new CapacityGrantSnapshotV1(CapacityGrantId.FromValue(Id(156)), Operation(157),
            ExpectedAuthorityVectorV1.Create(Session(), [new AuthorityAxisValueV1.Graph(Graph())]), Position(50), Position(51),
            new CapacityGrantExpiryV1.At(expiry), CapacityGrantStateV1.Reserved,
            [Balance(providerCharge), Balance(bytesCharge)]);
        var source = copied.Ledger.Owners[controlled.Request.DestinationOwnerId];
        var placeholder = new GraphMediaOpaqueIngressRequest(operation, Hash(1), Id(158),
            controlled.Request.ResidenceId, controlled.Request.DestinationOwnerId, Hash(159), 2, 400, maximumAge,
            admittedAt, controlled.Request, participantCatalog, registration, providerCatalog, provider, grant);
        var proof = GraphMediaResidenceLedger.OpaqueCapacityProofHash(grant);
        var contribution = ProviderContributionV1Codec.ComputeIntegrityHash(provider);
        var request = placeholder with { RequestHash = GraphMediaResidenceLedger.OpaqueHash(placeholder, source, qualification, contribution, proof) };
        return (request, copied.Ledger, visible.Ledger, source, provider);
    }

    private static GraphMediaOpaqueIngressRequest OpaqueRequest(
        (GraphMediaOpaqueIngressRequest Request, GraphMediaOwnershipLedger Ownership,
            GraphMediaResidenceLedger Ledger, GraphMediaOwnerRecord Source, ProviderContributionV1 Provider) fixture, byte seed)
    {
        var operation = Operation(seed); var admitted = fixture.Request.AdmittedAt; var age = fixture.Request.MaximumAge;
        var expiry = new MonotonicStampV1(admitted.ClockDomainId, admitted.BootId, admitted.Nanoseconds + (ulong)age.Nanoseconds);
        var charges = fixture.Request.Grant.Balances.Select(balance => balance.Charge.DimensionId.Value == 6
            ? balance.Charge
            : new CapacityChargeV1(balance.Charge.DimensionId,
                new(balance.Charge.Scope.TenantId, balance.Charge.Scope.SessionId, new CapacitySubjectV1.Operation(operation)),
                balance.Charge.Amount, balance.Charge.Purpose, new CapacityChargeWindowV1.NoWindow())).ToArray();
        var grant = CloneGrant(fixture.Request.Grant, balances: Array.AsReadOnly(charges.Select(Balance).ToArray()));
        var request = fixture.Request with { OperationId = operation, ResidenceId = Id(seed), ExternalReferenceFingerprint = Hash(seed), Grant = grant };
        var qualification = request.SelectedRegistration.OpaqueResidenceQualification!;
        return request with { RequestHash = GraphMediaResidenceLedger.OpaqueHash(request, fixture.Source, qualification,
            ProviderContributionV1Codec.ComputeIntegrityHash(request.SelectedProvider), GraphMediaResidenceLedger.OpaqueCapacityProofHash(grant)) };
    }

    private static CapacityChargeBalanceV1 Balance(CapacityChargeV1 charge) =>
        new(charge, charge.Amount, 0, charge.Amount, 0, 0, 0, 0, 0, 0, charge.Amount, 0);

    private static CapacityGrantSnapshotV1 CloneGrant(CapacityGrantSnapshotV1 grant,
        CapacityGrantStateV1? state = null, IReadOnlyList<CapacityChargeBalanceV1>? balances = null) =>
        new(grant.GrantId, grant.OperationId, grant.Authority, grant.GrantedAt, grant.CurrentFact,
            grant.ExpiresAt, state ?? grant.State, balances ?? grant.Balances);

    private static ProviderContributionV1 ProviderContribution(ProviderId providerId) => new(providerId,
        ProviderFamilyId.FromValue(Id(160)), new("tests"), [ProviderRoleV1.Realtime],
        new ProviderCapabilitySetV1(1, 0, Hash(160)), [], ProviderFactoryId.FromValue(Id(161)),
        ProviderLifetimeV1.SessionScoped, [], Hash(162));

    private static CapacityGrantSnapshotV1 Grant(
        (GraphMediaQuarantineIngressRequest Request, GraphMediaOwnershipLedger Ownership,
            GraphMediaResidenceLedger Ledger, GraphMediaOwnerRecord Source) fixture,
        CapacityChargeV1 charge, ExpectedAuthorityVectorV1? authority = null) =>
        new(fixture.Request.Grant.GrantId, fixture.Request.Grant.OperationId,
            authority ?? fixture.Request.Grant.Authority, fixture.Request.Grant.GrantedAt,
            fixture.Request.Grant.CurrentFact, fixture.Request.Grant.ExpiresAt, CapacityGrantStateV1.Reserved,
            [new CapacityChargeBalanceV1(charge, charge.Amount, 0, charge.Amount, 0, 0, 0, 0, 0, 0, charge.Amount, 0)]);

    private static GraphMediaQuarantineIngressRequest WithGrant(GraphMediaQuarantineIngressRequest request,
        CapacityGrantSnapshotV1 grant) => request with { Grant = grant, RequestHash = Hash(203) };

    private static GraphMediaQuarantineIngressRequest QuarantineRequest(
        (GraphMediaQuarantineIngressRequest Request, GraphMediaOwnershipLedger Ownership,
            GraphMediaResidenceLedger Ledger, GraphMediaOwnerRecord Source) fixture, byte identity)
    {
        var request = fixture.Request with { OperationId = Operation(identity), ResidenceId = Id(identity) };
        var charge = request.Grant.Balances[0].Charge;
        var proof = GraphMediaResidenceLedger.QuarantineCapacityProofHash(request.Grant);
        return request with { RequestHash = GraphMediaResidenceLedger.QuarantineHash(request, fixture.Source, charge, proof) };
    }

    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateNone((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x) => x;
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateArmSamples((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x) => Rehash(x, GraphMediaRepresentationArm.ResidentSamples, x.Request.Evidence.PreGrantPlan.Request.Charges[1]);
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateArmTimedBuffer((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x) => Rehash(x, GraphMediaRepresentationArm.ResidentTimedBuffer, x.Request.Evidence.PreGrantPlan.Request.Charges[2]);

    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) Rehash((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x, GraphMediaRepresentationArm arm, CapacityChargeV1 charge)
    { var request=x.Request with{Arm=arm};var assignment=new GraphMediaCapacityAssignment(charge,arm);request=request with{RequestHash=GraphMediaResidenceLedger.ResidenceHash(request,x.Source,assignment)};return(request,x.Ownership,x.Ledger,assignment,x.Source); }

    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
    private static GraphGenerationId Graph() => GraphGenerationId.FromValue(Id(3));
    private static JournalPositionV1 Position(long sequence) => new(Session(), sequence);
    private static MonotonicStampV1 Stamp(byte value) => new(ClockDomainId.FromValue(Id(4)), BootId.FromValue(Id(5)), value);
    private static StableId128 Id(byte value) { var bytes=new byte[16];bytes[^1]=value;return StableId128.FromBytes(bytes); }
    private static OperationId Operation(byte value) => OperationId.FromValue(Id(value));
    private static Hash256 Hash(byte value) { var bytes=new byte[32];bytes[^1]=value;return Hash256.FromBytes(bytes); }
    private static CapacityChargeV1 Charge(ushort dimension,long amount,byte purpose,CapacityScopeV1 scope)=>new(new(dimension),scope,amount,CapacityPurposeId.FromValue(Id(purpose)),new CapacityChargeWindowV1.NoWindow());
    private static AuthorityFactEnvelopeV1 Envelope(JournalPositionV1 position,byte[] payload,AuthorityPayloadRegistrationV1? registration=null)
    { registration??=GraphParticipantReservationPayloadRegistrationsV2.ReservationCommand;var correlation=new CorrelationEnvelopeV1(TenantId.FromValue(Id(60)),sessionId:SessionId.FromValue(Id(61)),operationId:Operation(15));return new(JournalFactId.Create(),position,null,OwnerSliceId.S1,registration.Schema,payload,AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,payload),correlation,new UtcInstant(1),new UtcInstant(1),new IntegrityEnvelopeV1(1,1,Hash(62),[])); }

    // Each mutator changes one authenticated join or one ledger precondition; the helpers route it through the documented API.
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateBindingCommandPosition((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)=>x with { Request=x.Request with{BindingResult=new GraphParticipantBindingResult.Bound(Position(2),x.Request.BindingResult.FactPosition,x.Request.BindingResult.ExactCanonicalFactBytes,x.Request.BindingResult.Binding,x.Request.BindingResult.CapacityGrantProof)} };

    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateBindingFactPosition((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var b=x.Request.BindingResult;return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,Position(9),b.ExactCanonicalFactBytes,b.Binding,b.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateBindingFactBytes((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var b=x.Request.BindingResult;return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,b.FactPosition,new byte[]{8},b.Binding,b.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateBindingValue((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var b=x.Request.BindingResult;var changed=new GraphParticipantBindingV1(ParticipantId.FromValue(Id(70)),b.Binding.ParticipantFactoryKey,b.Binding.OrderedTopologyNodeKeys);return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,b.FactPosition,b.ExactCanonicalFactBytes,changed,b.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateBindingProof((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var b=x.Request.BindingResult;var p=b.CapacityGrantProof;var changed=new CapacityGrantBindingProofV1(CapacityGrantId.FromValue(Id(71)),p.GrantedAt,p.CurrentFact,p.RequiredChargeCount,p.RequiredChargeCoverageHash);return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,b.FactPosition,b.ExactCanonicalFactBytes,b.Binding,changed)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateReservationCommandPosition((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var f=x.Request.FoldBound;var r=f.Reservation;var changed=new GraphParticipantReservationFoldV2.AppliedReservation(Envelope(Position(12),r.Command.PayloadMemory.ToArray()),r.Fact,r.Reservation);return x with{Request=x.Request with{FoldBound=new(changed,f.Command,f.Fact,f.Binding,f.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateReservationFactPosition((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var f=x.Request.FoldBound;var r=f.Reservation;var changed=new GraphParticipantReservationFoldV2.AppliedReservation(r.Command,Envelope(Position(13),r.Fact.PayloadMemory.ToArray()),r.Reservation);return x with{Request=x.Request with{FoldBound=new(changed,f.Command,f.Fact,f.Binding,f.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateOperation((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var f=x.Request.FoldBound;Assert.True(GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(f.Command.PayloadMemory,out var outer));var body=new GraphParticipantBindingCommandBodyV1(Operation(72),f.Reservation.Fact.Position,null,x.Request.Evidence.PreGrantPlan.GraphGeneration,Session().RuntimeGenerationId,x.Request.Evidence.PreGrantPlan.ParticipantPlanFingerprint,x.Request.Evidence.TopologyFingerprint,x.Request.Evidence.ExecutableFingerprint,f.CapacityGrantProof,Stamp(40));var payload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(Session(),outer!.ExpectedAuthority,GraphParticipantBindingCodecsV1.Encode(body)));var changed=Envelope(f.Command.Position,payload,GraphParticipantBindingPayloadRegistrationsV1.BindingCommand);return x with{Request=x.Request with{FoldBound=new(f.Reservation,changed,f.Fact,f.Binding,f.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateParticipant((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)=>MutateBindingValue(x);
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateFactory((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var b=x.Request.BindingResult;var changed=new GraphParticipantBindingV1(b.Binding.ParticipantId,new("changed"),b.Binding.OrderedTopologyNodeKeys);return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,b.FactPosition,b.ExactCanonicalFactBytes,changed,b.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateNodes((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var b=x.Request.BindingResult;var changed=new GraphParticipantBindingV1(b.Binding.ParticipantId,b.Binding.ParticipantFactoryKey,[new("other")]);return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,b.FactPosition,b.ExactCanonicalFactBytes,changed,b.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateGraph((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var p=x.Request.Evidence.PreGrantPlan;var changed=new GraphParticipantPreGrantPlan(p.ParticipantId,p.OperationId,p.ReservationCommandPosition,p.ReservationFactPosition,GraphGenerationId.FromValue(Id(73)),p.ParticipantPlanFingerprint,p.FactoryKey,p.AllocationCarrier,p.AllocationFingerprint,p.OrderedNodeKeys,p.CapacityRequestCanonicalBytes,p.CapacityRequestFingerprint,p.Request);return x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,changed)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateSession((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var key=new GraphMediaOwnerKey(new(RuntimeGenerationId.FromValue(Id(74)),Session().LiveSessionId),Graph(),x.Source.Key.MediaId);var changed=GraphMediaOwnershipLedger.Create(key,x.Request.SourceOwnerId,x.Source.Media);return x with{Ownership=changed,Source=changed.Owners[x.Request.SourceOwnerId]}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateGrantId((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)=>x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,grant:CapacityGrantId.FromValue(Id(75)))}};
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateGrantedAt((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)=>x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,granted:Position(35))}};
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateCurrentFact((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)=>x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,current:Position(36))}};
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateChargeCount((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var b=x.Request.BindingResult;var p=b.CapacityGrantProof;var changed=new CapacityGrantBindingProofV1(p.GrantId,p.GrantedAt,p.CurrentFact,2,p.RequiredChargeCoverageHash);return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,b.FactPosition,b.ExactCanonicalFactBytes,b.Binding,changed)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateCoverage((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)=>x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,coverage:Hash(76))}};
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateTopology((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)=>x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,topology:Hash(77))}};
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateExecutable((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)=>x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,executable:Hash(78))}};
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateDestinationNode((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)=>x with{Request=x.Request with{DestinationNodeKey=new("missing")}};

    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateChargeMissing((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var charges=x.Request.Evidence.PreGrantPlan.Request.Charges.Skip(1).ToArray();var topology=new GraphTopologyPlan(Session(),Graph(),x.Request.Evidence.GrantId,[new(new("node"))],[],[new(4),new(5)]);var executable=Compile(topology,charges);var changed=Evidence(x.Request.Evidence,topology:topology.Fingerprint,executable:executable.Fingerprint,topologyPlan:topology,executablePlan:executable);return x with{Request=x.Request with{Evidence=changed}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateChargeScope((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var p=x.Request.Evidence.PreGrantPlan;var charges=p.Request.Charges.ToArray();charges[0]=new(charges[0].DimensionId,new(TenantId.FromValue(Id(79)),SessionId.FromValue(Id(80)),new CapacitySubjectV1.Participant(ParticipantId.FromValue(Id(81)))),charges[0].Amount,charges[0].Purpose,charges[0].Window);var request=new CapacityRequestV1(p.OperationId,p.Request.Authority,charges,p.Request.Deadline,p.Request.Priority);var changed=Plan(p,request);return x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,changed)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateChargeAmount((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var p=x.Request.Evidence.PreGrantPlan;var charges=p.Request.Charges.ToArray();var c=charges[0];charges[0]=new(c.DimensionId,c.Scope,c.Amount+1,c.Purpose,c.Window);var changed=Plan(p,new CapacityRequestV1(p.OperationId,p.Request.Authority,charges,p.Request.Deadline,p.Request.Priority));return x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,changed)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateExactRetry((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var prepared=x.Ledger.PrepareControlled(x.Request,x.Ownership);Assert.Equal(GraphMediaResidenceResult.Prepared,prepared.Result);return x with{Ledger=prepared.Ledger}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateContradictoryRetry((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var prepared=x.Ledger.PrepareControlled(x.Request,x.Ownership);Assert.Equal(GraphMediaResidenceResult.Prepared,prepared.Result);return x with{Ledger=prepared.Ledger,Request=x.Request with{RequestHash=Hash(82)}}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateAssignmentConflict((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var prepared=x.Ledger.PrepareControlled(x.Request,x.Ownership);Assert.Equal(GraphMediaResidenceResult.Prepared,prepared.Result);var changed=Retarget(x,Operation(83),Id(84),Id(85));return changed with{Ledger=prepared.Ledger}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateResidenceLimit((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var ledger=x.Ledger;for(byte i=1;i<=64;i++){var variant=Variant(x,i);var added=ledger.PrepareControlled(variant.Request,variant.Ownership);Assert.Equal(GraphMediaResidenceResult.Prepared,added.Result);ledger=added.Ledger;}var changed=Variant(x,65);return changed with{Ledger=ledger}; }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) MutateUnknownReconcile((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x)
    { var prepared=x.Ledger.PrepareControlled(x.Request,x.Ownership);Assert.Equal(GraphMediaResidenceResult.Prepared,prepared.Result);var lost=prepared.Ledger.LoseOutcome(x.Request.OperationId,x.Request.RequestHash);Assert.Equal(GraphMediaResidenceResult.OutcomeUnknown,lost.Result);return x with{Ledger=lost.Ledger}; }

    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) Retarget((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x,OperationId operation,StableId128 residence,StableId128 destination)
    { var p=x.Request.Evidence.PreGrantPlan;var capacity=new CapacityRequestV1(operation,p.Request.Authority,p.Request.Charges,p.Request.Deadline,p.Request.Priority);var plan=new GraphParticipantPreGrantPlan(p.ParticipantId,operation,p.ReservationCommandPosition,p.ReservationFactPosition,p.GraphGeneration,p.ParticipantPlanFingerprint,p.FactoryKey,p.AllocationCarrier,p.AllocationFingerprint,p.OrderedNodeKeys,p.CapacityRequestCanonicalBytes,p.CapacityRequestFingerprint,capacity);var evidence=Evidence(x.Request.Evidence,plan);var f=x.Request.FoldBound;Assert.True(GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(f.Command.PayloadMemory,out var outer));var body=new GraphParticipantBindingCommandBodyV1(operation,f.Reservation.Fact.Position,null,plan.GraphGeneration,Session().RuntimeGenerationId,plan.ParticipantPlanFingerprint,evidence.TopologyFingerprint,evidence.ExecutableFingerprint,f.CapacityGrantProof,Stamp(40));var payload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(Session(),outer!.ExpectedAuthority,GraphParticipantBindingCodecsV1.Encode(body)));var command=Envelope(f.Command.Position,payload,GraphParticipantBindingPayloadRegistrationsV1.BindingCommand);var fold=new GraphParticipantBindingFoldQueryResultV2.Bound(f.Reservation,command,f.Fact,f.Binding,f.CapacityGrantProof);var request=x.Request with{OperationId=operation,ResidenceId=residence,DestinationOwnerId=destination,Evidence=evidence,FoldBound=fold};request=request with{RequestHash=GraphMediaResidenceLedger.ResidenceHash(request,x.Source,x.Assignment)};return(request,x.Ownership,x.Ledger,x.Assignment,x.Source); }
    private static (GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) Variant((GraphMediaControlledResidenceRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger, GraphMediaCapacityAssignment Assignment, GraphMediaOwnerRecord Source) x,byte seed)
    { var changed=Retarget(x,Operation((byte)(100+seed)),Id((byte)(100+seed)),Id((byte)(180+seed)));var p=changed.Request.Evidence.PreGrantPlan;var charges=p.Request.Charges.ToArray();var c=charges[0];charges[0]=new(c.DimensionId,c.Scope,c.Amount,CapacityPurposeId.FromValue(Id((byte)(100+seed))),c.Window);var capacity=new CapacityRequestV1(p.OperationId,p.Request.Authority,charges,p.Request.Deadline,p.Request.Priority);var plan=Plan(p,capacity);var topology=changed.Request.Evidence.Topology;var executable=Compile(topology,charges);var evidence=Evidence(changed.Request.Evidence,plan,executable:executable.Fingerprint,executablePlan:executable);var assignment=new GraphMediaCapacityAssignment(charges[0],GraphMediaRepresentationArm.ResidentBytes);var request=changed.Request with{Evidence=evidence};request=request with{RequestHash=GraphMediaResidenceLedger.ResidenceHash(request,changed.Source,assignment)};return(request,changed.Ownership,changed.Ledger,assignment,changed.Source); }
    private static GraphParticipantPreGrantPlan Plan(GraphParticipantPreGrantPlan p,CapacityRequestV1 request)=>new(p.ParticipantId,p.OperationId,p.ReservationCommandPosition,p.ReservationFactPosition,p.GraphGeneration,p.ParticipantPlanFingerprint,p.FactoryKey,p.AllocationCarrier,p.AllocationFingerprint,p.OrderedNodeKeys,p.CapacityRequestCanonicalBytes,p.CapacityRequestFingerprint,request);
    private static GraphRuntimeExecutablePlan Compile(GraphTopologyPlan topology,IEnumerable<CapacityChargeV1> charges){var catalog=Assert.IsType<GraphRuntimeExecutableCatalogResult.Created>(GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest([new(new("node"),"tests:node@1",1)]));return Assert.IsType<GraphRuntimeExecutableCompileResult.Compiled>(GraphRuntimeExecutablePlan.Compile(topology,topology.Fingerprint,catalog,charges)).Plan;}

    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateCopy((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)
    { var prepared=x.Ledger.PrepareFanout(x.Request,x.Ownership);Assert.Equal(GraphMediaFanoutResult.Prepared,prepared.Result);return x with{Ledger=prepared.ResidenceLedger}; }
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateTransfer((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)
    { var source=x.Ownership.Owners[x.Request.SourceOwnerId];var request=x.Request with{Mode=GraphMediaFanoutMode.TransferSingleDestination};request=request with{RequestHash=GraphMediaResidenceLedger.FanoutHash(request,source)};var prepared=x.Ledger.PrepareFanout(request,x.Ownership);Assert.Equal(GraphMediaFanoutResult.Prepared,prepared.Result);return(request,x.Ownership,prepared.ResidenceLedger); }
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateDestinationOrder((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)
    { var changed=FanoutDestinations(x,2);return changed with{Request=changed.Request with{Destinations=changed.Request.Destinations.Reverse().ToArray()}}; }
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateDestinationDuplicate((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)
    { var changed=FanoutDestinations(x,2);var rows=changed.Request.Destinations.ToArray();rows[1]=rows[1] with{DestinationOwnerId=rows[0].DestinationOwnerId};return changed with{Request=changed.Request with{Destinations=rows}}; }
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateResidenceAuthority((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)
    { var row=x.Request.Destinations[0];var b=row.Residence.BindingResult;var bad=new GraphParticipantBindingResult.Bound(b.CommandPosition,b.FactPosition,b.ExactCanonicalFactBytes,new(ParticipantId.FromValue(Id(90)),b.Binding.ParticipantFactoryKey,b.Binding.OrderedTopologyNodeKeys),b.CapacityGrantProof);var residence=row.Residence with{BindingResult=bad};var changed=row with{Residence=residence};return x with{Request=x.Request with{Destinations=[changed]}}; }
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateCapacityOverlap((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)
    { var row=x.Request.Destinations[0];var destination=Id(91);var variant=Retarget(CreateControlledFixture(),Operation(91),Id(92),destination);var rows=new[]{row,new GraphMediaFanoutDestination(destination,variant.Request.DestinationNodeKey,variant.Request)}.OrderBy(r=>r.DestinationOwnerId.ToString(),StringComparer.Ordinal).ToArray();var request=x.Request with{Destinations=rows};request=request with{RequestHash=GraphMediaResidenceLedger.FanoutHash(request,x.Ownership.Owners[x.Request.SourceOwnerId])};return x with{Request=request}; }
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateOwnerLimit((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)
    { var ownership=x.Ownership;byte seed=100;while(ownership.Owners.Count<GraphMediaOwnershipLedger.MaximumOwners){var take=Math.Min(16,GraphMediaOwnershipLedger.MaximumOwners-ownership.Owners.Count);var ids=Enumerable.Range(0,take).Select(i=>Id(seed++)).OrderBy(v=>v.ToString(),StringComparer.Ordinal).ToArray();var copied=ownership.CopyOwners(Session(),Graph(),x.Request.SourceOwnerId,ids);Assert.Equal(GraphMediaOwnershipBatchCopyResult.Copied,copied.Result);ownership=copied.Ledger;}var prepared=x.Ledger.PrepareFanout(x.Request,ownership);Assert.Equal(GraphMediaFanoutResult.Prepared,prepared.Result);return(x.Request,ownership,prepared.ResidenceLedger); }
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateFanoutResidenceLimit((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)
    { var controlled=CreateControlledFixture();var full=MutateResidenceLimit(controlled);return(x.Request,x.Ownership,full.Ledger); }
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateFanoutExactRetry((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)
    { var prepared=x.Ledger.PrepareFanout(x.Request,x.Ownership);Assert.Equal(GraphMediaFanoutResult.Prepared,prepared.Result);return x with{Ledger=prepared.ResidenceLedger}; }
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateFanoutContradiction((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)
    { var prepared=x.Ledger.PrepareFanout(x.Request,x.Ownership);Assert.Equal(GraphMediaFanoutResult.Prepared,prepared.Result);return x with{Ledger=prepared.ResidenceLedger,Request=x.Request with{RequestHash=Hash(92)}}; }
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutatePrecommitFailure((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)=>MutateCopy(x);
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateCommitDrift((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)
    { var prepared=MutateCopy(x);var destination=prepared.Request.Destinations[0].DestinationOwnerId;var copy=prepared.Ownership.CopyOwners(Session(),Graph(),prepared.Request.SourceOwnerId,[destination]);Assert.Equal(GraphMediaOwnershipBatchCopyResult.Copied,copy.Result);return prepared with{Ownership=copy.Ledger}; }
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateFanoutUnknownReconcile((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)
    { var prepared=x.Ledger.PrepareFanout(x.Request,x.Ownership);Assert.Equal(GraphMediaFanoutResult.Prepared,prepared.Result);var copied=x.Ownership.CopyOwners(Session(),Graph(),x.Request.SourceOwnerId,x.Request.Destinations.Select(d=>d.DestinationOwnerId).ToArray());Assert.Equal(GraphMediaOwnershipBatchCopyResult.Copied,copied.Result);var lost=prepared.ResidenceLedger.LoseFanoutOutcome(x.Request.OperationId,x.Request.RequestHash,copied.Ledger);Assert.Equal(GraphMediaFanoutResult.OutcomeUnknown,lost.Result);return(x.Request,copied.Ledger,lost.ResidenceLedger); }
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateDestinationMax((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)=>FanoutDestinations(x,GraphMediaResidenceLedger.MaximumDestinations);
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) MutateDestinationMaxPlusOne((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x)=>FanoutDestinations(x,GraphMediaResidenceLedger.MaximumDestinations+1);
    private static (GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) FanoutDestinations((GraphMediaFanoutRequest Request, GraphMediaOwnershipLedger Ownership, GraphMediaResidenceLedger Ledger) x,int count)
    { var controlled=CreateControlledFixture();var rows=new List<GraphMediaFanoutDestination>(count);for(byte i=1;i<=count;i++){var variant=Variant(controlled,i);rows.Add(new(variant.Request.DestinationOwnerId,variant.Request.DestinationNodeKey,variant.Request));}rows.Sort((a,b)=>StringComparer.Ordinal.Compare(a.DestinationOwnerId.ToString(),b.DestinationOwnerId.ToString()));var request=x.Request with{Destinations=rows};request=request with{RequestHash=GraphMediaResidenceLedger.FanoutHash(request,x.Ownership.Owners[x.Request.SourceOwnerId])};return(request,x.Ownership,x.Ledger); }

    private static GraphParticipantBindingPlanEvidence Evidence(GraphParticipantBindingPlanEvidence e,GraphParticipantPreGrantPlan? plan=null,CapacityGrantId? grant=null,JournalPositionV1? granted=null,JournalPositionV1? current=null,Hash256? coverage=null,Hash256? topology=null,Hash256? executable=null,GraphTopologyPlan? topologyPlan=null,GraphRuntimeExecutablePlan? executablePlan=null)=>new(plan??e.PreGrantPlan,grant??e.GrantId,granted??e.GrantedAt,current??e.CurrentFact,e.ExpiresAt,e.CanonicalProjection,coverage??e.CoverageHashV2,topologyPlan??e.Topology,executablePlan??e.ExecutablePlan,topology??e.TopologyFingerprint,executable??e.ExecutableFingerprint);
}
