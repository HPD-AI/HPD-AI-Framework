using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class GraphParticipantBindingWireV1Tests
{
    [Fact]
    public void FourBodies_HaveFrozenIndependentCanonicalGoldens()
    {
        var encodings=AllBodies().Select(GraphParticipantBindingCodecsV1Encode).Select(static b=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant()).ToArray();
        Assert.Equal(new[]{"c81116a77e20e98e116fa67063e7652f62780df38e0e9ae5916b12c488eb814e","d11823da1bab09e17b3b9bdff1b25eeb6ab3acc0b3b0d475a8d216b5c6e22123","ffcdf867cb7c5d9ca8ae7d16bb0b018cf83a36ccff5370209bcd6ad4c4a0ad8e","61065708145a825d25d6a3cea2bce491a8d902aec260f9afa14e179ab5335106"},encodings);
    }

    [Fact]
    public void FourOuters_HaveFrozenIndependentCanonicalGoldens()
    {
        var (s,a)=Authority();var bodies=AllBodies().Select(GraphParticipantBindingCodecsV1Encode).ToArray();
        var encodings=new[]{GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(s,a,bodies[0])),GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationFactV1(s,a,bodies[1])),GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(s,a,bodies[2])),GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingFactV1(s,a,bodies[3]))}.Select(static b=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant()).ToArray();
        Assert.Equal(new[]{"a0403f500d649ae8cce861bd9feb9611718e3f5c849bf65a4f9d4e0ba46877a4","df88001bac1c79d47f180577fa2b62c10cd0cf78a2570d796fea261522dfb96f","5026169b3b6f00ac5fcd7cfd957f44cc25eea8653d28b0e54a4baa7799b51736","de9339bd82b4e57bd856e5474255b16f0ef30bc023b055165cf2b26cecda513d"},encodings);
    }

    [Fact]
    public void AllBodiesAndOuterPayloads_RoundTripCanonicalBytes()
    {
        var command=ReservationCommand(); var bytes=GraphParticipantBindingCodecsV1.Encode(command);
        Assert.True(GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(bytes,out var decoded));
        Assert.Equal(bytes,GraphParticipantBindingCodecsV1.Encode(decoded!));
        var reservation=new GraphParticipantReservationV1(Participant(),new BoundedAscii("factory"),Keys());
        var fact=new GraphParticipantReservationFactBodyV1(Operation(),Position(1),null,1,Runtime(),Hash("plan"),Hash("topology"),Hash("executable"),reservation,null,Stamp());
        var factBytes=GraphParticipantBindingCodecsV1.Encode(fact); Assert.True(GraphParticipantBindingCodecsV1.TryDecodeReservationFactBody(factBytes,out var decodedFact)); Assert.Equal(factBytes,GraphParticipantBindingCodecsV1.Encode(decodedFact!));
        var proof=new CapacityGrantBindingProofV1(Grant(),Position(2),Position(3),3,Hash("coverage"));
        var bindingCommand=new GraphParticipantBindingCommandBodyV1(Operation(),Position(1),null,Graph(),Runtime(),Hash("plan"),Hash("topology"),Hash("executable"),proof,Stamp());
        var bindingBytes=GraphParticipantBindingCodecsV1.Encode(bindingCommand); Assert.True(GraphParticipantBindingCodecsV1.TryDecodeBindingCommandBody(bindingBytes,out var decodedBinding)); Assert.Equal(bindingBytes,GraphParticipantBindingCodecsV1.Encode(decodedBinding!));
        var binding=new GraphParticipantBindingV1(Participant(),new BoundedAscii("factory"),Keys());
        var result=new GraphParticipantBindingFactBodyV1(Operation(),Position(4),Position(1),null,1,Graph(),Runtime(),Hash("plan"),Hash("topology"),Hash("executable"),binding,proof,null,Stamp());
        var resultBytes=GraphParticipantBindingCodecsV1.Encode(result); Assert.True(GraphParticipantBindingCodecsV1.TryDecodeBindingFactBody(resultBytes,out var decodedResult)); Assert.Equal(resultBytes,GraphParticipantBindingCodecsV1.Encode(decodedResult!));

        var (session,vector)=Authority(); var source=bytes.ToArray(); var outer=new GraphParticipantReservationCommandV1(session,vector,source); source[0]^=0xff;
        Assert.NotEqual(source[0],outer.Body[0]); var outerBytes=GraphParticipantBindingCodecsV1.Encode(outer);
        Assert.True(GraphParticipantBindingCodecsV1.TryDecodeReservationCommand(outerBytes,out var decodedOuter)); Assert.Equal(outerBytes,GraphParticipantBindingCodecsV1.Encode(decodedOuter!));
    }

    [Fact]
    public void Decoders_RejectBoundsTrailingMalformedAndNoncanonical()
    {
        Assert.False(GraphParticipantBindingCodecsV1.TryDecodeBindingCommandBody(new byte[4097],out _));
        Assert.False(GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(new byte[16385],out _));
        var valid=GraphParticipantBindingCodecsV1.Encode(ReservationCommand());
        Assert.False(GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(valid.Concat(new byte[]{0}).ToArray(),out _));
        Assert.False(GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(new byte[]{0xbf,0xff},out _));
        Assert.False(GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(new byte[]{0xa1,0x01,0xf6},out _));
        Assert.False(GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(new byte[]{0xa1,0x18,0x01,0x00},out _));
        Assert.False(GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(new byte[]{0xa2,0x01,0x00,0x01,0x00},out _));
    }

    [Fact]
    public void AllFourOuterConstructors_EnforceExactSchemaMaximumAndOwnBodies()
    {
        var(s,a)=Authority();var maximums=new[]{16384,16384,4096,16384};
        for(var i=0;i<4;i++)
        {
            var source=new byte[maximums[i]];object outer=i switch{0=>new GraphParticipantReservationCommandV1(s,a,source),1=>new GraphParticipantReservationFactV1(s,a,source),2=>new GraphParticipantBindingCommandV1(s,a,source),_=>new GraphParticipantBindingFactV1(s,a,source)};source[0]=1;
            Assert.Equal(0,i switch{0=>((GraphParticipantReservationCommandV1)outer).Body[0],1=>((GraphParticipantReservationFactV1)outer).Body[0],2=>((GraphParticipantBindingCommandV1)outer).Body[0],_=>((GraphParticipantBindingFactV1)outer).Body[0]});
            Assert.ThrowsAny<ArgumentException>(()=>{_ = i switch{0=>(object)new GraphParticipantReservationCommandV1(s,a,new byte[maximums[i]+1]),1=>new GraphParticipantReservationFactV1(s,a,new byte[maximums[i]+1]),2=>new GraphParticipantBindingCommandV1(s,a,new byte[maximums[i]+1]),_=>new GraphParticipantBindingFactV1(s,a,new byte[maximums[i]+1])};});
        }
    }

    [Theory]
    [InlineData(0,"40")][InlineData(1,"4100")][InlineData(4096,"59100000")][InlineData(4097,"59100100")]
    [InlineData(16384,"59400000")][InlineData(16385,"59400100")][InlineData(65536,"5a0001000000")][InlineData(65537,"5a0001000100")]
    public void CanonicalBstrHeaders_MatchManifest(int length,string prefix)
    {
        var w=new System.Formats.Cbor.CborWriter(System.Formats.Cbor.CborConformanceMode.Ctap2Canonical);w.WriteByteString(new byte[length]);Assert.StartsWith(prefix,Convert.ToHexString(w.Encode()).ToLowerInvariant());
    }

    [Theory]
    [InlineData("a0")][InlineData("bf01f6ff")][InlineData("a201000100")][InlineData("a101f6")][InlineData("a101fa00000000")][InlineData("a1180100")][InlineData("a101009f")]
    public void EveryBodyDecoder_RejectsMalformedFamilies(string hex)
    {
        var b=Convert.FromHexString(hex);Assert.False(GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(b,out _));Assert.False(GraphParticipantBindingCodecsV1.TryDecodeReservationFactBody(b,out _));Assert.False(GraphParticipantBindingCodecsV1.TryDecodeBindingCommandBody(b,out _));Assert.False(GraphParticipantBindingCodecsV1.TryDecodeBindingFactBody(b,out _));
    }

    [Fact]
    public void FactIds_AreDomainSeparatedAndFrozen()
    {
        var (session,_)=Authority(); var operation=Operation(); var position=new JournalPositionV1(session,7);
        Assert.Equal(new[]{"fct:7CC58429EXH60ETHABZ47RF3CJ","fct:7816BVG1X1QNGSXT7580V814GV","fct:29NVF4CVMHZXX047X4V6427NY2","fct:7EZFC98G9T8E6T1F89YFSPZAYA"},new[]{GraphParticipantBindingFactIdsV1.ReservationCommand(session,operation).ToString(),GraphParticipantBindingFactIdsV1.ReservationFact(position).ToString(),GraphParticipantBindingFactIdsV1.BindingCommand(session,operation).ToString(),GraphParticipantBindingFactIdsV1.BindingFact(position).ToString()});
        Assert.NotEqual(default,GraphParticipantBindingFactIdsV1.Participant(session,operation));
        Assert.Equal("par:5B1YYP72DB4H5EJVSE779X0EMQ",GraphParticipantBindingFactIdsV1.Participant(session,operation).ToString());
        Span<byte> zero=stackalloc byte[32];Span<byte> repaired=stackalloc byte[16];Assert.True(GraphParticipantBindingFactIdsV1.RepairZero(zero).TryWriteBytes(repaired));Assert.Equal("00000000000000000000000000000001",Convert.ToHexString(repaired));
    }

    private static object[] AllBodies()
    {
        var proof=new CapacityGrantBindingProofV1(Grant(),Position(2),Position(3),3,Hash("coverage"));var reservation=new GraphParticipantReservationV1(Participant(),new BoundedAscii("factory"),Keys());var binding=new GraphParticipantBindingV1(Participant(),new BoundedAscii("factory"),Keys());
        return new object[]{ReservationCommand(),new GraphParticipantReservationFactBodyV1(Operation(),Position(1),null,1,Runtime(),Hash("plan"),Hash("topology"),Hash("executable"),reservation,null,Stamp()),new GraphParticipantBindingCommandBodyV1(Operation(),Position(1),null,Graph(),Runtime(),Hash("plan"),Hash("topology"),Hash("executable"),proof,Stamp()),new GraphParticipantBindingFactBodyV1(Operation(),Position(4),Position(1),null,1,Graph(),Runtime(),Hash("plan"),Hash("topology"),Hash("executable"),binding,proof,null,Stamp())};
    }
    private static byte[] GraphParticipantBindingCodecsV1Encode(object value)=>value switch{GraphParticipantReservationCommandBodyV1 v=>GraphParticipantBindingCodecsV1.Encode(v),GraphParticipantReservationFactBodyV1 v=>GraphParticipantBindingCodecsV1.Encode(v),GraphParticipantBindingCommandBodyV1 v=>GraphParticipantBindingCodecsV1.Encode(v),GraphParticipantBindingFactBodyV1 v=>GraphParticipantBindingCodecsV1.Encode(v),_=>throw new ArgumentException()};

    [Fact]
    public void Registrations_AreExactS1SchemasAndValidateEnvelopeAndInnerBody()
    {
        var registrations=new[]{GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand,GraphParticipantBindingPayloadRegistrationsV1.ReservationFact,GraphParticipantBindingPayloadRegistrationsV1.BindingCommand,GraphParticipantBindingPayloadRegistrationsV1.BindingFact};
        Assert.Equal(new ushort[]{38,39,40,41},new[]{GraphParticipantBindingPayloadRegistrationsV1.ReservationCommandDiscriminator,GraphParticipantBindingPayloadRegistrationsV1.ReservationFactDiscriminator,GraphParticipantBindingPayloadRegistrationsV1.BindingCommandDiscriminator,GraphParticipantBindingPayloadRegistrationsV1.BindingFactDiscriminator});
        Assert.All(registrations,r=>Assert.Equal(OwnerSliceId.S1,r.Owner)); Assert.Equal(4,registrations.Select(r=>r.Schema).Distinct().Count());
        var (session,vector)=Authority(); var inner=GraphParticipantBindingCodecsV1.Encode(ReservationCommand()); var outer=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(session,vector,inner));
        Assert.True(GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand.Validate(outer,session)); Assert.False(GraphParticipantBindingPayloadRegistrationsV1.ReservationFact.Validate(outer,session));
    }

    [Fact]
    public void FourRegistrations_AcceptOnlyTheirInnerSchemaAndExactSession()
    {
        var(s,a)=Authority();var bodies=AllBodies().Select(GraphParticipantBindingCodecsV1Encode).ToArray();var payloads=new[]{GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(s,a,bodies[0])),GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationFactV1(s,a,bodies[1])),GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(s,a,bodies[2])),GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingFactV1(s,a,bodies[3]))};var registrations=new[]{GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand,GraphParticipantBindingPayloadRegistrationsV1.ReservationFact,GraphParticipantBindingPayloadRegistrationsV1.BindingCommand,GraphParticipantBindingPayloadRegistrationsV1.BindingFact};
        for(var i=0;i<4;i++)for(var j=0;j<4;j++)Assert.Equal(i==j,registrations[i].Validate(payloads[j],s));
        var other=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());Assert.All(registrations,r=>Assert.False(r.Validate(payloads[Array.IndexOf(registrations,r)],other)));
        Assert.Equal(new[]{GraphParticipantBindingCodecsV1.ReservationCommandSchemaId,GraphParticipantBindingCodecsV1.ReservationFactSchemaId,GraphParticipantBindingCodecsV1.BindingCommandSchemaId,GraphParticipantBindingCodecsV1.BindingFactSchemaId},registrations.Select(r=>r.SchemaToken.ToString()));
        Assert.All(registrations,r=>{Assert.Equal((ushort)1,r.Schema.Major);Assert.Equal((ushort)0,r.Schema.Minor);Assert.Equal(GraphParticipantBindingCodecsV1.MaximumOuterBytes,r.MaximumPayloadBytes);});
    }

    [Fact]
    public void TrustedAdmissionRegistry_RejectsOwnerSchemaHashAndSessionContradictions()
    {
        var(s,a)=Authority();var r=GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand;var payload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(s,a,GraphParticipantBindingCodecsV1.Encode(ReservationCommand())));var hash=AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,payload);var correlation=new CorrelationEnvelopeV1(TenantId.Create(),operationId:OperationId.Create());var registry=new AuthorityPayloadAdmissionRegistryV1([r]);
        ProposedAuthorityFactV1 P(OwnerSliceId owner,SchemaReferenceV1 schema,Hash256 h,ThreadId? thread=null)=>new(JournalFactId.Create(),thread,owner,schema,payload,h,correlation,new UtcInstant(1));
        Assert.Equal(AuthorityPayloadAdmissionV1.Exact,registry.Validate(s,P(OwnerSliceId.S1,r.Schema,hash),out _));Assert.Equal(AuthorityPayloadAdmissionV1.OwnerMismatch,registry.Validate(s,P(OwnerSliceId.S2,r.Schema,hash),out _));Assert.Equal(AuthorityPayloadAdmissionV1.HashMismatch,registry.Validate(s,P(OwnerSliceId.S1,r.Schema,Hash("wrong")),out _));
        var unknown=new SchemaReferenceV1(AuthoritySchemaIdentityV1.Derive(new BoundedAscii("hpd.unknown.v1")),1,0);Assert.Equal(AuthorityPayloadAdmissionV1.UnknownSchema,registry.Validate(s,P(OwnerSliceId.S1,unknown,hash),out _));var other=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());Assert.Equal(AuthorityPayloadAdmissionV1.InvalidPayload,registry.Validate(other,P(OwnerSliceId.S1,r.Schema,hash),out _));
        var threaded=P(OwnerSliceId.S1,r.Schema,hash,ThreadId.Create());Assert.Equal(AuthorityPayloadAdmissionV1.InvalidPayload,GraphParticipantBindingPayloadRegistrationsV1.ValidateEnvelope(s,threaded,r));Assert.Equal(AuthorityPayloadAdmissionV1.Exact,GraphParticipantBindingPayloadRegistrationsV1.ValidateEnvelope(s,P(OwnerSliceId.S1,r.Schema,hash),r));
    }

    [Fact]
    public void CollectionsAndFactArms_AreStrictAndOwned()
    {
        var keys=new[]{new BoundedAscii("a")}; var reservation=new GraphParticipantReservationV1(Participant(),new BoundedAscii("factory"),keys);var binding=new GraphParticipantBindingV1(Participant(),new BoundedAscii("factory"),keys);var command=new GraphParticipantReservationCommandBodyV1(Operation(),null,Runtime(),Hash("p"),Hash("t"),Hash("e"),new BoundedAscii("factory"),keys,Stamp()); keys[0]=new BoundedAscii("changed"); Assert.Equal("a",reservation.OrderedTopologyNodeKeys[0].ToString());Assert.Equal("a",binding.OrderedTopologyNodeKeys[0].ToString());Assert.Equal("a",command.OrderedTopologyNodeKeys[0].ToString());
        var bad=new GraphParticipantReservationCommandBodyV1(Operation(),null,Runtime(),Hash("p"),Hash("t"),Hash("e"),new BoundedAscii("factory"),[new BoundedAscii("b"),new BoundedAscii("a")],Stamp());
        Assert.ThrowsAny<Exception>(()=>GraphParticipantBindingCodecsV1.Encode(bad));
        var rejectedWithoutCode=new GraphParticipantReservationFactBodyV1(Operation(),Position(1),null,2,Runtime(),Hash("p"),Hash("t"),Hash("e"),null,null,Stamp());
        Assert.ThrowsAny<Exception>(()=>GraphParticipantBindingCodecsV1.Encode(rejectedWithoutCode));
    }

    [Theory]
    [InlineData(0,16384)][InlineData(1,16384)][InlineData(2,4096)][InlineData(3,16384)]
    public void AllRoutes_RejectRawDeclaredMaximumPlusOneAndDirectBodyMaximumPlusOne(int route,int maximum)
    {
        Assert.True(DecodeOuter(route,RawOuter(new byte[maximum])));Assert.False(DecodeOuter(route,RawOuter(new byte[maximum+1])));
        Assert.False(DecodeBody(route,new byte[maximum]));Assert.False(DecodeBody(route,new byte[maximum+1]));
    }

    [Fact]
    public void NestedBoundsEnumsSafeCodesAndAllReservationFactArms_AreClosed()
    {
        GraphParticipantReservationCommandBodyV1 C(BoundedAscii key,IReadOnlyList<BoundedAscii> keys)=>new(Operation(),null,Runtime(),Hash("plan"),Hash("topology"),Hash("executable"),key,keys,Stamp());var factory=new BoundedAscii("factory");Assert.ThrowsAny<Exception>(()=>GraphParticipantBindingCodecsV1.Encode(C(factory,[])));Assert.ThrowsAny<Exception>(()=>GraphParticipantBindingCodecsV1.Encode(C(factory,Enumerable.Range(0,65).Select(i=>new BoundedAscii($"k{i:00}")).ToArray())));Assert.ThrowsAny<Exception>(()=>GraphParticipantBindingCodecsV1.Encode(C(factory,[new BoundedAscii("a"),new BoundedAscii("a")])));Assert.ThrowsAny<Exception>(()=>GraphParticipantBindingCodecsV1.Encode(C(new BoundedAscii(new string('x',129)),Keys())));
        var proof=new CapacityGrantBindingProofV1(Grant(),Position(1),Position(2),0,Hash("coverage"));var bc=new GraphParticipantBindingCommandBodyV1(Operation(),Position(1),null,Graph(),Runtime(),Hash("p"),Hash("t"),Hash("e"),proof,Stamp());Assert.ThrowsAny<Exception>(()=>GraphParticipantBindingCodecsV1.Encode(bc));Assert.ThrowsAny<Exception>(()=>GraphParticipantBindingCodecsV1.Encode(bc with{CapacityGrantProof=proof with{RequiredChargeCount=4}}));
        var reservation=new GraphParticipantReservationV1(Participant(),new BoundedAscii("factory"),Keys());GraphParticipantReservationFactBodyV1 F(ushort o,GraphParticipantReservationV1? r,BoundedAscii? code)=>new(Operation(),Position(1),null,o,Runtime(),Hash("p"),Hash("t"),Hash("e"),r,code,Stamp());
        foreach(var invalid in new[]{F(0,null,new BoundedAscii("invalid-body")),F(3,null,new BoundedAscii("invalid-body")),F(1,null,null),F(1,reservation,new BoundedAscii("invalid-body")),F(2,reservation,null),F(2,null,null),F(2,null,new BoundedAscii("not-registered")),F(2,null,new BoundedAscii(new string('x',65)))})Assert.ThrowsAny<Exception>(()=>GraphParticipantBindingCodecsV1.Encode(invalid));
        var rejected=GraphParticipantBindingCodecsV1.Encode(F(2,null,new BoundedAscii("invalid-body")));Assert.True(GraphParticipantBindingCodecsV1.TryDecodeReservationFactBody(rejected,out _));
        var goodProof=new CapacityGrantBindingProofV1(Grant(),Position(1),Position(2),1,Hash("coverage"));var binding=new GraphParticipantBindingV1(Participant(),new BoundedAscii("factory"),Keys());GraphParticipantBindingFactBodyV1 BF(ushort o,GraphParticipantBindingV1? b,CapacityGrantBindingProofV1? p,BoundedAscii? code)=>new(Operation(),Position(3),Position(1),null,o,Graph(),Runtime(),Hash("p"),Hash("t"),Hash("e"),b,p,code,Stamp());
        foreach(var invalid in new[]{BF(1,null,goodProof,null),BF(1,binding,null,null),BF(1,binding,goodProof,new BoundedAscii("invalid-body")),BF(2,binding,null,null),BF(2,null,goodProof,null),BF(2,null,null,null)})Assert.ThrowsAny<Exception>(()=>GraphParticipantBindingCodecsV1.Encode(invalid));Assert.True(GraphParticipantBindingCodecsV1.TryDecodeBindingFactBody(GraphParticipantBindingCodecsV1.Encode(BF(2,null,null,new BoundedAscii("invalid-body"))),out _));
    }

    private static GraphParticipantReservationCommandBodyV1 ReservationCommand()=>new(Operation(),null,Runtime(),Hash("plan"),Hash("topology"),Hash("executable"),new BoundedAscii("factory"),Keys(),Stamp());
    private static byte[] RawOuter(byte[] body){var(s,a)=Authority();var w=new System.Formats.Cbor.CborWriter(System.Formats.Cbor.CborConformanceMode.Ctap2Canonical);w.WriteStartMap(3);w.WriteUInt64(1);SessionAuthorityStampV1Codec.Write(w,s);w.WriteUInt64(2);AuthorityVectorCodecsV1.WriteVector(w,a);w.WriteUInt64(3);w.WriteByteString(body);w.WriteEndMap();return w.Encode();}
    private static bool DecodeOuter(int r,byte[] b)=>r switch{0=>GraphParticipantBindingCodecsV1.TryDecodeReservationCommand(b,out _),1=>GraphParticipantBindingCodecsV1.TryDecodeReservationFact(b,out _),2=>GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(b,out _),_=>GraphParticipantBindingCodecsV1.TryDecodeBindingFact(b,out _)};
    private static bool DecodeBody(int r,byte[] b)=>r switch{0=>GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(b,out _),1=>GraphParticipantBindingCodecsV1.TryDecodeReservationFactBody(b,out _),2=>GraphParticipantBindingCodecsV1.TryDecodeBindingCommandBody(b,out _),_=>GraphParticipantBindingCodecsV1.TryDecodeBindingFactBody(b,out _)};
    private static IReadOnlyList<BoundedAscii> Keys()=>new[]{new BoundedAscii("input"),new BoundedAscii("output")};
    private static Hash256 Hash(string text)=>Hash256.Compute(System.Text.Encoding.ASCII.GetBytes(text));
    private static OperationId Operation()=>OperationId.FromValue(Id("000102030405060708090a0b0c0d0e0f"));
    private static RuntimeGenerationId Runtime()=>RuntimeGenerationId.FromValue(Id("101112131415161718191a1b1c1d1e1f"));
    private static GraphGenerationId Graph()=>GraphGenerationId.FromValue(Id("202122232425262728292a2b2c2d2e2f"));
    private static ParticipantId Participant()=>ParticipantId.FromValue(Id("303132333435363738393a3b3c3d3e3f"));
    private static CapacityGrantId Grant()=>CapacityGrantId.FromValue(Id("404142434445464748494a4b4c4d4e4f"));
    private static MonotonicStampV1 Stamp()=>new(ClockDomainId.FromValue(Id("505152535455565758595a5b5c5d5e5f")),BootId.FromValue(Id("606162636465666768696a6b6c6d6e6f")),123);
    private static JournalPositionV1 Position(long sequence)=>new(Authority().Session,sequence);
    private static (SessionAuthorityStampV1 Session,ExpectedAuthorityVectorV1 Vector) Authority(){var s=new SessionAuthorityStampV1(Runtime(),LiveSessionId.FromValue(Id("707172737475767778797a7b7c7d7e7f")));return(s,ExpectedAuthorityVectorV1.Create(s,[]));}
    private static StableId128 Id(string hex)=>StableId128.FromBytes(Convert.FromHexString(hex));
}
