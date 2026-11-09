using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.Graph.Runtime;
namespace HPD.Agent.Authority;

internal sealed class GraphParticipantCapacityPlanEvidenceProvider
{
    private readonly IAuthorityJournalV1 _journal;
    private readonly SessionAuthorityStampV1 _session;
    private readonly SemaphoreSlim _mutex = new(1,1);
    private readonly Dictionary<(ParticipantId,OperationId),GraphParticipantBindingPlanEvidence> _entries = [];
    private ulong _retained;

    internal GraphParticipantCapacityPlanEvidenceProvider(IAuthorityJournalV1 capacityJournal, SessionAuthorityStampV1 capacitySession)
    { _journal=capacityJournal??throw new ArgumentNullException(nameof(capacityJournal));if(!capacitySession.IsValid)throw new ArgumentException("A valid S2 session is required.",nameof(capacitySession));_session=capacitySession; }

    internal ValueTask<AttachResult> AttachAsync(GraphParticipantPreGrantPlan plan, CapacityGrantId grantId, JournalPositionV1 throughPosition, GraphTopologyPlan topology, GraphRuntimeExecutableCatalogResult executableCatalog, CancellationToken cancellationToken=default)
        => AttachCoreAsync(plan, grantId, throughPosition, topology, executableCatalog, cancellationToken);

