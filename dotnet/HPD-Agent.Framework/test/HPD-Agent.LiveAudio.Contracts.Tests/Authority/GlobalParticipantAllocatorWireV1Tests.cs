using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests.Authority;

public sealed class GlobalParticipantAllocatorWireV1Tests
{
    [Fact]
    public void CoreOuterAndBody_RoundTripCanonicalOwnedBytes()
    {
        var fixture = Fixture();
        var bodyBytes = GlobalParticipantAllocatorCodecsV1.Encode(fixture.Body);
        Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(bodyBytes, out var body));
        Assert.Equal(bodyBytes, GlobalParticipantAllocatorCodecsV1.Encode(body!));
        var source = bodyBytes.ToArray();
        var outer = new GlobalParticipantClaimRecordV1(fixture.Session, fixture.Vector, source);
        source[0] ^= 0xff;
        Assert.NotEqual(source[0], outer.Body[0]);
        var outerBytes = GlobalParticipantAllocatorCodecsV1.Encode(outer);
        Assert.InRange(outerBytes.Length, 1, 8192);
        Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(outerBytes, out var decoded));
        Assert.Equal(outerBytes, GlobalParticipantAllocatorCodecsV1.Encode(decoded!));
    }

    [Fact]
    public void CoreCodecs_RejectMalformedAndBounds()
    {
        Assert.False(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(new byte[8193], out _));
        Assert.False(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(new byte[7169], out _));
        Assert.False(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(Convert.FromHexString("BF01F6FF"), out _));
        Assert.Throws<ArgumentException>(() => new ParticipantIdOwnerProofV1(
            Participant(), null, 1, null, Hash("root"), new byte[4095], 0));
    }

    [Fact]
    public void FactIdentityAndRecordHash_AreDeterministicAndSeparated()
    {
        var fixture = Fixture();
        var fingerprint = GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(fixture.Source);
        var participant = GlobalParticipantAllocatorFactIdsV1.Participant(fixture.Session.LiveSessionId, fixture.Body.OperationId, fingerprint);
        var fact = GlobalParticipantAllocatorFactIdsV1.Fact(fixture.Session.LiveSessionId, fixture.Body.OperationId, fingerprint);
        Assert.True(participant.IsValid);
        Assert.True(fact.IsValid);
        Assert.NotEqual(participant.ToString(), fact.ToString());
        var body = GlobalParticipantAllocatorCodecsV1.Encode(fixture.Body);
        var hash = GlobalParticipantAllocatorFactIdsV1.RecordHash(fixture.Body.AssignedPosition, null, fact,
            fixture.Session, fixture.Vector, body);
        Assert.Equal(hash, GlobalParticipantAllocatorFactIdsV1.RecordHash(fixture.Body.AssignedPosition, null, fact,
            fixture.Session, fixture.Vector, body));
    }

    [Fact]
    public void EmptyPage_RoundTripsAndHasFrozenDeterministicHash()
    {
        var page = new GlobalParticipantPageV1(JournalId(), null, GlobalParticipantPageCodecV1.DefaultIndexRoot,
            1, null, GlobalParticipantPageCodecV1.EncodeRecordsField(Array.Empty<ReadOnlyMemory<byte>>()),
            1, 1, 0, 0);
        var bytes = GlobalParticipantPageCodecV1.Encode(page);
        Assert.True(GlobalParticipantPageCodecV1.TryDecode(bytes, out var decoded));
        Assert.Equal(bytes, GlobalParticipantPageCodecV1.Encode(decoded!));
        Assert.Equal(GlobalParticipantPageCodecV1.ComputePageHash(page), GlobalParticipantPageCodecV1.ComputePageHash(decoded!));
    }

    [Fact]
    public void PageFraming_RejectsRecordAndPageBounds()
    {
        Assert.ThrowsAny<ArgumentException>(() => GlobalParticipantPageCodecV1.EncodeRecordsField(
            [new ReadOnlyMemory<byte>(new byte[8193])]));
        var malformed = new byte[] { 0, 1, 0, 0, 0, 2, 0xa0 };
        Assert.False(GlobalParticipantPageCodecV1.TryDecodeRecordsField(malformed, out _));
        Assert.ThrowsAny<ArgumentException>(() => new GlobalParticipantPageV1(JournalId(), null,
            GlobalParticipantPageCodecV1.DefaultIndexRoot, 1, null, new byte[] { 0, 0 }, 2, 1, 0, 0));
    }

    [Fact]
    public void AllNestedSchemas_RoundTripStrictCanonicalBytes()
    {
        var f=Fixture();
        var positionBytes=GlobalParticipantAllocatorCodecsV1.Encode(f.Body.AssignedPosition);Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodePosition(positionBytes,out var position));Assert.Equal(positionBytes,GlobalParticipantAllocatorCodecsV1.Encode(position));
        var sourceBytes=GlobalParticipantAllocatorCodecsV1.Encode(f.Source);Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeSource(sourceBytes,out var source));Assert.Equal(sourceBytes,GlobalParticipantAllocatorCodecsV1.Encode(source!));
        var head=new GlobalParticipantAuthorityHeadV1(f.Body.AssignedPosition,Hash("head"));var headBytes=GlobalParticipantAllocatorCodecsV1.Encode(head);Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeHead(headBytes,out var decodedHead));Assert.Equal(headBytes,GlobalParticipantAllocatorCodecsV1.Encode(decodedHead));
        var evidence=new ParticipantIdOwnerEvidenceV1(f.Body.ParticipantId,f.Session.LiveSessionId,f.Body.OperationId,Hash("fingerprint"),head);var evidenceBytes=GlobalParticipantAllocatorCodecsV1.Encode(evidence);Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeEvidence(evidenceBytes,out var decodedEvidence));Assert.Equal(evidenceBytes,GlobalParticipantAllocatorCodecsV1.Encode(decodedEvidence!));
        var proofBytes=GlobalParticipantAllocatorCodecsV1.Encode(f.Body.OwnerProof);Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeProof(proofBytes,out var proof));Assert.Equal(proofBytes,GlobalParticipantAllocatorCodecsV1.Encode(proof!));
        var outcomeBytes=GlobalParticipantAllocatorCodecsV1.Encode(f.Body.Outcome);Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOutcome(outcomeBytes,out var outcome));Assert.Equal(outcomeBytes,GlobalParticipantAllocatorCodecsV1.Encode(outcome!));
    }

    [Fact]
    public void ClosedOutcomesAndLifetimeBounds_RejectBeforeEncoding()
    {
        Assert.Throws<ArgumentException>(()=>new ParticipantIdClaimOutcomeV1(3,null,null));
        Assert.Throws<ArgumentException>(()=>new ParticipantIdClaimOutcomeV1(3,null,new BoundedAscii("unknown")));
        Assert.Throws<ArgumentException>(()=>new GlobalParticipantAuthorityPositionV1(JournalId(),65537));
        _=new ParticipantIdClaimOutcomeV1(3,null,new BoundedAscii("invalid-body"));
    }

    [Fact]
    public void PageRecords_RequireExactClaimOuterNotArbitraryCanonicalCbor()
    {
        Assert.Throws<ArgumentException>(()=>GlobalParticipantPageCodecV1.EncodeRecordsField([new ReadOnlyMemory<byte>(new byte[]{0xa0})]));
        var f=Fixture();var body=GlobalParticipantAllocatorCodecsV1.Encode(f.Body);var outer=GlobalParticipantAllocatorCodecsV1.Encode(new GlobalParticipantClaimRecordV1(f.Session,f.Vector,body));
        var framed=GlobalParticipantPageCodecV1.EncodeRecordsField([new ReadOnlyMemory<byte>(outer)]);Assert.True(GlobalParticipantPageCodecV1.TryDecodeRecordsField(framed,out var records));Assert.Single(records);
    }

    [Fact]
    public void FrozenWireAndIdentityGoldens_AreIndependentlyFramed()
    {
        var f=Fixture();var fingerprint=GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(f.Source);
        Span<byte>s=stackalloc byte[16];Span<byte>o=stackalloc byte[16];Span<byte>fp=stackalloc byte[32];f.Session.LiveSessionId.TryWriteBytes(s);f.Body.OperationId.TryWriteBytes(o);fingerprint.TryWriteBytes(fp);
        var domain=Encoding.UTF8.GetBytes("hpd-s1-global-participant-claim-record-fact-id-v1\0");var preimage=new byte[domain.Length+64];domain.CopyTo(preimage,0);s.CopyTo(preimage.AsSpan(domain.Length));o.CopyTo(preimage.AsSpan(domain.Length+16));fp.CopyTo(preimage.AsSpan(domain.Length+32));
        var expected=SHA256.HashData(preimage).AsSpan(0,16);Span<byte>actual=stackalloc byte[16];GlobalParticipantAllocatorFactIdsV1.Fact(f.Session.LiveSessionId,f.Body.OperationId,fingerprint).TryWriteBytes(actual);Assert.Equal(expected.ToArray(),actual.ToArray());
        var head=new GlobalParticipantAuthorityHeadV1(f.Body.AssignedPosition,Hash("head"));
        var evidence=new ParticipantIdOwnerEvidenceV1(f.Body.ParticipantId,f.Session.LiveSessionId,f.Body.OperationId,Hash("fingerprint"),head);
        var body=GlobalParticipantAllocatorCodecsV1.Encode(f.Body);
        var outer=GlobalParticipantAllocatorCodecsV1.Encode(new GlobalParticipantClaimRecordV1(f.Session,f.Vector,body));
        var page=GlobalParticipantPageCodecV1.Encode(new GlobalParticipantPageV1(JournalId(),null,GlobalParticipantPageCodecV1.DefaultIndexRoot,1,null,GlobalParticipantPageCodecV1.EncodeRecordsField(Array.Empty<ReadOnlyMemory<byte>>()),1,1,0,0));
        var encodings=new[]{outer,body,GlobalParticipantAllocatorCodecsV1.Encode(f.Body.AssignedPosition),GlobalParticipantAllocatorCodecsV1.Encode(head),GlobalParticipantAllocatorCodecsV1.Encode(f.Source),GlobalParticipantAllocatorCodecsV1.Encode(evidence),GlobalParticipantAllocatorCodecsV1.Encode(f.Body.OwnerProof),GlobalParticipantAllocatorCodecsV1.Encode(f.Body.Outcome),page};
        var digest=SHA256.HashData(encodings.SelectMany(x=>x).ToArray());Assert.Equal("43C3DF7C2CFDA0EAC78DBA505566DDA91A1BEA510CC1079A41A341FFF545F81B",Convert.ToHexString(digest));
    }

    [Fact]
    public void OwnerProof_UsesParticipantBitsLeastSignificantFirst()
    {
        var f=Fixture();var head=new GlobalParticipantAuthorityHeadV1(new GlobalParticipantAuthorityPositionV1(JournalId(),1),Hash("claim"));var evidence=new ParticipantIdOwnerEvidenceV1(f.Body.ParticipantId,f.Session.LiveSessionId,f.Body.OperationId,Hash("fingerprint"),head);
        var path=Enumerable.Range(0,128).SelectMany(i=>SHA256.HashData(BitConverter.GetBytes(i))).ToArray();var root=IndependentProofRoot(f.Body.ParticipantId,2,evidence,path);
        _=new ParticipantIdOwnerProofV1(f.Body.ParticipantId,head,2,evidence,root,path,1);
        var changed=ParticipantId.FromValue(Id(5));Assert.Throws<ArgumentException>(()=>new ParticipantIdOwnerProofV1(changed,head,2,new ParticipantIdOwnerEvidenceV1(changed,f.Session.LiveSessionId,f.Body.OperationId,Hash("fingerprint"),head),root,path,1));
    }

    [Fact]
    public void ClaimRegistration_AdmitsOnlyExactS1UnthreadedEnvelope()
    {
        var f=Fixture();var body=GlobalParticipantAllocatorCodecsV1.Encode(f.Body);var payload=GlobalParticipantAllocatorCodecsV1.Encode(new GlobalParticipantClaimRecordV1(f.Session,f.Vector,body));var r=GlobalParticipantAllocatorPayloadRegistrationV1.ClaimRecord;Assert.Equal((ushort)42,GlobalParticipantAllocatorPayloadRegistrationV1.Discriminator);Assert.Equal(OwnerSliceId.S1,r.Owner);
        var hash=AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,payload);var correlation=new CorrelationEnvelopeV1(TenantId.Create(),operationId:OperationId.Create());
        ProposedAuthorityFactV1 P(OwnerSliceId owner,SchemaReferenceV1 schema,Hash256 h,ThreadId? thread=null)=>new(JournalFactId.Create(),thread,owner,schema,payload,h,correlation,new UtcInstant(1));
        Assert.Equal(AuthorityPayloadAdmissionV1.Exact,GlobalParticipantAllocatorPayloadRegistrationV1.ValidateEnvelope(f.Session,P(OwnerSliceId.S1,r.Schema,hash)));Assert.Equal(AuthorityPayloadAdmissionV1.OwnerMismatch,GlobalParticipantAllocatorPayloadRegistrationV1.ValidateEnvelope(f.Session,P(OwnerSliceId.S2,r.Schema,hash)));Assert.Equal(AuthorityPayloadAdmissionV1.HashMismatch,GlobalParticipantAllocatorPayloadRegistrationV1.ValidateEnvelope(f.Session,P(OwnerSliceId.S1,r.Schema,Hash("wrong"))));Assert.Equal(AuthorityPayloadAdmissionV1.InvalidPayload,GlobalParticipantAllocatorPayloadRegistrationV1.ValidateEnvelope(f.Session,P(OwnerSliceId.S1,r.Schema,hash,ThreadId.Create())));
    }

    [Fact]
    public void PageFramingAndHash_GoldensCoverEmptyOneAndChain()
    {
        Assert.Equal(new byte[]{0,0},GlobalParticipantPageCodecV1.EncodeRecordsField(Array.Empty<ReadOnlyMemory<byte>>()));var f=Fixture();var record=GlobalParticipantAllocatorCodecsV1.Encode(new GlobalParticipantClaimRecordV1(f.Session,f.Vector,GlobalParticipantAllocatorCodecsV1.Encode(f.Body)));var field=GlobalParticipantPageCodecV1.EncodeRecordsField([record]);Assert.Equal((ushort)1,BinaryPrimitives.ReadUInt16BigEndian(field));Assert.Equal((uint)record.Length,BinaryPrimitives.ReadUInt32BigEndian(field.AsSpan(2,4)));Assert.Equal(record,field.AsSpan(6).ToArray());
        var head=new GlobalParticipantAuthorityHeadV1(new GlobalParticipantAuthorityPositionV1(JournalId(),1),Hash("record"));var first=new GlobalParticipantPageV1(JournalId(),head,GlobalParticipantPageCodecV1.DefaultIndexRoot,1,null,field,0,2,1,(ulong)record.Length);var firstHash=GlobalParticipantPageCodecV1.ComputePageHash(first);var second=new GlobalParticipantPageV1(JournalId(),head,GlobalParticipantPageCodecV1.DefaultIndexRoot,2,firstHash,field,1,2,1,(ulong)record.Length);Assert.NotEqual(firstHash,GlobalParticipantPageCodecV1.ComputePageHash(second));
        var tooMany=new byte[2];BinaryPrimitives.WriteUInt16BigEndian(tooMany,257);Assert.False(GlobalParticipantPageCodecV1.TryDecodeRecordsField(tooMany,out _));
    }

    private static Hash256 IndependentProofRoot(ParticipantId participant,ushort state,ParticipantIdOwnerEvidenceV1 owner,ReadOnlySpan<byte> path)
    {
        Span<byte>id=stackalloc byte[16];participant.TryWriteBytes(id);byte[] current;if(state==1)current=SHA256.HashData(Encoding.UTF8.GetBytes("hpd-s1-global-participant-absent-leaf-v1\0"));else{var evidence=GlobalParticipantAllocatorCodecsV1.Encode(owner);var domain=Encoding.UTF8.GetBytes("hpd-s1-global-participant-owner-leaf-v1\0");var p=new byte[domain.Length+16+evidence.Length];domain.CopyTo(p,0);id.CopyTo(p.AsSpan(domain.Length));evidence.CopyTo(p,domain.Length+16);current=SHA256.HashData(p);}var node=Encoding.UTF8.GetBytes("hpd-s1-global-participant-owner-node-v1\0");for(var depth=1;depth<=128;depth++){var sibling=path.Slice((depth-1)*32,32);var p=new byte[node.Length+65];node.CopyTo(p,0);p[node.Length]=(byte)depth;var bit=(id[15-(depth-1)/8]>>((depth-1)%8))&1;if(bit==0){current.CopyTo(p,node.Length+1);sibling.CopyTo(p.AsSpan(node.Length+33));}else{sibling.CopyTo(p.AsSpan(node.Length+1));current.CopyTo(p,node.Length+33);}current=SHA256.HashData(p);}return Hash256.FromBytes(current);
    }

    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Vector,
        GlobalParticipantAllocationSourceV1 Source, GlobalParticipantClaimRecordBodyV1 Body) Fixture()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
        var vector = ExpectedAuthorityVectorV1.Create(session, []);
        var source = new GlobalParticipantAllocationSourceV1(session.LiveSessionId, new JournalPositionV1(session, 1),
            Hash("outer"), Hash("body"));
        var participant = Participant();
        var position = new GlobalParticipantAuthorityPositionV1(JournalId(), 1);
        var proof = new ParticipantIdOwnerProofV1(participant, null, 1, null,
            GlobalParticipantPageCodecV1.DefaultIndexRoot, GlobalParticipantAllocatorCodecsV1.CreateEmptyProofPath(), 0);
        var outcome = new ParticipantIdClaimOutcomeV1(1, null, null);
        var stamp = new MonotonicStampV1(ClockDomainId.FromValue(Id(8)), BootId.FromValue(Id(9)), 10);
        return (session, vector, source, new GlobalParticipantClaimRecordBodyV1(
            OperationId.FromValue(Id(3)), source, participant, null, proof, outcome, position, stamp));
    }

    private static ParticipantId Participant() => ParticipantId.FromValue(Id(4));
    private static GlobalParticipantAllocatorJournalId JournalId() => GlobalParticipantAllocatorJournalId.FromValue(Id(5));
    private static StableId128 Id(byte value) => StableId128.FromBytes(Enumerable.Repeat(value, 16).ToArray());
    private static Hash256 Hash(string value) => Hash256.FromBytes(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
