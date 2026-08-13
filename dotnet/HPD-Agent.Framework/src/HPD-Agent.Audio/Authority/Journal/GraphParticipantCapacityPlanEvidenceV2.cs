using System.Formats.Cbor;
using System.Security.Cryptography;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.Graph.Runtime;

namespace HPD.Agent.Authority;

internal sealed record GraphParticipantPreGrantPlanV2
{
    private readonly byte[] _carrier, _requestBytes;
    internal GraphParticipantPreGrantPlanV2(ParticipantId participantId, OperationId operationId, BoundedAscii factoryKey, byte[] allocationCarrier, Hash256 allocationFingerprint, IReadOnlyList<BoundedAscii> orderedNodeKeys, byte[] capacityRequestCanonicalBytes, Hash256 capacityRequestFingerprint, CapacityRequestV1 request)
    {
        if (!participantId.IsValid || !operationId.IsValid || !factoryKey.IsValid || allocationCarrier is null || allocationCarrier.Length is 0 or > 16384 || allocationFingerprint == default || orderedNodeKeys is null || orderedNodeKeys.Count is < 1 or > 64 || capacityRequestCanonicalBytes is null || capacityRequestCanonicalBytes.Length == 0 || capacityRequestFingerprint == default || request is null) throw new ArgumentException("Invalid pregrant plan.");
        ParticipantId=participantId;OperationId=operationId;FactoryKey=factoryKey;_carrier=allocationCarrier.ToArray();AllocationFingerprint=allocationFingerprint;OrderedNodeKeys=Array.AsReadOnly(orderedNodeKeys.ToArray());_requestBytes=capacityRequestCanonicalBytes.ToArray();CapacityRequestFingerprint=capacityRequestFingerprint;Request=request;
    }
    internal ParticipantId ParticipantId { get; }
    internal OperationId OperationId { get; }
    internal BoundedAscii FactoryKey { get; }
    internal byte[] AllocationCarrier => _carrier.ToArray();
    internal Hash256 AllocationFingerprint { get; }
    internal IReadOnlyList<BoundedAscii> OrderedNodeKeys { get; }
    internal byte[] CapacityRequestCanonicalBytes => _requestBytes.ToArray();
    internal Hash256 CapacityRequestFingerprint { get; }
    internal CapacityRequestV1 Request { get; }
}

internal sealed record GraphParticipantBindingPlanEvidenceV2
{
    private readonly byte[] _projection;
    internal GraphParticipantBindingPlanEvidenceV2(GraphParticipantPreGrantPlanV2 preGrantPlan, CapacityGrantId grantId, JournalPositionV1 grantedAt, JournalPositionV1 currentFact, CapacityGrantExpiryV1 expiresAt, byte[] canonicalProjection, Hash256 coverageHashV2, GraphTopologyPlanV1 topology, GraphRuntimeExecutablePlanV1 executablePlan, Hash256 topologyFingerprint, Hash256 executableFingerprint)
    { PreGrantPlan=preGrantPlan??throw new ArgumentNullException(nameof(preGrantPlan));if(!grantId.IsValid||!grantedAt.IsValid||!currentFact.IsValid||expiresAt is null||canonicalProjection is null||canonicalProjection.Length is 0 or >65536||coverageHashV2==default||topology is null||executablePlan is null||topologyFingerprint==default||executableFingerprint==default)throw new ArgumentException("Invalid binding evidence.");GrantId=grantId;GrantedAt=grantedAt;CurrentFact=currentFact;ExpiresAt=expiresAt;_projection=canonicalProjection.ToArray();CoverageHashV2=coverageHashV2;Topology=topology;ExecutablePlan=executablePlan;TopologyFingerprint=topologyFingerprint;ExecutableFingerprint=executableFingerprint; }
    internal GraphParticipantPreGrantPlanV2 PreGrantPlan { get; }
    internal CapacityGrantId GrantId { get; }
    internal JournalPositionV1 GrantedAt { get; }
    internal JournalPositionV1 CurrentFact { get; }
    internal CapacityGrantExpiryV1 ExpiresAt { get; }
    internal byte[] CanonicalProjection => _projection.ToArray();
    internal Hash256 CoverageHashV2 { get; }
    internal GraphTopologyPlanV1 Topology { get; }
    internal GraphRuntimeExecutablePlanV1 ExecutablePlan { get; }
    internal Hash256 TopologyFingerprint { get; }
    internal Hash256 ExecutableFingerprint { get; }
}

