using System.Formats.Cbor;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphRuntimeCodecsTests
{
    [Fact]
    public void Command_ActivateAndRetireRoundTripCanonically()
    {
        var activate=Activate();var encoded=GraphRuntimeCodecs.EncodeCommand(activate);
        Assert.True(GraphRuntimeCodecs.TryDecodeCommand(encoded,out var decoded));
        Assert.IsType<GraphRuntimeCommand.Activate>(decoded);Assert.Equal(encoded,GraphRuntimeCodecs.EncodeCommand(decoded!));
        var retire=new GraphRuntimeCommand.Retire(Operation(8),Position(22),Position(20),
            GraphRuntimeEffectHashes.Retire(Session(),Operation(8),Position(20)));
        var retired=GraphRuntimeCodecs.EncodeCommand(retire);Assert.True(GraphRuntimeCodecs.TryDecodeCommand(retired,out var decodedRetire));
        Assert.Equal(retired,GraphRuntimeCodecs.EncodeCommand(decodedRetire!));Assert.NotEqual(encoded,retired);
    }

    [Fact]
    public void SnapshotAndFact_ExactOptionalOutcomeMatrixRoundTrips()
    {
        var snapshot=Snapshot();var bytes=GraphRuntimeCodecs.EncodeSnapshot(snapshot);
        Assert.True(GraphRuntimeCodecs.TryDecodeSnapshot(bytes,out var decoded));Assert.Equal(bytes,GraphRuntimeCodecs.EncodeSnapshot(decoded!));
        var fact=new GraphRuntimeFact(Position(21),Position(20),Position(3),GraphRuntimeOutcome.Activated,snapshot,Hash(9),null);
        var factBytes=GraphRuntimeCodecs.EncodeFact(fact);Assert.True(GraphRuntimeCodecs.TryDecodeFact(factBytes,out var decodedFact));Assert.Equal(factBytes,GraphRuntimeCodecs.EncodeFact(decodedFact!));
        var rejected=new GraphRuntimeFact(Position(21),Position(20),Position(3),GraphRuntimeOutcome.Rejected,null,null,new BoundedAscii("effect-refused"));
        Assert.True(GraphRuntimeCodecs.TryDecodeFact(GraphRuntimeCodecs.EncodeFact(rejected),out _));
    }

    [Fact]
    public void Decoders_RejectTrailingUnknownIndefiniteReorderedAndCrossArm()
    {
        var canonical=GraphRuntimeCodecs.EncodeCommand(Activate());
        Assert.False(GraphRuntimeCodecs.TryDecodeCommand(canonical.Concat(new byte[]{0}).ToArray(),out _));
        var unknown=(byte[])canonical.Clone();unknown[2]=3;Assert.False(GraphRuntimeCodecs.TryDecodeCommand(unknown,out _));
        Assert.False(GraphRuntimeCodecs.TryDecodeCommand(new byte[]{0xbf,0x01,0x01,0x02,0x41,0xa0,0xff},out _));
        var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(2);w.WriteUInt64(2);w.WriteByteString(new byte[]{0xa0});w.WriteUInt64(1);w.WriteUInt64(1);w.WriteEndMap();
        Assert.False(GraphRuntimeCodecs.TryDecodeCommand(w.Encode(),out _));
        Assert.False(GraphRuntimeCodecs.TryDecodeOuter(new byte[GraphRuntimeCodecs.MaximumOuterBytes+1],out _));
        var cross=(byte[])canonical.Clone();cross[2]=2;Assert.False(GraphRuntimeCodecs.TryDecodeCommand(cross,out _));
    }

    [Fact]
    public void Decoders_RejectMalformedOptionalsNestedOversizeAndOutcomeMutation()
    {
        var oversized=new CborWriter(CborConformanceMode.Ctap2Canonical);oversized.WriteStartMap(2);oversized.WriteUInt64(1);oversized.WriteUInt64(1);oversized.WriteUInt64(2);oversized.WriteByteString(new byte[65_537]);oversized.WriteEndMap();
        Assert.False(GraphRuntimeCodecs.TryDecodeCommand(oversized.Encode(),out _));
        var snapshot=GraphRuntimeCodecs.EncodeSnapshot(Snapshot());
        Assert.False(GraphRuntimeCodecs.TryDecodeSnapshot(Rewrite(snapshot,9,w=>w.WriteByteString(new byte[4_097])),out _));
        var fact=new GraphRuntimeFact(Position(21),Position(20),Position(3),GraphRuntimeOutcome.Activated,Snapshot(),Hash(9),null);
        var factBytes=GraphRuntimeCodecs.EncodeFact(fact);
        var optional=new CborWriter(CborConformanceMode.Ctap2Canonical);optional.WriteStartMap(2);optional.WriteUInt64(1);optional.WriteUInt64(0);optional.WriteUInt64(2);optional.WriteByteString(new byte[]{1});optional.WriteEndMap();
        Assert.False(GraphRuntimeCodecs.TryDecodeFact(Rewrite(factBytes,5,w=>w.WriteByteString(optional.Encode())),out _));
        Assert.False(GraphRuntimeCodecs.TryDecodeFact(Rewrite(factBytes,4,w=>w.WriteUInt64(0)),out _));
    }

    [Fact]
    public void Registrations_RequireExactSessionGraphSchemaAndBodyHashDomain()
    {
        var commandBody=GraphRuntimeCodecs.EncodeCommand(Activate());var outer=GraphRuntimeCodecs.EncodeOuter(new GraphRuntimeOwnerPayload(Session(),Authority(),commandBody));
        Assert.True(GraphRuntimePayloadRegistrations.Command.Validate(outer,Session()));
        Assert.False(GraphRuntimePayloadRegistrations.Fact.Validate(outer,Session()));
        Assert.False(GraphRuntimePayloadRegistrations.Command.Validate(outer,new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(40)),LiveSessionId.FromValue(Id(41)))));
        var other=new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(40)),LiveSessionId.FromValue(Id(41)));
        var crossOperation=Operation(8);var cross=new GraphRuntimeCommand.Retire(crossOperation,new JournalPositionV1(other,10),new JournalPositionV1(other,9),GraphRuntimeEffectHashes.Retire(other,crossOperation,new JournalPositionV1(other,9)));
        Assert.False(GraphRuntimePayloadRegistrations.Command.Validate(Outer(GraphRuntimeCodecs.EncodeCommand(cross)),Session()));
        var noGraph=ExpectedAuthorityVectorV1.Create(Session(),[new AuthorityAxisValueV1.Activity(ActivityGenerationId.FromValue(Id(7)))]);
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeOwnerPayload(Session(),noGraph,commandBody));
        Assert.NotEqual(GraphRuntimeCodecs.Hash(GraphRuntimeCodecs.CommandOuterSchemaId,outer),GraphRuntimeCodecs.Hash(GraphRuntimeCodecs.FactOuterSchemaId,outer));
    }

    [Fact]
    public void EnvelopeValidation_RejectsWrongIdHashOwnerSchemaThreadOrderAndRequestHash()
    {
        var command=Activate();var payload=Outer(GraphRuntimeCodecs.EncodeCommand(command));
        var exact=Envelope(GraphRuntimeFactIds.Command(Session(),command.OperationId,command.Kind),Position(30),
            GraphRuntimePayloadRegistrations.Command,payload);
        Assert.True(GraphRuntimePayloadRegistrations.ValidateCommandEnvelope(exact));
        Assert.False(GraphRuntimePayloadRegistrations.ValidateCommandEnvelope(Envelope(JournalFactId.Create(),Position(30),GraphRuntimePayloadRegistrations.Command,payload)));
        Assert.False(GraphRuntimePayloadRegistrations.ValidateCommandEnvelope(Envelope(exact.FactId,Position(30),GraphRuntimePayloadRegistrations.Command,payload,hash:Hash(33))));
        Assert.False(GraphRuntimePayloadRegistrations.ValidateCommandEnvelope(Envelope(exact.FactId,Position(30),GraphRuntimePayloadRegistrations.Command,payload,owner:OwnerSliceId.S1)));
        Assert.False(GraphRuntimePayloadRegistrations.ValidateCommandEnvelope(Envelope(exact.FactId,Position(30),GraphRuntimePayloadRegistrations.Fact,payload)));
        Assert.False(GraphRuntimePayloadRegistrations.ValidateCommandEnvelope(Envelope(exact.FactId,Position(30),GraphRuntimePayloadRegistrations.Command,payload,thread:true)));
        var future=new GraphRuntimeCommand.Retire(Operation(8),Position(31),Position(20),GraphRuntimeEffectHashes.Retire(Session(),Operation(8),Position(20)));
        Assert.False(GraphRuntimePayloadRegistrations.ValidateCommandEnvelope(Envelope(GraphRuntimeFactIds.Command(Session(),future.OperationId,future.Kind),Position(30),GraphRuntimePayloadRegistrations.Command,Outer(GraphRuntimeCodecs.EncodeCommand(future)))));
        var wrongHash=new GraphRuntimeCommand.Retire(Operation(8),Position(22),Position(20),Hash(31));
        Assert.False(GraphRuntimePayloadRegistrations.ValidateCommandEnvelope(Envelope(GraphRuntimeFactIds.Command(Session(),wrongHash.OperationId,wrongHash.Kind),Position(30),GraphRuntimePayloadRegistrations.Command,Outer(GraphRuntimeCodecs.EncodeCommand(wrongHash)))));
    }

    [Fact]
    public void FactEnvelopeValidation_AcceptsSuccessAndPriorFailureSnapshotOnlyWithExactOrder()
    {
        var success=new GraphRuntimeFact(Position(21),Position(20),Position(3),GraphRuntimeOutcome.Activated,Snapshot(),Hash(9),null);
        var successEnvelope=Envelope(GraphRuntimeFactIds.Result(Position(21)),Position(22),GraphRuntimePayloadRegistrations.Fact,Outer(GraphRuntimeCodecs.EncodeFact(success)));
        Assert.True(GraphRuntimePayloadRegistrations.ValidateFactEnvelope(successEnvelope));
        Assert.False(GraphRuntimePayloadRegistrations.ValidateFactEnvelope(Envelope(JournalFactId.Create(),Position(22),GraphRuntimePayloadRegistrations.Fact,successEnvelope.Payload.ToArray())));
        var prior=new GraphRuntimeSnapshot(GraphRuntimePhase.Active,GraphGenerationId.FromValue(Id(5)),Hash(4),Position(6),Authority(),Operation(8),Position(20),Position(20),null);
        var failure=new GraphRuntimeFact(Position(21),Position(20),Position(3),GraphRuntimeOutcome.Rejected,prior,null,new BoundedAscii("effect-refused"));
        Assert.True(GraphRuntimePayloadRegistrations.ValidateFactEnvelope(Envelope(GraphRuntimeFactIds.Result(Position(21)),Position(22),GraphRuntimePayloadRegistrations.Fact,Outer(GraphRuntimeCodecs.EncodeFact(failure)))));
        Assert.False(GraphRuntimePayloadRegistrations.ValidateFactEnvelope(Envelope(GraphRuntimeFactIds.Result(Position(21)),Position(20),GraphRuntimePayloadRegistrations.Fact,Outer(GraphRuntimeCodecs.EncodeFact(failure)))));
    }

    private static GraphRuntimeCommand.Activate Activate(){var operation=Operation(8);return new(operation,Position(10),Position(3),Hash(4),GraphGenerationId.FromValue(Id(5)),Position(6),GraphRuntimeEffectHashes.Activate(Session(),operation,Position(3),Hash(4),GraphGenerationId.FromValue(Id(5)),Position(6)));}
    private static GraphRuntimeSnapshot Snapshot()=>new(GraphRuntimePhase.Active,GraphGenerationId.FromValue(Id(5)),Hash(4),Position(6),Authority(),Operation(8),Position(22),Position(22),null);
    private static byte[] Outer(byte[] body)=>GraphRuntimeCodecs.EncodeOuter(new GraphRuntimeOwnerPayload(Session(),Authority(),body));
    private static AuthorityFactEnvelopeV1 Envelope(JournalFactId id,JournalPositionV1 position,AuthorityPayloadRegistrationV1 registration,byte[] payload,Hash256? hash=null,OwnerSliceId owner=OwnerSliceId.S2,bool thread=false)=>new(id,position,thread?new ThreadPositionV1(ThreadId.Create(),1,1):null,owner,registration.Schema,payload,hash??AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,payload),new CorrelationEnvelopeV1(TenantId.Create()),new UtcInstant(1),new UtcInstant(2),new IntegrityEnvelopeV1(1,1,Hash(30),[]));
    private static ExpectedAuthorityVectorV1 Authority()=>ExpectedAuthorityVectorV1.Create(Session(),[new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(Id(5)))]);
    private static SessionAuthorityStampV1 Session()=>new(RuntimeGenerationId.FromValue(Id(1)),LiveSessionId.FromValue(Id(2)));
    private static JournalPositionV1 Position(long n)=>new(Session(),n);private static OperationId Operation(byte n)=>OperationId.FromValue(Id(n));
    private static StableId128 Id(byte n)=>StableId128.FromBytes(Enumerable.Repeat(n,16).ToArray());private static Hash256 Hash(byte n){Hash256.TryCreate(Enumerable.Repeat(n,32).ToArray(),out var h);return h;}
    private static byte[] Rewrite(byte[] encoded,ulong target,Action<CborWriter> replacement){var r=new CborReader(encoded,CborConformanceMode.Ctap2Canonical);var count=r.ReadStartMap()!.Value;var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(count);for(var i=0;i<count;i++){var tag=r.ReadUInt64();w.WriteUInt64(tag);if(tag==target){r.SkipValue();replacement(w);}else w.WriteEncodedValue(r.ReadEncodedValue().Span);}r.ReadEndMap();w.WriteEndMap();return w.Encode();}
}
