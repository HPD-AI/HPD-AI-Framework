using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.Graph.Runtime;
namespace HPD.Agent.Authority;

internal sealed class GraphParticipantCapacityPlanEvidenceProviderV2
{
    private readonly IAuthorityJournalV1 _journal;
    private readonly SessionAuthorityStampV1 _session;
    private readonly SemaphoreSlim _mutex = new(1,1);
    private readonly Dictionary<(ParticipantId,OperationId),GraphParticipantBindingPlanEvidenceV2> _entries = [];
    private ulong _retained;

    internal GraphParticipantCapacityPlanEvidenceProviderV2(IAuthorityJournalV1 capacityJournal, SessionAuthorityStampV1 capacitySession)
    { _journal=capacityJournal??throw new ArgumentNullException(nameof(capacityJournal));if(!capacitySession.IsValid)throw new ArgumentException("A valid S2 session is required.",nameof(capacitySession));_session=capacitySession; }

    internal ValueTask<AttachResultV2> AttachAsync(GraphParticipantPreGrantPlanV2 plan, CapacityGrantId grantId, JournalPositionV1 throughPosition, GraphTopologyPlanV1 topology, GraphRuntimeExecutableCatalogResultV1 executableCatalog, CancellationToken cancellationToken=default)
        => AttachCoreAsync(plan, grantId, throughPosition, topology, executableCatalog, cancellationToken);