internal abstract record GraphParticipantCapacityPlanBuildResultV2
{
    private GraphParticipantCapacityPlanBuildResultV2() { }
    internal sealed record Found : GraphParticipantCapacityPlanBuildResultV2
    {
        internal Found(GraphParticipantPreGrantPlanV2 plan) { Plan=plan??throw new ArgumentNullException(nameof(plan)); }
        internal GraphParticipantPreGrantPlanV2 Plan { get; }
    }
    internal sealed record Quarantined : GraphParticipantCapacityPlanBuildResultV2
    {
        internal Quarantined(BoundedAscii safeCode) { if(!safeCode.IsValid)throw new ArgumentException("A valid safe code is required.",nameof(safeCode));SafeCode=safeCode; }
        internal BoundedAscii SafeCode { get; }
    }
}

internal static class GraphParticipantCapacityPlanCompilerV2
{
    internal static GraphParticipantCapacityPlanBuildResultV2 BuildCapacityRequest(LiveAudioParticipantCatalogManifestV1 manifest, GraphParticipantReservationResultV1.Applied applied, GraphParticipantReservationFoldV1.AppliedReservation authenticated, MonotonicStampV1 deadline, CapacityPriorityV1 priority)
    {
        ArgumentNullException.ThrowIfNull(manifest);ArgumentNullException.ThrowIfNull(applied);ArgumentNullException.ThrowIfNull(authenticated);
        if (!GraphParticipantBindingCodecsV1.TryDecodeReservationCommand(authenticated.Command.PayloadMemory,out var outer)||outer is null||!GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(outer.BodyBytes.ToArray(),out var body)||body is null||!GraphParticipantBindingCodecsV1.TryDecodeReservationFact(applied.ExactCanonicalFactBytes,out var factOuter)||factOuter is null||!GraphParticipantBindingCodecsV1.TryDecodeReservationFactBody(factOuter.BodyBytes.ToArray(),out var fact)||fact is null||authenticated.Fact.Position!=applied.FactPosition||authenticated.Command.Position!=applied.CommandPosition||!authenticated.Fact.PayloadMemory.Span.SequenceEqual(applied.ExactCanonicalFactBytes.Span)||authenticated.Reservation.ParticipantId!=applied.ParticipantId||fact.OperationId!=body.OperationId||outer.Session!=outer.ExpectedAuthority.Session||authenticated.Command.Correlation.OperationId!=body.OperationId||authenticated.Command.Correlation.SessionId is null)
            return Bad("reservation-evidence-invalid");
        var allocated=manifest.Descriptors.Select(d=>manifest.TryGet(d.FactoryKey,out var r)?r:null).Where(r=>r is not null&&!r.GraphParticipantAllocationDeclarationBytes.IsEmpty).ToArray();
        if(allocated.Length!=1||allocated[0]!.Descriptor.FactoryKey!=authenticated.Reservation.ParticipantFactoryKey)return Bad("allocation-carrier-invalid");
        var registration=allocated[0]!;var carrier=registration.GraphParticipantAllocationDeclarationBytes.ToArray();if(registration.GraphParticipantAllocationDeclarationFingerprint is not Hash256 allocationFingerprint||Hash("hpd-graph-participant-allocation-declaration-v1\0"u8,carrier)!=allocationFingerprint||!Decode(carrier,out var embeddedFactory,out var nodes,out var templates)||embeddedFactory!=registration.Descriptor.FactoryKey.ToString()||embeddedFactory!=authenticated.Reservation.ParticipantFactoryKey.ToString()||!nodes.SequenceEqual(authenticated.Reservation.OrderedTopologyNodeKeys.Select(x=>x.ToString())))return Bad("allocation-carrier-invalid");
        if(templates.Count is <1 or >3||templates.Any(x=>x.Dimension is not(1 or 4 or 5)||x.Policy!=1))return Bad("plan-dimension-incompatible");
        try
        {
            var scope=new CapacityScopeV1(authenticated.Command.Correlation.TenantId,authenticated.Command.Correlation.SessionId,new CapacitySubjectV1.Participant(applied.ParticipantId));
            var charges=templates.Select(x=>new CapacityChargeV1(new CapacityDimensionId(x.Dimension),scope,checked((long)x.Amount),CapacityPurposeId.FromValue(StableId128.FromBytes(x.Purpose)),new CapacityChargeWindowV1.NoWindow())).ToArray();
            var request=new CapacityRequestV1(body.OperationId,outer.ExpectedAuthority,charges,deadline,priority);var bytes=EncodeRequest(request);var hash=Hash("hpd-graph-participant-capacity-request-v2\0"u8,bytes);
            return new GraphParticipantCapacityPlanBuildResultV2.Found(new(applied.ParticipantId,body.OperationId,authenticated.Reservation.ParticipantFactoryKey,carrier,allocationFingerprint,authenticated.Reservation.OrderedTopologyNodeKeys,bytes,hash,request));
        }catch(Exception e) when(e is ArgumentException or OverflowException){return Bad("request-invalid");}
    }