    private async ValueTask<AttachResult> AttachCoreAsync(GraphParticipantPreGrantPlan plan, CapacityGrantId grantId, JournalPositionV1 throughPosition, GraphTopologyPlan topology, GraphRuntimeExecutableCatalogResult executableCatalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(topology);ArgumentNullException.ThrowIfNull(executableCatalog);if(!grantId.IsValid||!throughPosition.IsValid||throughPosition.Session!=_session)throw new ArgumentException("Invalid historical grant request.");
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var key=(plan.ParticipantId,plan.OperationId);
            if(_entries.TryGetValue(key,out var prior))return SameAttach(prior,plan,grantId,throughPosition,topology,executableCatalog)?new AttachResult.AlreadyAttached(prior):new AttachResult.Contradiction(new BoundedAscii("retained-evidence-contradiction"));
            cancellationToken.ThrowIfCancellationRequested();CapacityGrantSnapshotAtResultV1 read;
            try{read=await CapacityGrantSnapshotReaderV1.ReadAtAsync(_journal,_session,grantId,throughPosition,cancellationToken).ConfigureAwait(false);}
            catch(OperationCanceledException) when (!cancellationToken.IsCancellationRequested){return new AttachResult.StoreUnavailable(new BoundedAscii("capacity-grant-read-unavailable"));}
            catch(Exception){return new AttachResult.StoreUnavailable(new BoundedAscii("capacity-grant-read-unavailable"));}
            if(read is CapacityGrantSnapshotAtResultV1.OutcomeUnknown unknown)return unknown.SafeCode.ToString() is "capacity-history-read-failed" or "capacity-history-snapshot-drift"?new AttachResult.StoreUnavailable(new BoundedAscii("capacity-grant-read-unavailable")):new AttachResult.Quarantined(new BoundedAscii("capacity-history-invalid"));
            var grant=((CapacityGrantSnapshotAtResultV1.Exact)read).Grant;
            if(!GraphParticipantCapacityPlanCompiler.GrantMatches(plan,grant,_session,throughPosition,out var projection,out var coverage))return new AttachResult.Quarantined(new BoundedAscii("capacity-grant-invalid"));
            if(topology.Session!=plan.Request.Authority.Session||topology.Session.RuntimeGenerationId!=plan.ReservationCommandPosition.Session.RuntimeGenerationId||topology.GraphGeneration!=plan.GraphGeneration||topology.CapacityGrantId!=grantId||!topology.CapacityDimensions.SequenceEqual(plan.Request.Charges.Select(x=>x.DimensionId))||!topology.Nodes.Select(x=>x.Key).Order().SequenceEqual(plan.OrderedNodeKeys.Order()))return new AttachResult.Quarantined(new BoundedAscii("binding-plan-invalid"));
            if(GraphRuntimeExecutablePlan.Compile(topology,topology.Fingerprint,executableCatalog,plan.Request.Charges) is not GraphRuntimeExecutableCompileResult.Compiled compiled)return new AttachResult.Quarantined(new BoundedAscii("binding-plan-invalid"));
            _ = nameof(GraphParticipantBindingPlanEvidence.TopologyFingerprint);_ = nameof(GraphParticipantBindingPlanEvidence.ExecutableFingerprint);
            var next=checked(_retained+(ulong)(plan.AllocationCarrier.Length+plan.CapacityRequestCanonicalBytes.Length+projection.Length)+1_900_000UL);if(_entries.Count==32||next>67_108_864)return new AttachResult.Quarantined(new BoundedAscii("evidence-lifetime-exhausted"));
            var evidence=new GraphParticipantBindingPlanEvidence(plan,grantId,grant.GrantedAt,grant.CurrentFact,grant.ExpiresAt,projection,coverage,topology,compiled.Plan,topology.Fingerprint,compiled.Plan.Fingerprint);_entries.Add(key,evidence);_retained=next;return new AttachResult.Attached(evidence);
        }
        finally{_mutex.Release();}
    }

    private static bool SameAttach(GraphParticipantBindingPlanEvidence prior,GraphParticipantPreGrantPlan plan,CapacityGrantId grantId,JournalPositionV1 throughPosition,GraphTopologyPlan topology,GraphRuntimeExecutableCatalogResult executableCatalog)
    {
        var retained=prior.PreGrantPlan;
        if(prior.GrantId!=grantId||prior.CurrentFact!=throughPosition||retained.ParticipantId!=plan.ParticipantId||retained.OperationId!=plan.OperationId||retained.ReservationCommandPosition!=plan.ReservationCommandPosition||retained.ReservationFactPosition!=plan.ReservationFactPosition||retained.GraphGeneration!=plan.GraphGeneration||retained.ParticipantPlanFingerprint!=plan.ParticipantPlanFingerprint||retained.FactoryKey!=plan.FactoryKey||retained.AllocationFingerprint!=plan.AllocationFingerprint||retained.CapacityRequestFingerprint!=plan.CapacityRequestFingerprint||!retained.AllocationCarrier.AsSpan().SequenceEqual(plan.AllocationCarrier)||!retained.CapacityRequestCanonicalBytes.AsSpan().SequenceEqual(plan.CapacityRequestCanonicalBytes)||!retained.OrderedNodeKeys.SequenceEqual(plan.OrderedNodeKeys)||retained.Request.OperationId!=plan.Request.OperationId||retained.Request.Authority!=plan.Request.Authority||retained.Request.Deadline!=plan.Request.Deadline||retained.Request.Priority!=plan.Request.Priority||!retained.Request.Charges.SequenceEqual(plan.Request.Charges)||prior.TopologyFingerprint!=topology.Fingerprint)return false;
        return executableCatalog is GraphRuntimeExecutableCatalogResult.Created created&&created.Catalog.Fingerprint==prior.ExecutablePlan.CatalogFingerprint;
    }

    internal abstract record AttachResult
    {
        private AttachResult() { }
        internal sealed record Attached : AttachResult
        {
            internal Attached(GraphParticipantBindingPlanEvidence evidence) { Evidence=evidence??throw new ArgumentNullException(nameof(evidence)); }
            internal GraphParticipantBindingPlanEvidence Evidence { get; }
        }
        internal sealed record AlreadyAttached : AttachResult
        {
            internal AlreadyAttached(GraphParticipantBindingPlanEvidence evidence) { Evidence=evidence??throw new ArgumentNullException(nameof(evidence)); }
            internal GraphParticipantBindingPlanEvidence Evidence { get; }
        }
        internal sealed record StoreUnavailable : AttachResult
        {
            internal StoreUnavailable(BoundedAscii safeCode) { if(!safeCode.IsValid)throw new ArgumentException("A valid safe code is required.",nameof(safeCode));SafeCode=safeCode; }
            internal BoundedAscii SafeCode { get; }
        }
        internal sealed record Quarantined : AttachResult
        {
            internal Quarantined(BoundedAscii safeCode) { if(!safeCode.IsValid)throw new ArgumentException("A valid safe code is required.",nameof(safeCode));SafeCode=safeCode; }
            internal BoundedAscii SafeCode { get; }
        }
        internal sealed record Contradiction : AttachResult
        {
            internal Contradiction(BoundedAscii safeCode) { if(!safeCode.IsValid)throw new ArgumentException("A valid safe code is required.",nameof(safeCode));SafeCode=safeCode; }
            internal BoundedAscii SafeCode { get; }
        }
    }
}
