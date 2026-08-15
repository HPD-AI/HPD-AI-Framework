using HPD.Agent.Audio;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.Graph.Runtime;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaResidenceV1Tests
{
    [Fact]
    public void Derived_copy_plans_without_mutation_then_commits_new_media_owner_and_residence_atomically()
    {
        var fixture = CreateControlledFixture();
        Assert.True(GraphMediaBindingV1.TryCreate(0, 1_000, Id(210), 1, 16_000, 1, 2,
            Id(211), 1, 0, GraphMediaDiscontinuityKindV1.ResetBefore, 400, 200, null, out var media));
        var destinationKey = new GraphMediaOwnerKeyV1(Session(), Graph(), Id(212));
        var destination = new GraphMediaOwnerRecordV1(fixture.Request.DestinationOwnerId,
            destinationKey, media!, GraphMediaOwnerStateV1.Owned, 1);
        var authority = fixture.Request with
        {
            RequestHash = GraphMediaResidenceLedgerV1.DerivedCopyHash(
                fixture.Request, destination, fixture.Assignment, fixture.Source.Version),
        };
        var request = new GraphMediaDerivedResidenceRequestV1(authority, fixture.Source.Version,
            destinationKey, media!);
        var residenceBefore = fixture.Ledger.Fingerprint;
        var ownershipBefore = fixture.Ownership.Fingerprint;

        var planned = fixture.Ledger.PlanDerivedCopy(request, fixture.Ownership);

        Assert.Equal(GraphMediaDerivedCopyResultV1.Planned, planned.Result);
        Assert.Same(fixture.Ledger, planned.ResidenceLedger);
        Assert.Same(fixture.Ownership, planned.OwnershipLedger);
        Assert.Equal(residenceBefore, planned.ResidenceLedger.Fingerprint);
        Assert.Equal(ownershipBefore, planned.OwnershipLedger.Fingerprint);

        var committed = fixture.Ledger.CommitDerivedCopy(planned.Plan!, fixture.Ownership);
        Assert.Equal(GraphMediaDerivedCopyResultV1.Committed, committed.Result);
        Assert.NotEqual(residenceBefore, committed.ResidenceLedger.Fingerprint);
        Assert.NotEqual(ownershipBefore, committed.OwnershipLedger.Fingerprint);
        Assert.Equal(media, committed.OwnershipLedger.Owners[authority.DestinationOwnerId].Media);
        Assert.Equal(GraphMediaResidenceStateV1.Visible, committed.Residence!.State);
        Assert.Equal(media, committed.Residence.Media);
        Assert.Equal(GraphMediaDerivedCopyResultV1.IdempotentCommitted,
            committed.ResidenceLedger.PlanDerivedCopy(request, committed.OwnershipLedger).Result);
        var changedVersion = request with { ExpectedSourceVersion = request.ExpectedSourceVersion + 1 };
        Assert.Equal(GraphMediaDerivedCopyResultV1.ContradictoryDuplicate,
            committed.ResidenceLedger.PlanDerivedCopy(changedVersion, committed.OwnershipLedger).Result);
    }

    [Fact]
    public void Derived_copy_discard_and_active_borrow_leave_both_ledgers_byte_identical()
    {
        var fixture = CreateControlledFixture();
        Assert.True(GraphMediaBindingV1.TryCreate(0, 1_000, Id(220), 1, 16_000, 1, 2,
            Id(221), 1, 0, GraphMediaDiscontinuityKindV1.ResetBefore, 400, 200, null, out var media));
        var destinationKey = new GraphMediaOwnerKeyV1(Session(), Graph(), Id(222));
        var destination = new GraphMediaOwnerRecordV1(fixture.Request.DestinationOwnerId,
            destinationKey, media!, GraphMediaOwnerStateV1.Owned, 1);
        var authority = fixture.Request with
        {
            RequestHash = GraphMediaResidenceLedgerV1.DerivedCopyHash(
                fixture.Request, destination, fixture.Assignment, fixture.Source.Version),
        };
        var request = new GraphMediaDerivedResidenceRequestV1(authority, fixture.Source.Version,
            destinationKey, media!);
        var planned = fixture.Ledger.PlanDerivedCopy(request, fixture.Ownership);
        Assert.Equal(GraphMediaDerivedCopyResultV1.Planned, planned.Result);
        Assert.Empty(fixture.Ledger.Residences);
        Assert.Single(fixture.Ownership.Owners);

        var borrowed = fixture.Ownership.Acquire(Session(), Graph(), fixture.Source.OwnerId, Id(223), Hash(224));
        Assert.Equal(GraphMediaBorrowResultV1.Borrowed, borrowed.Result);
        var residenceBefore = fixture.Ledger.Fingerprint;
        var ownershipBefore = borrowed.Ledger.Fingerprint;
        var rejected = fixture.Ledger.PlanDerivedCopy(request, borrowed.Ledger);
        Assert.Equal(GraphMediaDerivedCopyResultV1.BorrowOutstanding, rejected.Result);
        Assert.Equal(residenceBefore, rejected.ResidenceLedger.Fingerprint);
        Assert.Equal(ownershipBefore, rejected.OwnershipLedger.Fingerprint);
    }

    [Fact]
    public void Explicit_unknown_ingress_requires_exact_schema_capacity_and_is_never_publishable()
    {
        var fixture = CreateQuarantineFixture(); var before = fixture.Ledger.Fingerprint;
        var transition = fixture.Ledger.Quarantine(fixture.Request, fixture.Ownership);
        Assert.Equal(GraphMediaResidenceResultV1.Quarantined, transition.Result);
        Assert.NotEqual(before, transition.Ledger.Fingerprint);
        var row = Assert.Single(transition.Ledger.Quarantines).Value;
        Assert.Equal(GraphMediaResidenceClassV1.Quarantine, row.Class);
        Assert.Equal(GraphMediaResidenceStateV1.Quarantined, row.State);
        Assert.Equal((ushort)12, row.Charge.DimensionId.Value);
        Assert.Equal(CapacityScopeKindV1.Schema, row.Charge.Scope.Kind);
        Assert.Equal(fixture.Source.Media.ByteLength, row.Charge.Amount);
        Assert.Equal(GraphMediaResidenceResultV1.IdempotentQuarantined,
            transition.Ledger.Quarantine(fixture.Request, fixture.Ownership).Result);
        Assert.Equal(GraphMediaResidenceResultV1.WrongState,
            transition.Ledger.MakeVisible(fixture.Request.OperationId, fixture.Request.RequestHash, fixture.Ownership).Result);
    }

    [Fact]
    public void Quarantine_wrong_source_scope_amount_authority_and_duplicate_fail_before_mutation()
    {
        var fixture = CreateQuarantineFixture();
        var cases = new List<(GraphMediaQuarantineIngressRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership,
            GraphMediaResidenceLedgerV1 Ledger, GraphMediaResidenceResultV1 Expected)>();
        cases.Add((fixture.Request with { SourceResidenceId = Id(199) }, fixture.Ownership, fixture.Ledger,
            GraphMediaResidenceResultV1.WrongState));
        var wrongSchemaCharge = new CapacityChargeV1(new(12), new(TenantId.FromValue(Id(17)),
            SessionId.FromValue(Id(18)), new CapacitySubjectV1.Schema(SchemaId.FromValue(Id(16)))),
            fixture.Source.Media.ByteLength, CapacityPurposeId.FromValue(Id(19)), new CapacityChargeWindowV1.NoWindow());
        cases.Add((WithGrant(fixture.Request, Grant(fixture, wrongSchemaCharge)), fixture.Ownership, fixture.Ledger,
            GraphMediaResidenceResultV1.CapacityMismatch));
        var schemaCharge = fixture.Request.Grant.Balances[0].Charge;
        var wrongAmount = new CapacityChargeV1(schemaCharge.DimensionId, schemaCharge.Scope, schemaCharge.Amount + 1,
            schemaCharge.Purpose, schemaCharge.Window);
        cases.Add((WithGrant(fixture.Request, Grant(fixture, wrongAmount)), fixture.Ownership, fixture.Ledger,
            GraphMediaResidenceResultV1.CapacityMismatch));
        var staleAuthority = ExpectedAuthorityVectorV1.Create(new(RuntimeGenerationId.FromValue(Id(201)),
            fixture.Ownership.Session.LiveSessionId), [new AuthorityAxisValueV1.Graph(Graph())]);
        cases.Add((WithGrant(fixture.Request, Grant(fixture, schemaCharge, staleAuthority)), fixture.Ownership,
            fixture.Ledger, GraphMediaResidenceResultV1.StaleGeneration));
        foreach (var item in cases)
        {
            var before = item.Ledger.Fingerprint; var result = item.Ledger.Quarantine(item.Request, item.Ownership);
            Assert.Equal(item.Expected, result.Result); Assert.Same(item.Ledger, result.Ledger);
            Assert.Equal(before, result.Ledger.Fingerprint);
        }
        var accepted = fixture.Ledger.Quarantine(fixture.Request, fixture.Ownership);
        var changed = fixture.Request with { RequestHash = Hash(202) };
        Assert.Equal(GraphMediaResidenceResultV1.ContradictoryDuplicate,
            accepted.Ledger.Quarantine(changed, fixture.Ownership).Result);
    }

    [Fact]
    public void Quarantine_has_an_exact_sixteen_item_ceiling()
    {
        var fixture = CreateQuarantineFixture();
        var ledger = fixture.Ledger;
        for (byte index = 0; index < GraphMediaResidenceLedgerV1.MaximumQuarantine; index++)
        {
            var request = QuarantineRequest(fixture, (byte)(110 + index));
            var transition = ledger.Quarantine(request, fixture.Ownership);
            Assert.Equal(GraphMediaResidenceResultV1.Quarantined, transition.Result);
            ledger = transition.Ledger;
        }
        Assert.Equal(GraphMediaResidenceLedgerV1.MaximumQuarantine, ledger.Quarantines.Count);
        var before = ledger.Fingerprint;
        var overflow = ledger.Quarantine(QuarantineRequest(fixture, 140), fixture.Ownership);
        Assert.Equal(GraphMediaResidenceResultV1.ResidenceLimitReached, overflow.Result);
        Assert.Same(ledger, overflow.Ledger);
        Assert.Equal(before, overflow.Ledger.Fingerprint);
    }

    [Fact]
    public void Qualified_opaque_ingress_retains_finite_credits_without_claiming_physical_size()
    {
        var fixture = CreateOpaqueFixture(); var before = fixture.Ledger.Fingerprint;
        var accepted = fixture.Ledger.AdmitOpaque(fixture.Request, fixture.Ownership);
        Assert.Equal(GraphMediaResidenceResultV1.OpaqueAdmitted, accepted.Result);
        Assert.NotEqual(before, accepted.Ledger.Fingerprint);
        var row = Assert.Single(accepted.Ledger.Opaques).Value;
        Assert.Equal(GraphMediaResidenceClassV1.Opaque, row.Class);
        Assert.Equal((ushort)2, row.SubmittedOperations); Assert.Equal(400UL, row.SubmittedBytes);
        Assert.Equal(1_000L, row.MaximumAge.Nanoseconds);
        Assert.Equal(fixture.Provider.ProviderId, row.ProviderId);
        Assert.Null(typeof(GraphMediaOpaqueResidenceV1).GetProperty("ByteLength"));
        Assert.Equal(GraphMediaResidenceResultV1.IdempotentOpaqueAdmitted,
            accepted.Ledger.AdmitOpaque(fixture.Request, fixture.Ownership).Result);
        Assert.Equal(GraphMediaResidenceResultV1.WrongState,
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
            Assert.Contains(result.Result, new[] { GraphMediaResidenceResultV1.AuthorityMismatch, GraphMediaResidenceResultV1.CapacityMismatch });
            Assert.Same(fixture.Ledger, result.Ledger); Assert.Equal(before, result.Ledger.Fingerprint);
        }
        var accepted = fixture.Ledger.AdmitOpaque(fixture.Request, fixture.Ownership);
        Assert.Equal(GraphMediaResidenceResultV1.ContradictoryDuplicate,
            accepted.Ledger.AdmitOpaque(fixture.Request with { RequestHash = Hash(166) }, fixture.Ownership).Result);
    }

    [Fact]
    public void Opaque_has_an_exact_sixteen_item_ceiling()
    {
        var fixture = CreateOpaqueFixture(); var ledger = fixture.Ledger;
        for (byte index = 0; index < GraphMediaResidenceLedgerV1.MaximumOpaque; index++)
        {
            var request = OpaqueRequest(fixture, (byte)(170 + index));
            var accepted = ledger.AdmitOpaque(request, fixture.Ownership);
            Assert.Equal(GraphMediaResidenceResultV1.OpaqueAdmitted, accepted.Result); ledger = accepted.Ledger;
        }
        Assert.Equal(GraphMediaResidenceLedgerV1.MaximumOpaque, ledger.Opaques.Count);
        var overflow = ledger.AdmitOpaque(OpaqueRequest(fixture, 190), fixture.Ownership);
        Assert.Equal(GraphMediaResidenceResultV1.ResidenceLimitReached, overflow.Result);
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
        Assert.Equal(GraphMediaResidenceResultV1.Prepared, first.Result);
        var collision = Retarget(fixture, Operation(93), fixture.Request.ResidenceId, Id(94));
        var rejected = first.Ledger.PrepareControlled(collision.Request, collision.Ownership);
        Assert.Equal(GraphMediaResidenceResultV1.InvalidRequest, rejected.Result);
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
        Assert.Equal(GraphMediaFanoutResultV1.Committed, committed.Result);
        Assert.Equal(GraphMediaFanoutResultV1.WrongState,
            committed.ResidenceLedger.CommitFanout(fixture.Request.OperationId, fixture.Request.RequestHash, fixture.Ownership).Result);
        Assert.Equal(GraphMediaFanoutResultV1.WrongState,
            committed.ResidenceLedger.PrepareFanout(fixture.Request, fixture.Ownership).Result);

        var controlled = CreateControlledFixture();
        var foreignSession = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(95)), LiveSessionId.FromValue(Id(96)));
        var foreignOwnership = GraphMediaOwnershipLedgerV1.Create(
            new GraphMediaOwnerKeyV1(foreignSession, Graph(), Id(14)), controlled.Request.SourceOwnerId, controlled.Source.Media);
        Assert.Equal(GraphMediaFanoutResultV1.WrongState,
            prepared.ResidenceLedger.LoseFanoutOutcome(fixture.Request.OperationId, fixture.Request.RequestHash, foreignOwnership).Result);
        Assert.Equal(GraphMediaFanoutResultV1.WrongState,
            prepared.ResidenceLedger.FailFanout(fixture.Request.OperationId, fixture.Request.RequestHash, foreignOwnership).Result);

        var two = FanoutDestinations(CreateFanoutFixture(), 2);
        var preparedTwo = two.Ledger.PrepareFanout(two.Request, two.Ownership);
        var partial = two.Ownership.CopyOwners(Session(), Graph(), two.Request.SourceOwnerId,
            [two.Request.Destinations[0].DestinationOwnerId]);
        Assert.Equal(GraphMediaOwnershipBatchCopyResultV1.Copied, partial.Result);
        Assert.Equal(GraphMediaFanoutResultV1.WrongState,
            preparedTwo.ResidenceLedger.LoseFanoutOutcome(two.Request.OperationId, two.Request.RequestHash, partial.Ledger).Result);
        Assert.Equal(GraphMediaFanoutResultV1.WrongState,
            preparedTwo.ResidenceLedger.FailFanout(two.Request.OperationId, two.Request.RequestHash, partial.Ledger).Result);

        var unknown = MutateFanoutUnknownReconcile(CreateFanoutFixture());
        var reconciled = unknown.Ledger.ReconcileFanout(unknown.Request.OperationId, unknown.Request.RequestHash, unknown.Ownership);
        Assert.Equal(GraphMediaFanoutResultV1.Reconciled, reconciled.Result);
        Assert.All(unknown.Request.Destinations, destination =>
            Assert.Equal(GraphMediaResidenceStateV1.Visible,
                reconciled.ResidenceLedger.Residences[destination.Residence.ResidenceId].State));
    }

    [Fact]
    public void Surface_and_forbidden_effect_inventory_is_exact()
    {
        Assert.False(typeof(GraphMediaResidenceLedgerV1).IsPublic);
        Assert.False(typeof(GraphMediaControlledResidenceRequestV1).IsPublic);
        Assert.Equal(64, GraphMediaResidenceLedgerV1.MaximumControlled);
        Assert.Equal(16, GraphMediaResidenceLedgerV1.MaximumDestinations);
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

    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) CreateControlledFixture()
    {
        var session = Session(); var graph = Graph(); var sourceId = Id(10); var destinationId = Id(11);
        Assert.True(GraphMediaBindingV1.TryCreate(0, 1_000, Id(12), 1, 48_000, 2, 2, Id(13), 1, 0, GraphMediaDiscontinuityKindV1.ResetBefore, 400, 100, null, out var media));
        var ownership = GraphMediaOwnershipLedgerV1.Create(new(session, graph, Id(14)), sourceId, media!);
        var source = ownership.Owners[sourceId]; var operation = Operation(15); var participant = ParticipantId.FromValue(Id(16));
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(graph)]);
        var scope = new CapacityScopeV1(TenantId.FromValue(Id(17)), SessionId.FromValue(Id(18)), new CapacitySubjectV1.Participant(participant));
        var charges = new[] { Charge(1, 400, 19, scope), Charge(4, 200, 20, scope), Charge(5, 1_000, 21, scope) };
        var request = new CapacityRequestV1(operation, authority, charges, Stamp(30), CapacityPriorityV1.Normal);
        var reservationCommand = Position(1); var reservationFact = Position(2);
        var preGrant = new GraphParticipantPreGrantPlanV2(participant, operation, reservationCommand, reservationFact, graph, Hash(22), new("factory"), [1], Hash(23), [new("node")], [2], Hash(24), request);
        var grantId = CapacityGrantId.FromValue(Id(25));
        var topology = new GraphTopologyPlanV1(session, graph, grantId, [new(new("node"))], [], [new(1), new(4), new(5)]);
        var catalog = Assert.IsType<GraphRuntimeExecutableCatalogResultV1.Created>(GraphRuntimeExecutableFactoryCatalogV1.FromGeneratedApplicationManifest([new(new("node"), "tests:node@1", 1)]));
        var executable = Assert.IsType<GraphRuntimeExecutableCompileResultV1.Compiled>(GraphRuntimeExecutablePlanV1.Compile(topology, topology.Fingerprint, catalog, charges)).Plan;
        var evidence = new GraphParticipantBindingPlanEvidenceV2(preGrant, grantId, Position(30), Position(31), new CapacityGrantExpiryV1.NoExpiry(), [3], Hash(26), topology, executable, topology.Fingerprint, executable.Fingerprint);
        var reservation = new GraphParticipantReservationV1(participant, new("factory"), [new("node")]);
        var reservationApplied = new GraphParticipantReservationFoldV2.AppliedReservation(Envelope(reservationCommand, [1]), Envelope(reservationFact, [2]), reservation);
        var proof = new CapacityGrantBindingProofV1(grantId, evidence.GrantedAt, evidence.CurrentFact, 3, evidence.CoverageHashV2);
        var binding = new GraphParticipantBindingV1(participant, new("factory"), [new("node")]);
        var bindingCommandBody = new GraphParticipantBindingCommandBodyV1(operation, reservationFact, null, graph, session.RuntimeGenerationId, preGrant.ParticipantPlanFingerprint, evidence.TopologyFingerprint, evidence.ExecutableFingerprint, proof, Stamp(40));
        var bindingCommandPayload = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(session, authority, GraphParticipantBindingCodecsV1.Encode(bindingCommandBody)));
        var bindingCommand = Envelope(Position(3), bindingCommandPayload, GraphParticipantBindingPayloadRegistrationsV1.BindingCommand);
        var bindingFact = Envelope(Position(4), [9], GraphParticipantBindingPayloadRegistrationsV1.BindingFact);
        var bound = new GraphParticipantBindingResultV2.Bound(bindingCommand.Position, bindingFact.Position, bindingFact.PayloadMemory, binding, proof);
        var foldBound = new GraphParticipantBindingFoldQueryResultV2.Bound(reservationApplied, bindingCommand, bindingFact, binding, proof);
        var controlled = new GraphMediaControlledResidenceRequestV1(operation, Hash(27), Id(28), sourceId, destinationId, new("node"), GraphMediaRepresentationArmV1.ResidentBytes, bound, foldBound, evidence);
        var assignment = new GraphMediaCapacityAssignmentV1(charges[0], controlled.Arm);
        controlled = controlled with { RequestHash = GraphMediaResidenceLedgerV1.ResidenceHash(controlled, source, assignment) };
        return (controlled, ownership, GraphMediaResidenceLedgerV1.Create(session, graph), assignment, source);
    }

    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) CreateFanoutFixture()
    {
        var controlled = CreateControlledFixture();
        var destination = new GraphMediaFanoutDestinationV1(controlled.Request.DestinationOwnerId, controlled.Request.DestinationNodeKey, controlled.Request);
        var request = new GraphMediaFanoutRequestV1(Operation(50), Hash(51), controlled.Request.SourceOwnerId, 1, GraphMediaFanoutModeV1.Copy, [destination]);
        request = request with { RequestHash = GraphMediaResidenceLedgerV1.FanoutHash(request, controlled.Source) };
        return (request, controlled.Ownership, controlled.Ledger);
    }

    private static (GraphMediaQuarantineIngressRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership,
        GraphMediaResidenceLedgerV1 Ledger, GraphMediaOwnerRecordV1 Source) CreateQuarantineFixture()
    {
        var controlled = CreateControlledFixture();
        var copied = controlled.Ownership.CopyOwners(Session(), Graph(), controlled.Request.SourceOwnerId,
            [controlled.Request.DestinationOwnerId]);
        Assert.Equal(GraphMediaOwnershipBatchCopyResultV1.Copied, copied.Result);
        var prepared = controlled.Ledger.PrepareControlled(controlled.Request, copied.Ledger);
        Assert.Equal(GraphMediaResidenceResultV1.Prepared, prepared.Result);
        var unknown = prepared.Ledger.LoseOutcome(controlled.Request.OperationId, controlled.Request.RequestHash);
        Assert.Equal(GraphMediaResidenceResultV1.OutcomeUnknown, unknown.Result);
        var source = copied.Ledger.Owners[controlled.Request.DestinationOwnerId];
        var schema = SchemaId.FromValue(Id(90));
        var charge = new CapacityChargeV1(new(12), new(TenantId.FromValue(Id(17)), SessionId.FromValue(Id(18)),
            new CapacitySubjectV1.Schema(schema)), source.Media.ByteLength, CapacityPurposeId.FromValue(Id(91)),
            new CapacityChargeWindowV1.NoWindow());
        var placeholder = new GraphMediaQuarantineIngressRequestV1(Operation(92), Hash(1), Id(93),
            controlled.Request.ResidenceId, source.OwnerId, schema,
            new CapacityGrantSnapshotV1(CapacityGrantId.FromValue(Id(94)), Operation(95),
                ExpectedAuthorityVectorV1.Create(Session(), [new AuthorityAxisValueV1.Graph(Graph())]),
                Position(40), Position(41), new CapacityGrantExpiryV1.NoExpiry(), CapacityGrantStateV1.Reserved,
                [new CapacityChargeBalanceV1(charge, charge.Amount, 0, charge.Amount, 0, 0, 0, 0, 0, 0, charge.Amount, 0)]));
        var proof = GraphMediaResidenceLedgerV1.QuarantineCapacityProofHash(placeholder.Grant);
        var request = placeholder with { RequestHash = GraphMediaResidenceLedgerV1.QuarantineHash(placeholder, source, charge, proof) };
        return (request, copied.Ledger, unknown.Ledger, source);
    }

    private static (GraphMediaOpaqueIngressRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership,
        GraphMediaResidenceLedgerV1 Ledger, GraphMediaOwnerRecordV1 Source, ProviderContributionV1 Provider) CreateOpaqueFixture()
    {
        var controlled = CreateControlledFixture();
        var copied = controlled.Ownership.CopyOwners(Session(), Graph(), controlled.Request.SourceOwnerId,
            [controlled.Request.DestinationOwnerId]);
        Assert.Equal(GraphMediaOwnershipBatchCopyResultV1.Copied, copied.Result);
        var prepared = controlled.Ledger.PrepareControlled(controlled.Request, copied.Ledger);
        Assert.Equal(GraphMediaResidenceResultV1.Prepared, prepared.Result);
        var visible = prepared.Ledger.MakeVisible(controlled.Request.OperationId, controlled.Request.RequestHash, copied.Ledger);
        Assert.Equal(GraphMediaResidenceResultV1.Visible, visible.Result);
        var providerId = ProviderId.FromValue(Id(150)); var provider = ProviderContribution(providerId);
        var qualification = new LiveAudioOpaqueResidenceQualificationV1(providerId, 8, 4_194_304,
            new DurationNs(10_000_000_000), LiveAudioOpaqueResidenceControlV1.ObservationOnly);
        var descriptor = new LiveAudioParticipantDescriptorV1(new("factory"), OwnerSliceId.S2,
            AuthorityAxisId.Graph, [], [new CapacityDimensionId(1)], new(1), new(1), new(1));
        var registration = new LiveAudioParticipantFactoryRegistrationV1(typeof(GraphMediaResidenceV1Tests),
            "tests:opaque", descriptor, ReadOnlyMemory<byte>.Empty, null, qualification);
        var participantCatalog = LiveAudioParticipantCatalogManifestV1.Create([registration]);
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
        var placeholder = new GraphMediaOpaqueIngressRequestV1(operation, Hash(1), Id(158),
            controlled.Request.ResidenceId, controlled.Request.DestinationOwnerId, Hash(159), 2, 400, maximumAge,
            admittedAt, controlled.Request, participantCatalog, registration, providerCatalog, provider, grant);
        var proof = GraphMediaResidenceLedgerV1.OpaqueCapacityProofHash(grant);
        var contribution = ProviderContributionV1Codec.ComputeIntegrityHash(provider);
        var request = placeholder with { RequestHash = GraphMediaResidenceLedgerV1.OpaqueHash(placeholder, source, qualification, contribution, proof) };
        return (request, copied.Ledger, visible.Ledger, source, provider);
    }

    private static GraphMediaOpaqueIngressRequestV1 OpaqueRequest(
        (GraphMediaOpaqueIngressRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership,
            GraphMediaResidenceLedgerV1 Ledger, GraphMediaOwnerRecordV1 Source, ProviderContributionV1 Provider) fixture, byte seed)
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
        return request with { RequestHash = GraphMediaResidenceLedgerV1.OpaqueHash(request, fixture.Source, qualification,
            ProviderContributionV1Codec.ComputeIntegrityHash(request.SelectedProvider), GraphMediaResidenceLedgerV1.OpaqueCapacityProofHash(grant)) };
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
        (GraphMediaQuarantineIngressRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership,
            GraphMediaResidenceLedgerV1 Ledger, GraphMediaOwnerRecordV1 Source) fixture,
        CapacityChargeV1 charge, ExpectedAuthorityVectorV1? authority = null) =>
        new(fixture.Request.Grant.GrantId, fixture.Request.Grant.OperationId,
            authority ?? fixture.Request.Grant.Authority, fixture.Request.Grant.GrantedAt,
            fixture.Request.Grant.CurrentFact, fixture.Request.Grant.ExpiresAt, CapacityGrantStateV1.Reserved,
            [new CapacityChargeBalanceV1(charge, charge.Amount, 0, charge.Amount, 0, 0, 0, 0, 0, 0, charge.Amount, 0)]);

    private static GraphMediaQuarantineIngressRequestV1 WithGrant(GraphMediaQuarantineIngressRequestV1 request,
        CapacityGrantSnapshotV1 grant) => request with { Grant = grant, RequestHash = Hash(203) };

    private static GraphMediaQuarantineIngressRequestV1 QuarantineRequest(
        (GraphMediaQuarantineIngressRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership,
            GraphMediaResidenceLedgerV1 Ledger, GraphMediaOwnerRecordV1 Source) fixture, byte identity)
    {
        var request = fixture.Request with { OperationId = Operation(identity), ResidenceId = Id(identity) };
        var charge = request.Grant.Balances[0].Charge;
        var proof = GraphMediaResidenceLedgerV1.QuarantineCapacityProofHash(request.Grant);
        return request with { RequestHash = GraphMediaResidenceLedgerV1.QuarantineHash(request, fixture.Source, charge, proof) };
    }

    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateNone((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x) => x;
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateArmSamples((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x) => Rehash(x, GraphMediaRepresentationArmV1.ResidentSamples, x.Request.Evidence.PreGrantPlan.Request.Charges[1]);
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateArmTimedBuffer((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x) => Rehash(x, GraphMediaRepresentationArmV1.ResidentTimedBuffer, x.Request.Evidence.PreGrantPlan.Request.Charges[2]);

    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) Rehash((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x, GraphMediaRepresentationArmV1 arm, CapacityChargeV1 charge)
    { var request=x.Request with{Arm=arm};var assignment=new GraphMediaCapacityAssignmentV1(charge,arm);request=request with{RequestHash=GraphMediaResidenceLedgerV1.ResidenceHash(request,x.Source,assignment)};return(request,x.Ownership,x.Ledger,assignment,x.Source); }

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
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateBindingCommandPosition((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)=>x with { Request=x.Request with{BindingResult=new GraphParticipantBindingResultV2.Bound(Position(2),x.Request.BindingResult.FactPosition,x.Request.BindingResult.ExactCanonicalFactBytes,x.Request.BindingResult.Binding,x.Request.BindingResult.CapacityGrantProof)} };

    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateBindingFactPosition((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var b=x.Request.BindingResult;return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,Position(9),b.ExactCanonicalFactBytes,b.Binding,b.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateBindingFactBytes((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var b=x.Request.BindingResult;return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,b.FactPosition,new byte[]{8},b.Binding,b.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateBindingValue((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var b=x.Request.BindingResult;var changed=new GraphParticipantBindingV1(ParticipantId.FromValue(Id(70)),b.Binding.ParticipantFactoryKey,b.Binding.OrderedTopologyNodeKeys);return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,b.FactPosition,b.ExactCanonicalFactBytes,changed,b.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateBindingProof((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var b=x.Request.BindingResult;var p=b.CapacityGrantProof;var changed=new CapacityGrantBindingProofV1(CapacityGrantId.FromValue(Id(71)),p.GrantedAt,p.CurrentFact,p.RequiredChargeCount,p.RequiredChargeCoverageHash);return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,b.FactPosition,b.ExactCanonicalFactBytes,b.Binding,changed)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateReservationCommandPosition((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var f=x.Request.FoldBound;var r=f.Reservation;var changed=new GraphParticipantReservationFoldV2.AppliedReservation(Envelope(Position(12),r.Command.PayloadMemory.ToArray()),r.Fact,r.Reservation);return x with{Request=x.Request with{FoldBound=new(changed,f.Command,f.Fact,f.Binding,f.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateReservationFactPosition((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var f=x.Request.FoldBound;var r=f.Reservation;var changed=new GraphParticipantReservationFoldV2.AppliedReservation(r.Command,Envelope(Position(13),r.Fact.PayloadMemory.ToArray()),r.Reservation);return x with{Request=x.Request with{FoldBound=new(changed,f.Command,f.Fact,f.Binding,f.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateOperation((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var f=x.Request.FoldBound;Assert.True(GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(f.Command.PayloadMemory,out var outer));var body=new GraphParticipantBindingCommandBodyV1(Operation(72),f.Reservation.Fact.Position,null,x.Request.Evidence.PreGrantPlan.GraphGeneration,Session().RuntimeGenerationId,x.Request.Evidence.PreGrantPlan.ParticipantPlanFingerprint,x.Request.Evidence.TopologyFingerprint,x.Request.Evidence.ExecutableFingerprint,f.CapacityGrantProof,Stamp(40));var payload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(Session(),outer!.ExpectedAuthority,GraphParticipantBindingCodecsV1.Encode(body)));var changed=Envelope(f.Command.Position,payload,GraphParticipantBindingPayloadRegistrationsV1.BindingCommand);return x with{Request=x.Request with{FoldBound=new(f.Reservation,changed,f.Fact,f.Binding,f.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateParticipant((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)=>MutateBindingValue(x);
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateFactory((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var b=x.Request.BindingResult;var changed=new GraphParticipantBindingV1(b.Binding.ParticipantId,new("changed"),b.Binding.OrderedTopologyNodeKeys);return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,b.FactPosition,b.ExactCanonicalFactBytes,changed,b.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateNodes((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var b=x.Request.BindingResult;var changed=new GraphParticipantBindingV1(b.Binding.ParticipantId,b.Binding.ParticipantFactoryKey,[new("other")]);return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,b.FactPosition,b.ExactCanonicalFactBytes,changed,b.CapacityGrantProof)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateGraph((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var p=x.Request.Evidence.PreGrantPlan;var changed=new GraphParticipantPreGrantPlanV2(p.ParticipantId,p.OperationId,p.ReservationCommandPosition,p.ReservationFactPosition,GraphGenerationId.FromValue(Id(73)),p.ParticipantPlanFingerprint,p.FactoryKey,p.AllocationCarrier,p.AllocationFingerprint,p.OrderedNodeKeys,p.CapacityRequestCanonicalBytes,p.CapacityRequestFingerprint,p.Request);return x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,changed)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateSession((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var key=new GraphMediaOwnerKeyV1(new(RuntimeGenerationId.FromValue(Id(74)),Session().LiveSessionId),Graph(),x.Source.Key.MediaId);var changed=GraphMediaOwnershipLedgerV1.Create(key,x.Request.SourceOwnerId,x.Source.Media);return x with{Ownership=changed,Source=changed.Owners[x.Request.SourceOwnerId]}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateGrantId((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)=>x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,grant:CapacityGrantId.FromValue(Id(75)))}};
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateGrantedAt((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)=>x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,granted:Position(35))}};
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateCurrentFact((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)=>x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,current:Position(36))}};
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateChargeCount((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var b=x.Request.BindingResult;var p=b.CapacityGrantProof;var changed=new CapacityGrantBindingProofV1(p.GrantId,p.GrantedAt,p.CurrentFact,2,p.RequiredChargeCoverageHash);return x with{Request=x.Request with{BindingResult=new(b.CommandPosition,b.FactPosition,b.ExactCanonicalFactBytes,b.Binding,changed)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateCoverage((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)=>x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,coverage:Hash(76))}};
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateTopology((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)=>x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,topology:Hash(77))}};
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateExecutable((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)=>x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,executable:Hash(78))}};
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateDestinationNode((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)=>x with{Request=x.Request with{DestinationNodeKey=new("missing")}};

    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateChargeMissing((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var charges=x.Request.Evidence.PreGrantPlan.Request.Charges.Skip(1).ToArray();var topology=new GraphTopologyPlanV1(Session(),Graph(),x.Request.Evidence.GrantId,[new(new("node"))],[],[new(4),new(5)]);var executable=Compile(topology,charges);var changed=Evidence(x.Request.Evidence,topology:topology.Fingerprint,executable:executable.Fingerprint,topologyPlan:topology,executablePlan:executable);return x with{Request=x.Request with{Evidence=changed}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateChargeScope((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var p=x.Request.Evidence.PreGrantPlan;var charges=p.Request.Charges.ToArray();charges[0]=new(charges[0].DimensionId,new(TenantId.FromValue(Id(79)),SessionId.FromValue(Id(80)),new CapacitySubjectV1.Participant(ParticipantId.FromValue(Id(81)))),charges[0].Amount,charges[0].Purpose,charges[0].Window);var request=new CapacityRequestV1(p.OperationId,p.Request.Authority,charges,p.Request.Deadline,p.Request.Priority);var changed=Plan(p,request);return x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,changed)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateChargeAmount((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var p=x.Request.Evidence.PreGrantPlan;var charges=p.Request.Charges.ToArray();var c=charges[0];charges[0]=new(c.DimensionId,c.Scope,c.Amount+1,c.Purpose,c.Window);var changed=Plan(p,new CapacityRequestV1(p.OperationId,p.Request.Authority,charges,p.Request.Deadline,p.Request.Priority));return x with{Request=x.Request with{Evidence=Evidence(x.Request.Evidence,changed)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateExactRetry((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var prepared=x.Ledger.PrepareControlled(x.Request,x.Ownership);Assert.Equal(GraphMediaResidenceResultV1.Prepared,prepared.Result);return x with{Ledger=prepared.Ledger}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateContradictoryRetry((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var prepared=x.Ledger.PrepareControlled(x.Request,x.Ownership);Assert.Equal(GraphMediaResidenceResultV1.Prepared,prepared.Result);return x with{Ledger=prepared.Ledger,Request=x.Request with{RequestHash=Hash(82)}}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateAssignmentConflict((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var prepared=x.Ledger.PrepareControlled(x.Request,x.Ownership);Assert.Equal(GraphMediaResidenceResultV1.Prepared,prepared.Result);var changed=Retarget(x,Operation(83),Id(84),Id(85));return changed with{Ledger=prepared.Ledger}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateResidenceLimit((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var ledger=x.Ledger;for(byte i=1;i<=64;i++){var variant=Variant(x,i);var added=ledger.PrepareControlled(variant.Request,variant.Ownership);Assert.Equal(GraphMediaResidenceResultV1.Prepared,added.Result);ledger=added.Ledger;}var changed=Variant(x,65);return changed with{Ledger=ledger}; }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) MutateUnknownReconcile((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x)
    { var prepared=x.Ledger.PrepareControlled(x.Request,x.Ownership);Assert.Equal(GraphMediaResidenceResultV1.Prepared,prepared.Result);var lost=prepared.Ledger.LoseOutcome(x.Request.OperationId,x.Request.RequestHash);Assert.Equal(GraphMediaResidenceResultV1.OutcomeUnknown,lost.Result);return x with{Ledger=lost.Ledger}; }

    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) Retarget((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x,OperationId operation,StableId128 residence,StableId128 destination)
    { var p=x.Request.Evidence.PreGrantPlan;var capacity=new CapacityRequestV1(operation,p.Request.Authority,p.Request.Charges,p.Request.Deadline,p.Request.Priority);var plan=new GraphParticipantPreGrantPlanV2(p.ParticipantId,operation,p.ReservationCommandPosition,p.ReservationFactPosition,p.GraphGeneration,p.ParticipantPlanFingerprint,p.FactoryKey,p.AllocationCarrier,p.AllocationFingerprint,p.OrderedNodeKeys,p.CapacityRequestCanonicalBytes,p.CapacityRequestFingerprint,capacity);var evidence=Evidence(x.Request.Evidence,plan);var f=x.Request.FoldBound;Assert.True(GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(f.Command.PayloadMemory,out var outer));var body=new GraphParticipantBindingCommandBodyV1(operation,f.Reservation.Fact.Position,null,plan.GraphGeneration,Session().RuntimeGenerationId,plan.ParticipantPlanFingerprint,evidence.TopologyFingerprint,evidence.ExecutableFingerprint,f.CapacityGrantProof,Stamp(40));var payload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(Session(),outer!.ExpectedAuthority,GraphParticipantBindingCodecsV1.Encode(body)));var command=Envelope(f.Command.Position,payload,GraphParticipantBindingPayloadRegistrationsV1.BindingCommand);var fold=new GraphParticipantBindingFoldQueryResultV2.Bound(f.Reservation,command,f.Fact,f.Binding,f.CapacityGrantProof);var request=x.Request with{OperationId=operation,ResidenceId=residence,DestinationOwnerId=destination,Evidence=evidence,FoldBound=fold};request=request with{RequestHash=GraphMediaResidenceLedgerV1.ResidenceHash(request,x.Source,x.Assignment)};return(request,x.Ownership,x.Ledger,x.Assignment,x.Source); }
    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) Variant((GraphMediaControlledResidenceRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger, GraphMediaCapacityAssignmentV1 Assignment, GraphMediaOwnerRecordV1 Source) x,byte seed)
    { var changed=Retarget(x,Operation((byte)(100+seed)),Id((byte)(100+seed)),Id((byte)(180+seed)));var p=changed.Request.Evidence.PreGrantPlan;var charges=p.Request.Charges.ToArray();var c=charges[0];charges[0]=new(c.DimensionId,c.Scope,c.Amount,CapacityPurposeId.FromValue(Id((byte)(100+seed))),c.Window);var capacity=new CapacityRequestV1(p.OperationId,p.Request.Authority,charges,p.Request.Deadline,p.Request.Priority);var plan=Plan(p,capacity);var topology=changed.Request.Evidence.Topology;var executable=Compile(topology,charges);var evidence=Evidence(changed.Request.Evidence,plan,executable:executable.Fingerprint,executablePlan:executable);var assignment=new GraphMediaCapacityAssignmentV1(charges[0],GraphMediaRepresentationArmV1.ResidentBytes);var request=changed.Request with{Evidence=evidence};request=request with{RequestHash=GraphMediaResidenceLedgerV1.ResidenceHash(request,changed.Source,assignment)};return(request,changed.Ownership,changed.Ledger,assignment,changed.Source); }
    private static GraphParticipantPreGrantPlanV2 Plan(GraphParticipantPreGrantPlanV2 p,CapacityRequestV1 request)=>new(p.ParticipantId,p.OperationId,p.ReservationCommandPosition,p.ReservationFactPosition,p.GraphGeneration,p.ParticipantPlanFingerprint,p.FactoryKey,p.AllocationCarrier,p.AllocationFingerprint,p.OrderedNodeKeys,p.CapacityRequestCanonicalBytes,p.CapacityRequestFingerprint,request);
    private static GraphRuntimeExecutablePlanV1 Compile(GraphTopologyPlanV1 topology,IEnumerable<CapacityChargeV1> charges){var catalog=Assert.IsType<GraphRuntimeExecutableCatalogResultV1.Created>(GraphRuntimeExecutableFactoryCatalogV1.FromGeneratedApplicationManifest([new(new("node"),"tests:node@1",1)]));return Assert.IsType<GraphRuntimeExecutableCompileResultV1.Compiled>(GraphRuntimeExecutablePlanV1.Compile(topology,topology.Fingerprint,catalog,charges)).Plan;}

    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateCopy((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)
    { var prepared=x.Ledger.PrepareFanout(x.Request,x.Ownership);Assert.Equal(GraphMediaFanoutResultV1.Prepared,prepared.Result);return x with{Ledger=prepared.ResidenceLedger}; }
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateTransfer((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)
    { var source=x.Ownership.Owners[x.Request.SourceOwnerId];var request=x.Request with{Mode=GraphMediaFanoutModeV1.TransferSingleDestination};request=request with{RequestHash=GraphMediaResidenceLedgerV1.FanoutHash(request,source)};var prepared=x.Ledger.PrepareFanout(request,x.Ownership);Assert.Equal(GraphMediaFanoutResultV1.Prepared,prepared.Result);return(request,x.Ownership,prepared.ResidenceLedger); }
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateDestinationOrder((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)
    { var changed=FanoutDestinations(x,2);return changed with{Request=changed.Request with{Destinations=changed.Request.Destinations.Reverse().ToArray()}}; }
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateDestinationDuplicate((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)
    { var changed=FanoutDestinations(x,2);var rows=changed.Request.Destinations.ToArray();rows[1]=rows[1] with{DestinationOwnerId=rows[0].DestinationOwnerId};return changed with{Request=changed.Request with{Destinations=rows}}; }
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateResidenceAuthority((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)
    { var row=x.Request.Destinations[0];var b=row.Residence.BindingResult;var bad=new GraphParticipantBindingResultV2.Bound(b.CommandPosition,b.FactPosition,b.ExactCanonicalFactBytes,new(ParticipantId.FromValue(Id(90)),b.Binding.ParticipantFactoryKey,b.Binding.OrderedTopologyNodeKeys),b.CapacityGrantProof);var residence=row.Residence with{BindingResult=bad};var changed=row with{Residence=residence};return x with{Request=x.Request with{Destinations=[changed]}}; }
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateCapacityOverlap((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)
    { var row=x.Request.Destinations[0];var destination=Id(91);var variant=Retarget(CreateControlledFixture(),Operation(91),Id(92),destination);var rows=new[]{row,new GraphMediaFanoutDestinationV1(destination,variant.Request.DestinationNodeKey,variant.Request)}.OrderBy(r=>r.DestinationOwnerId.ToString(),StringComparer.Ordinal).ToArray();var request=x.Request with{Destinations=rows};request=request with{RequestHash=GraphMediaResidenceLedgerV1.FanoutHash(request,x.Ownership.Owners[x.Request.SourceOwnerId])};return x with{Request=request}; }
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateOwnerLimit((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)
    { var ownership=x.Ownership;byte seed=100;while(ownership.Owners.Count<GraphMediaOwnershipLedgerV1.MaximumOwners){var take=Math.Min(16,GraphMediaOwnershipLedgerV1.MaximumOwners-ownership.Owners.Count);var ids=Enumerable.Range(0,take).Select(i=>Id(seed++)).OrderBy(v=>v.ToString(),StringComparer.Ordinal).ToArray();var copied=ownership.CopyOwners(Session(),Graph(),x.Request.SourceOwnerId,ids);Assert.Equal(GraphMediaOwnershipBatchCopyResultV1.Copied,copied.Result);ownership=copied.Ledger;}var prepared=x.Ledger.PrepareFanout(x.Request,ownership);Assert.Equal(GraphMediaFanoutResultV1.Prepared,prepared.Result);return(x.Request,ownership,prepared.ResidenceLedger); }
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateFanoutResidenceLimit((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)
    { var controlled=CreateControlledFixture();var full=MutateResidenceLimit(controlled);return(x.Request,x.Ownership,full.Ledger); }
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateFanoutExactRetry((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)
    { var prepared=x.Ledger.PrepareFanout(x.Request,x.Ownership);Assert.Equal(GraphMediaFanoutResultV1.Prepared,prepared.Result);return x with{Ledger=prepared.ResidenceLedger}; }
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateFanoutContradiction((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)
    { var prepared=x.Ledger.PrepareFanout(x.Request,x.Ownership);Assert.Equal(GraphMediaFanoutResultV1.Prepared,prepared.Result);return x with{Ledger=prepared.ResidenceLedger,Request=x.Request with{RequestHash=Hash(92)}}; }
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutatePrecommitFailure((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)=>MutateCopy(x);
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateCommitDrift((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)
    { var prepared=MutateCopy(x);var destination=prepared.Request.Destinations[0].DestinationOwnerId;var copy=prepared.Ownership.CopyOwners(Session(),Graph(),prepared.Request.SourceOwnerId,[destination]);Assert.Equal(GraphMediaOwnershipBatchCopyResultV1.Copied,copy.Result);return prepared with{Ownership=copy.Ledger}; }
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateFanoutUnknownReconcile((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)
    { var prepared=x.Ledger.PrepareFanout(x.Request,x.Ownership);Assert.Equal(GraphMediaFanoutResultV1.Prepared,prepared.Result);var copied=x.Ownership.CopyOwners(Session(),Graph(),x.Request.SourceOwnerId,x.Request.Destinations.Select(d=>d.DestinationOwnerId).ToArray());Assert.Equal(GraphMediaOwnershipBatchCopyResultV1.Copied,copied.Result);var lost=prepared.ResidenceLedger.LoseFanoutOutcome(x.Request.OperationId,x.Request.RequestHash,copied.Ledger);Assert.Equal(GraphMediaFanoutResultV1.OutcomeUnknown,lost.Result);return(x.Request,copied.Ledger,lost.ResidenceLedger); }
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateDestinationMax((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)=>FanoutDestinations(x,GraphMediaResidenceLedgerV1.MaximumDestinations);
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) MutateDestinationMaxPlusOne((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x)=>FanoutDestinations(x,GraphMediaResidenceLedgerV1.MaximumDestinations+1);
    private static (GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) FanoutDestinations((GraphMediaFanoutRequestV1 Request, GraphMediaOwnershipLedgerV1 Ownership, GraphMediaResidenceLedgerV1 Ledger) x,int count)
    { var controlled=CreateControlledFixture();var rows=new List<GraphMediaFanoutDestinationV1>(count);for(byte i=1;i<=count;i++){var variant=Variant(controlled,i);rows.Add(new(variant.Request.DestinationOwnerId,variant.Request.DestinationNodeKey,variant.Request));}rows.Sort((a,b)=>StringComparer.Ordinal.Compare(a.DestinationOwnerId.ToString(),b.DestinationOwnerId.ToString()));var request=x.Request with{Destinations=rows};request=request with{RequestHash=GraphMediaResidenceLedgerV1.FanoutHash(request,x.Ownership.Owners[x.Request.SourceOwnerId])};return(request,x.Ownership,x.Ledger); }

    private static GraphParticipantBindingPlanEvidenceV2 Evidence(GraphParticipantBindingPlanEvidenceV2 e,GraphParticipantPreGrantPlanV2? plan=null,CapacityGrantId? grant=null,JournalPositionV1? granted=null,JournalPositionV1? current=null,Hash256? coverage=null,Hash256? topology=null,Hash256? executable=null,GraphTopologyPlanV1? topologyPlan=null,GraphRuntimeExecutablePlanV1? executablePlan=null)=>new(plan??e.PreGrantPlan,grant??e.GrantId,granted??e.GrantedAt,current??e.CurrentFact,e.ExpiresAt,e.CanonicalProjection,coverage??e.CoverageHashV2,topologyPlan??e.Topology,executablePlan??e.ExecutablePlan,topology??e.TopologyFingerprint,executable??e.ExecutableFingerprint);
}