    internal static bool GrantMatches(GraphParticipantPreGrantPlanV2 plan, CapacityGrantSnapshotV1 grant, SessionAuthorityStampV1 capacitySession, JournalPositionV1 throughPosition, out byte[] canonicalProjection, out Hash256 coverageHashV2)
    {
        canonicalProjection=[];coverageHashV2=default;if(plan is null||grant is null||!capacitySession.IsValid||!throughPosition.IsValid||throughPosition.Session!=capacitySession||grant.GrantId!=CapacityGrantIdDerivationV1.Derive(plan.OperationId)||grant.OperationId!=plan.OperationId||grant.Authority!=plan.Request.Authority||grant.CurrentFact!=throughPosition||grant.GrantedAt.Session!=capacitySession||grant.GrantedAt.Sequence>grant.CurrentFact.Sequence||grant.State!=CapacityGrantStateV1.Reserved||grant.ExpiresAt is CapacityGrantExpiryV1.At at&&at.Value.CompareTo(plan.Request.Deadline)!=ClockComparison.Later||grant.Balances.Count!=plan.Request.Charges.Count)return false;
        for(var i=0;i<grant.Balances.Count;i++){var b=grant.Balances[i];try{if(b.Charge!=plan.Request.Charges[i]||b.NormalAllocation<0||b.ReserveAllocation<0||checked(b.NormalAllocation+b.ReserveAllocation)!=b.Charge.Amount||b.Unactivated<0||b.Active<0||b.Released<0||b.Consumed<0||b.AgedOut<0||b.Revoked<0||b.ExplicitlyUnknown<0||b.EncumberedNormal<0||b.EncumberedReserve<0||b.Unactivated!=b.Charge.Amount||b.Active!=0||b.Released!=0||b.Consumed!=0||b.AgedOut!=0||b.Revoked!=0||b.ExplicitlyUnknown!=0||b.EncumberedNormal!=b.NormalAllocation||b.EncumberedReserve!=b.ReserveAllocation)return false;}catch(OverflowException){return false;}}
        canonicalProjection=EncodeCoverage(plan,grant);coverageHashV2=Hash("hpd-graph-participant-capacity-coverage-v2\0"u8,canonicalProjection);return canonicalProjection.Length<=65536;
    }
    private static GraphParticipantCapacityPlanBuildResultV2.Quarantined Bad(string code)=>new(new BoundedAscii(code));
    private static Hash256 Hash(ReadOnlySpan<byte> domain,byte[] bytes){using var h=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);h.AppendData(domain);h.AppendData(bytes);return Hash256.FromBytes(h.GetHashAndReset());}
    private static bool Decode(byte[] bytes,out string factory,out List<string> nodes,out List<(ushort Dimension,byte[] Purpose,ulong Amount,byte Policy)> templates){factory="";nodes=[];templates=[];try{var r=new CborReader(bytes,CborConformanceMode.Ctap2Canonical);if(r.ReadStartMap()!=4||r.ReadUInt64()!=0||r.ReadUInt64()!=1||r.ReadUInt64()!=1) return false;factory=r.ReadTextString();if(r.ReadUInt64()!=2)return false;var n=r.ReadStartArray()!.Value;for(var i=0;i<n;i++)nodes.Add(r.ReadTextString());r.ReadEndArray();if(r.ReadUInt64()!=3)return false;var c=r.ReadStartArray()!.Value;for(var i=0;i<c;i++){if(r.ReadStartMap()!=4||r.ReadUInt64()!=0)return false;var d=checked((ushort)r.ReadUInt64());if(r.ReadUInt64()!=1)return false;var p=r.ReadByteString();if(r.ReadUInt64()!=2)return false;var a=r.ReadUInt64();if(r.ReadUInt64()!=3)return false;var w=checked((byte)r.ReadUInt64());r.ReadEndMap();templates.Add((d,p,a,w));}r.ReadEndArray();r.ReadEndMap();return r.BytesRemaining==0;}catch{return false;}}
    private static byte[] EncodeRequest(CapacityRequestV1 request){var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(6);w.WriteUInt64(0);w.WriteUInt64(2);w.WriteUInt64(1);WriteId(w,request.OperationId);w.WriteUInt64(2);w.WriteEncodedValue(request.Authority.GetCanonicalBytes());w.WriteUInt64(3);w.WriteEncodedValue(MonotonicStampV1Codec.Encode(request.Deadline));w.WriteUInt64(4);w.WriteUInt64((ushort)request.Priority);w.WriteUInt64(5);w.WriteStartArray(request.Charges.Count);foreach(var c in request.Charges)WriteCharge(w,c);w.WriteEndArray();w.WriteEndMap();return w.Encode();}
    private static byte[] EncodeCoverage(GraphParticipantPreGrantPlanV2 plan,CapacityGrantSnapshotV1 grant){var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(9);w.WriteUInt64(0);w.WriteUInt64(2);w.WriteUInt64(1);WriteHash(w,plan.AllocationFingerprint);w.WriteUInt64(2);WriteHash(w,plan.CapacityRequestFingerprint);w.WriteUInt64(3);WriteId(w,plan.ParticipantId);w.WriteUInt64(4);WriteId(w,grant.GrantId);w.WriteUInt64(5);w.WriteEncodedValue(AuthorityPositionCodecsV1.Encode(grant.GrantedAt));w.WriteUInt64(6);w.WriteEncodedValue(AuthorityPositionCodecsV1.Encode(grant.CurrentFact));w.WriteUInt64(7);w.WriteStartMap(grant.ExpiresAt is CapacityGrantExpiryV1.NoExpiry?1:2);w.WriteUInt64(1);w.WriteUInt64((ushort)grant.ExpiresAt.Kind);if(grant.ExpiresAt is CapacityGrantExpiryV1.At at){w.WriteUInt64(2);w.WriteEncodedValue(MonotonicStampV1Codec.Encode(at.Value));}w.WriteEndMap();w.WriteUInt64(8);w.WriteStartArray(grant.Balances.Count);foreach(var b in grant.Balances){w.WriteStartMap(12);w.WriteUInt64(0);WriteCharge(w,b.Charge);long[] v=[b.NormalAllocation,b.ReserveAllocation,b.Unactivated,b.Active,b.Released,b.Consumed,b.AgedOut,b.Revoked,b.ExplicitlyUnknown,b.EncumberedNormal,b.EncumberedReserve];for(var i=0;i<v.Length;i++){w.WriteUInt64((ulong)i+1);w.WriteInt64(v[i]);}w.WriteEndMap();}w.WriteEndArray();w.WriteEndMap();return w.Encode();}
    private static void WriteCharge(CborWriter w,CapacityChargeV1 c){w.WriteStartMap(5);w.WriteUInt64(0);w.WriteUInt64(c.DimensionId.Value);w.WriteUInt64(1);w.WriteEncodedValue(CapacityScopeCanonicalCodecV1.Encode(c.Scope));w.WriteUInt64(2);w.WriteInt64(c.Amount);w.WriteUInt64(3);WriteId(w,c.Purpose);w.WriteUInt64(4);w.WriteStartMap(c.Window is CapacityChargeWindowV1.NoWindow?1:2);w.WriteUInt64(0);w.WriteUInt64((ushort)c.Window.Kind);if(c.Window is CapacityChargeWindowV1.EndsAt at){w.WriteUInt64(1);w.WriteEncodedValue(MonotonicStampV1Codec.Encode(at.Value));}w.WriteEndMap();w.WriteEndMap();}
    private static void WriteId(CborWriter w,OperationId x){Span<byte>b=stackalloc byte[16];if(!x.TryWriteBytes(b))throw new InvalidOperationException();w.WriteByteString(b);}
    private static void WriteId(CborWriter w,ParticipantId x){Span<byte>b=stackalloc byte[16];if(!x.TryWriteBytes(b))throw new InvalidOperationException();w.WriteByteString(b);}
    private static void WriteId(CborWriter w,CapacityGrantId x){Span<byte>b=stackalloc byte[16];if(!x.TryWriteBytes(b))throw new InvalidOperationException();w.WriteByteString(b);}
    private static void WriteId(CborWriter w,CapacityPurposeId x){Span<byte>b=stackalloc byte[16];if(!x.TryWriteBytes(b))throw new InvalidOperationException();w.WriteByteString(b);}
    private static void WriteHash(CborWriter w,Hash256 h){Span<byte>b=stackalloc byte[32];if(!h.TryWriteBytes(b))throw new InvalidOperationException();w.WriteByteString(b);}
}