    private async ValueTask<AttachResultV2> AttachCoreAsync(GraphParticipantPreGrantPlanV2 plan, CapacityGrantId grantId, JournalPositionV1 throughPosition, GraphTopologyPlanV1 topology, GraphRuntimeExecutableCatalogResultV1 executableCatalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(topology);ArgumentNullException.ThrowIfNull(executableCatalog);if(!grantId.IsValid||!throughPosition.IsValid||throughPosition.Session!=_session)throw new ArgumentException("Invalid historical grant request.");
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var key=(plan.ParticipantId,plan.OperationId);
            if(_entries.TryGetValue(key,out var prior))return SameAttach(prior,plan,grantId,throughPosition,topology,executableCatalog)?new AttachResultV2.AlreadyAttached(prior):new AttachResultV2.Contradiction(new BoundedAscii("retained-evidence-contradiction"));
            cancellationToken.ThrowIfCancellationRequested();CapacityGrantSnapshotAtResultV1 read;
            try{read=await CapacityGrantSnapshotReaderV1.ReadAtAsync(_journal,_session,grantId,throughPosition,cancellationToken).ConfigureAwait(false);}
            catch(OperationCanceledException) when (!cancellationToken.IsCancellationRequested){return new AttachResultV2.StoreUnavailable(new BoundedAscii("capacity-grant-read-unavailable"));}
            catch(Exception){return new AttachResultV2.StoreUnavailable(new BoundedAscii("capacity-grant-read-unavailable"));}
            if(read is CapacityGrantSnapshotAtResultV1.OutcomeUnknown unknown)return unknown.SafeCode.ToString() is "capacity-history-read-failed" or "capacity-history-snapshot-drift"?new AttachResultV2.StoreUnavailable(new BoundedAscii("capacity-grant-read-unavailable")):new AttachResultV2.Quarantined(new BoundedAscii("capacity-history-invalid"));
            var grant=((CapacityGrantSnapshotAtResultV1.Exact)read).Grant;
            if(!GraphParticipantCapacityPlanCompilerV2.GrantMatches(plan,grant,_session,throughPosition,out var projection,out var coverage))return new AttachResultV2.Quarantined(new BoundedAscii("capacity-grant-invalid"));
            if(topology.Session!=plan.Request.Authority.Session||topology.Session.RuntimeGenerationId!=plan.ReservationCommandPosition.Session.RuntimeGenerationId||topology.GraphGeneration!=plan.GraphGeneration||topology.CapacityGrantId!=grantId||!topology.CapacityDimensions.SequenceEqual(plan.Request.Charges.Select(x=>x.DimensionId))||!topology.Nodes.Select(x=>x.Key).Order().SequenceEqual(plan.OrderedNodeKeys.Order()))return new AttachResultV2.Quarantined(new BoundedAscii("binding-plan-invalid"));
            if(GraphRuntimeExecutablePlanV1.Compile(topology,topology.Fingerprint,executableCatalog,plan.Request.Charges) is not GraphRuntimeExecutableCompileResultV1.Compiled compiled)return new AttachResultV2.Quarantined(new BoundedAscii("binding-plan-invalid"));
            _ = nameof(GraphParticipantBindingPlanEvidenceV2.TopologyFingerprint);_ = nameof(GraphParticipantBindingPlanEvidenceV2.ExecutableFingerprint);
            var next=checked(_retained+(ulong)(plan.AllocationCarrier.Length+plan.CapacityRequestCanonicalBytes.Length+projection.Length)+1_900_000UL);if(_entries.Count==32||next>67_108_864)return new AttachResultV2.Quarantined(new BoundedAscii("evidence-lifetime-exhausted"));
            var evidence=new GraphParticipantBindingPlanEvidenceV2(plan,grantId,grant.GrantedAt,grant.CurrentFact,grant.ExpiresAt,projection,coverage,topology,compiled.Plan,topology.Fingerprint,compiled.Plan.Fingerprint);_entries.Add(key,evidence);_retained=next;return new AttachResultV2.Attached(evidence);
        }
        finally{_mutex.Release();}
    }

    private static bool SameAttach(GraphParticipantBindingPlanEvidenceV2 prior,GraphParticipantPreGrantPlanV2 plan,CapacityGrantId grantId,JournalPositionV1 throughPosition,GraphTopologyPlanV1 topology,GraphRuntimeExecutableCatalogResultV1 executableCatalog)
    {
        var retained=prior.PreGrantPlan;
        if(prior.GrantId!=grantId||prior.CurrentFact!=throughPosition||retained.ParticipantId!=plan.ParticipantId||retained.OperationId!=plan.OperationId||retained.ReservationCommandPosition!=plan.ReservationCommandPosition||retained.ReservationFactPosition!=plan.ReservationFactPosition||retained.GraphGeneration!=plan.GraphGeneration||retained.ParticipantPlanFingerprint!=plan.ParticipantPlanFingerprint||retained.FactoryKey!=plan.FactoryKey||retained.AllocationFingerprint!=plan.AllocationFingerprint||retained.CapacityRequestFingerprint!=plan.CapacityRequestFingerprint||!retained.AllocationCarrier.AsSpan().SequenceEqual(plan.AllocationCarrier)||!retained.CapacityRequestCanonicalBytes.AsSpan().SequenceEqual(plan.CapacityRequestCanonicalBytes)||!retained.OrderedNodeKeys.SequenceEqual(plan.OrderedNodeKeys)||retained.Request.OperationId!=plan.Request.OperationId||retained.Request.Authority!=plan.Request.Authority||retained.Request.Deadline!=plan.Request.Deadline||retained.Request.Priority!=plan.Request.Priority||!retained.Request.Charges.SequenceEqual(plan.Request.Charges)||prior.TopologyFingerprint!=topology.Fingerprint)return false;
        return executableCatalog is GraphRuntimeExecutableCatalogResultV1.Created created&&created.Catalog.Fingerprint==prior.ExecutablePlan.CatalogFingerprint;
    }

    internal abstract record AttachResultV2
    {
        private AttachResultV2() { }
        internal sealed record Attached : AttachResultV2
        {
            internal Attached(GraphParticipantBindingPlanEvidenceV2 evidence) { Evidence=evidence??throw new ArgumentNullException(nameof(evidence)); }
            internal GraphParticipantBindingPlanEvidenceV2 Evidence { get; }
        }
        internal sealed record AlreadyAttached : AttachResultV2
        {
            internal AlreadyAttached(GraphParticipantBindingPlanEvidenceV2 evidence) { Evidence=evidence??throw new ArgumentNullException(nameof(evidence)); }
            internal GraphParticipantBindingPlanEvidenceV2 Evidence { get; }
        }
        internal sealed record StoreUnavailable : AttachResultV2
        {
            internal StoreUnavailable(BoundedAscii safeCode) { if(!safeCode.IsValid)throw new ArgumentException("A valid safe code is required.",nameof(safeCode));SafeCode=safeCode; }
            internal BoundedAscii SafeCode { get; }
        }
        internal sealed record Quarantined : AttachResultV2
        {
            internal Quarantined(BoundedAscii safeCode) { if(!safeCode.IsValid)throw new ArgumentException("A valid safe code is required.",nameof(safeCode));SafeCode=safeCode; }
            internal BoundedAscii SafeCode { get; }
        }
        internal sealed record Contradiction : AttachResultV2
        {
            internal Contradiction(BoundedAscii safeCode) { if(!safeCode.IsValid)throw new ArgumentException("A valid safe code is required.",nameof(safeCode));SafeCode=safeCode; }
            internal BoundedAscii SafeCode { get; }
        }
    }
}
