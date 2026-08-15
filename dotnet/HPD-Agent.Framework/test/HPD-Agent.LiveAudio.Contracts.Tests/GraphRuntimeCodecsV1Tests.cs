using System.Formats.Cbor;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphRuntimeCodecsV1Tests
{
    [Fact]
    public void Command_ActivateAndRetireRoundTripCanonically()
    {
        var activate=Activate();var encoded=GraphRuntimeCodecsV1.EncodeCommand(activate);
        Assert.True(GraphRuntimeCodecsV1.TryDecodeCommand(encoded,out var decoded));
        Assert.IsType<GraphRuntimeCommandV1.Activate>(decoded);Assert.Equal(encoded,GraphRuntimeCodecsV1.EncodeCommand(decoded!));
        var retire=new GraphRuntimeCommandV1.Retire(Operation(8),Position(22),Position(20),
            GraphRuntimeEffectHashesV1.Retire(Session(),Operation(8),Position(20)));
        var retired=GraphRuntimeCodecsV1.EncodeCommand(retire);Assert.True(GraphRuntimeCodecsV1.TryDecodeCommand(retired,out var decodedRetire));
        Assert.Equal(retired,GraphRuntimeCodecsV1.EncodeCommand(decodedRetire!));Assert.NotEqual(encoded,retired);
    }

    [Fact]
    public void SnapshotAndFact_ExactOptionalOutcomeMatrixRoundTrips()
    {
        var snapshot=Snapshot();var bytes=GraphRuntimeCodecsV1.EncodeSnapshot(snapshot);
        Assert.True(GraphRuntimeCodecsV1.TryDecodeSnapshot(bytes,out var decoded));Assert.Equal(bytes,GraphRuntimeCodecsV1.EncodeSnapshot(decoded!));
        var fact=new GraphRuntimeFactV1(Position(21),Position(20),Position(3),GraphRuntimeOutcomeV1.Activated,snapshot,Hash(9),null);
        var factBytes=GraphRuntimeCodecsV1.EncodeFact(fact);Assert.True(GraphRuntimeCodecsV1.TryDecodeFact(factBytes,out var decodedFact));Assert.Equal(factBytes,GraphRuntimeCodecsV1.EncodeFact(decodedFact!));
        var rejected=new GraphRuntimeFactV1(Position(21),Position(20),Position(3),GraphRuntimeOutcomeV1.Rejected,null,null,new BoundedAscii("effect-refused"));
        Assert.True(GraphRuntimeCodecsV1.TryDecodeFact(GraphRuntimeCodecsV1.EncodeFact(rejected),out _));
    }

    [Fact]
    public void Decoders_RejectTrailingUnknownIndefiniteReorderedAndCrossArm()
    {
        var canonical=GraphRuntimeCodecsV1.EncodeCommand(Activate());
        Assert.False(GraphRuntimeCodecsV1.TryDecodeCommand(canonical.Concat(new byte[]{0}).ToArray(),out _));
        var unknown=(byte[])canonical.Clone();unknown[2]=3;Assert.False(GraphRuntimeCodecsV1.TryDecodeCommand(unknown,out _));
        Assert.False(GraphRuntimeCodecsV1.TryDecodeCommand(new byte[]{0xbf,0x01,0x01,0x02,0x41,0xa0,0xff},out _));
        var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(2);w.WriteUInt64(2);w.WriteByteString(new byte[]{0xa0});w.WriteUInt64(1);w.WriteUInt64(1);w.WriteEndMap();
        Assert.False(GraphRuntimeCodecsV1.TryDecodeCommand(w.Encode(),out _));
        Assert.False(GraphRuntimeCodecsV1.TryDecodeOuter(new byte[GraphRuntimeCodecsV1.MaximumOuterBytes+1],out _));
        var cross=(byte[])canonical.Clone();cross[2]=2;Assert.False(GraphRuntimeCodecsV1.TryDecodeCommand(cross,out _));
    }

    [Fact]
    public void Decoders_RejectMalformedOptionalsNestedOversizeAndOutcomeMutation()
    {
        var oversized=new CborWriter(CborConformanceMode.Ctap2Canonical);oversized.WriteStartMap(2);oversized.WriteUInt64(1);oversized.WriteUInt64(1);oversized.WriteUInt64(2);oversized.WriteByteString(new byte[65_537]);oversized.WriteEndMap();
        Assert.False(GraphRuntimeCodecsV1.TryDecodeCommand(oversized.Encode(),out _));
        var snapshot=GraphRuntimeCodecsV1.EncodeSnapshot(Snapshot());
        Assert.False(GraphRuntimeCodecsV1.TryDecodeSnapshot(Rewrite(snapshot,9,w=>w.WriteByteString(new byte[4_097])),out _));
        var fact=new GraphRuntimeFactV1(Position(21),Position(20),Position(3),GraphRuntimeOutcomeV1.Activated,Snapshot(),Hash(9),null);
        var factBytes=GraphRuntimeCodecsV1.EncodeFact(fact);
        var optional=new CborWriter(CborConformanceMode.Ctap2Canonical);optional.WriteStartMap(2);optional.WriteUInt64(1);optional.WriteUInt64(0);optional.WriteUInt64(2);optional.WriteByteString(new byte[]{1});optional.WriteEndMap();
        Assert.False(GraphRuntimeCodecsV1.TryDecodeFact(Rewrite(factBytes,5,w=>w.WriteByteString(optional.Encode())),out _));
        Assert.False(GraphRuntimeCodecsV1.TryDecodeFact(Rewrite(factBytes,4,w=>w.WriteUInt64(0)),out _));
    }

    [Fact]
    public void Registrations_RequireExactSessionGraphSchemaAndBodyHashDomain()
    {
        var commandBody=GraphRuntimeCodecsV1.EncodeCommand(Activate());var outer=GraphRuntimeCodecsV1.EncodeOuter(new GraphRuntimeOwnerPayloadV1(Session(),Authority(),commandBody));
        Assert.True(GraphRuntimePayloadRegistrationsV1.Command.Validate(outer,Session()));
        Assert.False(GraphRuntimePayloadRegistrationsV1.Fact.Validate(outer,Session()));
        Assert.False(GraphRuntimePayloadRegistrationsV1.Command.Validate(outer,new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(40)),LiveSessionId.FromValue(Id(41)))));
        var other=new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(40)),LiveSessionId.FromValue(Id(41)));
        var crossOperation=Operation(8);var cross=new GraphRuntimeCommandV1.Retire(crossOperation,new JournalPositionV1(other,10),new JournalPositionV1(other,9),GraphRuntimeEffectHashesV1.Retire(other,crossOperation,new JournalPositionV1(other,9)));
        Assert.False(GraphRuntimePayloadRegistrationsV1.Command.Validate(Outer(GraphRuntimeCodecsV1.EncodeCommand(cross)),Session()));
        var noGraph=ExpectedAuthorityVectorV1.Create(Session(),[new AuthorityAxisValueV1.Activity(ActivityGenerationId.FromValue(Id(7)))]);
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeOwnerPayloadV1(Session(),noGraph,commandBody));
        Assert.NotEqual(GraphRuntimeCodecsV1.Hash(GraphRuntimeCodecsV1.CommandOuterSchemaId,outer),GraphRuntimeCodecsV1.Hash(GraphRuntimeCodecsV1.FactOuterSchemaId,outer));
    }

    [Fact]
    public void EnvelopeValidation_RejectsWrongIdHashOwnerSchemaThreadOrderAndRequestHash()
    {
        var command=Activate();var payload=Outer(GraphRuntimeCodecsV1.EncodeCommand(command));
        var exact=Envelope(GraphRuntimeFactIdsV1.Command(Session(),command.OperationId,command.Kind),Position(30),
            GraphRuntimePayloadRegistrationsV1.Command,payload);
        Assert.True(GraphRuntimePayloadRegistrationsV1.ValidateCommandEnvelope(exact));
        Assert.False(GraphRuntimePayloadRegistrationsV1.ValidateCommandEnvelope(Envelope(JournalFactId.Create(),Position(30),GraphRuntimePayloadRegistrationsV1.Command,payload)));
        Assert.False(GraphRuntimePayloadRegistrationsV1.ValidateCommandEnvelope(Envelope(exact.FactId,Position(30),GraphRuntimePayloadRegistrationsV1.Command,payload,hash:Hash(33))));
        Assert.False(GraphRuntimePayloadRegistrationsV1.ValidateCommandEnvelope(Envelope(exact.FactId,Position(30),GraphRuntimePayloadRegistrationsV1.Command,payload,owner:OwnerSliceId.S1)));
        Assert.False(GraphRuntimePayloadRegistrationsV1.ValidateCommandEnvelope(Envelope(exact.FactId,Position(30),GraphRuntimePayloadRegistrationsV1.Fact,payload)));
        Assert.False(GraphRuntimePayloadRegistrationsV1.ValidateCommandEnvelope(Envelope(exact.FactId,Position(30),GraphRuntimePayloadRegistrationsV1.Command,payload,thread:true)));
        var future=new GraphRuntimeCommandV1.Retire(Operation(8),Position(31),Position(20),GraphRuntimeEffectHashesV1.Retire(Session(),Operation(8),Position(20)));
        Assert.False(GraphRuntimePayloadRegistrationsV1.ValidateCommandEnvelope(Envelope(GraphRuntimeFactIdsV1.Command(Session(),future.OperationId,future.Kind),Position(30),GraphRuntimePayloadRegistrationsV1.Command,Outer(GraphRuntimeCodecsV1.EncodeCommand(future)))));
        var wrongHash=new GraphRuntimeCommandV1.Retire(Operation(8),Position(22),Position(20),Hash(31));
        Assert.False(GraphRuntimePayloadRegistrationsV1.ValidateCommandEnvelope(Envelope(GraphRuntimeFactIdsV1.Command(Session(),wrongHash.OperationId,wrongHash.Kind),Position(30),GraphRuntimePayloadRegistrationsV1.Command,Outer(GraphRuntimeCodecsV1.EncodeCommand(wrongHash)))));
    }

    [Fact]
    public void FactEnvelopeValidation_AcceptsSuccessAndPriorFailureSnapshotOnlyWithExactOrder()
    {
        var success=new GraphRuntimeFactV1(Position(21),Position(20),Position(3),GraphRuntimeOutcomeV1.Activated,Snapshot(),Hash(9),null);
        var successEnvelope=Envelope(GraphRuntimeFactIdsV1.Result(Position(21)),Position(22),GraphRuntimePayloadRegistrationsV1.Fact,Outer(GraphRuntimeCodecsV1.EncodeFact(success)));
        Assert.True(GraphRuntimePayloadRegistrationsV1.ValidateFactEnvelope(successEnvelope));
        Assert.False(GraphRuntimePayloadRegistrationsV1.ValidateFactEnvelope(Envelope(JournalFactId.Create(),Position(22),GraphRuntimePayloadRegistrationsV1.Fact,successEnvelope.Payload.ToArray())));
        var prior=new GraphRuntimeSnapshotV1(GraphRuntimePhaseV1.Active,GraphGenerationId.FromValue(Id(5)),Hash(4),Position(6),Authority(),Operation(8),Position(20),Position(20),null);
        var failure=new GraphRuntimeFactV1(Position(21),Position(20),Position(3),GraphRuntimeOutcomeV1.Rejected,prior,null,new BoundedAscii("effect-refused"));
        Assert.True(GraphRuntimePayloadRegistrationsV1.ValidateFactEnvelope(Envelope(GraphRuntimeFactIdsV1.Result(Position(21)),Position(22),GraphRuntimePayloadRegistrationsV1.Fact,Outer(GraphRuntimeCodecsV1.EncodeFact(failure)))));
        Assert.False(GraphRuntimePayloadRegistrationsV1.ValidateFactEnvelope(Envelope(GraphRuntimeFactIdsV1.Result(Position(21)),Position(20),GraphRuntimePayloadRegistrationsV1.Fact,Outer(GraphRuntimeCodecsV1.EncodeFact(failure)))));
    }

    private static GraphRuntimeCommandV1.Activate Activate(){var operation=Operation(8);return new(operation,Position(10),Position(3),Hash(4),GraphGenerationId.FromValue(Id(5)),Position(6),GraphRuntimeEffectHashesV1.Activate(Session(),operation,Position(3),Hash(4),GraphGenerationId.FromValue(Id(5)),Position(6)));}
    private static GraphRuntimeSnapshotV1 Snapshot()=>new(GraphRuntimePhaseV1.Active,GraphGenerationId.FromValue(Id(5)),Hash(4),Position(6),Authority(),Operation(8),Position(22),Position(22),null);
    private static byte[] Outer(byte[] body)=>GraphRuntimeCodecsV1.EncodeOuter(new GraphRuntimeOwnerPayloadV1(Session(),Authority(),body));
    private static AuthorityFactEnvelopeV1 Envelope(JournalFactId id,JournalPositionV1 position,AuthorityPayloadRegistrationV1 registration,byte[] payload,Hash256? hash=null,OwnerSliceId owner=OwnerSliceId.S2,bool thread=false)=>new(id,position,thread?new ThreadPositionV1(ThreadId.Create(),1,1):null,owner,registration.Schema,payload,hash??AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,payload),new CorrelationEnvelopeV1(TenantId.Create()),new UtcInstant(1),new UtcInstant(2),new IntegrityEnvelopeV1(1,1,Hash(30),[]));
    private static ExpectedAuthorityVectorV1 Authority()=>ExpectedAuthorityVectorV1.Create(Session(),[new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(Id(5)))]);
    private static SessionAuthorityStampV1 Session()=>new(RuntimeGenerationId.FromValue(Id(1)),LiveSessionId.FromValue(Id(2)));
    private static JournalPositionV1 Position(long n)=>new(Session(),n);private static OperationId Operation(byte n)=>OperationId.FromValue(Id(n));
    private static StableId128 Id(byte n)=>StableId128.FromBytes(Enumerable.Repeat(n,16).ToArray());private static Hash256 Hash(byte n){Hash256.TryCreate(Enumerable.Repeat(n,32).ToArray(),out var h);return h;}
    private static byte[] Rewrite(byte[] encoded,ulong target,Action<CborWriter> replacement){var r=new CborReader(encoded,CborConformanceMode.Ctap2Canonical);var count=r.ReadStartMap()!.Value;var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(count);for(var i=0;i<count;i++){var tag=r.ReadUInt64();w.WriteUInt64(tag);if(tag==target){r.SkipValue();replacement(w);}else w.WriteEncodedValue(r.ReadEncodedValue().Span);}r.ReadEndMap();w.WriteEndMap();return w.Encode();}
}
