using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityCanonicalCborV1Tests
{
    [Fact]
    public void CorrelationProposalAndBatch_RoundTripStrictlyWithDistinctHashes()
    {
        var correlation=new CorrelationEnvelopeV1(TenantId.Create(),PrincipalId.Create(),SessionId.Create(),ThreadId.Create(),ParticipantId.Create(),OperationId.Create());
        var session=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());
        var token=new BoundedAscii(SessionAuthorityStampV1Codec.SchemaId);var schema=new SchemaReferenceV1(AuthoritySchemaIdentityV1.Derive(token),1,0);var payload=SessionAuthorityStampV1Codec.Encode(session);
        var proposal=new ProposedAuthorityFactV1(JournalFactId.Create(),correlation.ThreadId,OwnerSliceId.S1,schema,payload,
            AuthorityPayloadHashV1.Compute(token,schema,payload),correlation,new UtcInstant(1));
        var batch=new AppendAuthorityBatchV1(session,0,[new ThreadExpectedHeadV1(correlation.ThreadId!.Value,1,0)],[proposal],4096);

        var correlationBytes=AuthorityCanonicalCborV1.Encode(correlation);
        var proposalBytes=AuthorityCanonicalCborV1.Encode(proposal);
        var batchBytes=AuthorityCanonicalCborV1.EncodeAppendBatch(batch);
        Assert.True(AuthorityCanonicalCborV1.TryDecodeCorrelation(correlationBytes,out var decodedCorrelation));
        Assert.True(AuthorityCanonicalCborV1.TryDecodeProposal(proposalBytes,out var decodedProposal));
        Assert.True(AuthorityCanonicalCborV1.TryDecodeAppendBatch(batchBytes,out var decodedBatch));
        Assert.Equal(correlation,decodedCorrelation);
        Assert.Equal(proposalBytes,AuthorityCanonicalCborV1.Encode(decodedProposal!));
        Assert.Equal(batchBytes,AuthorityCanonicalCborV1.EncodeAppendBatch(decodedBatch!));

        var hashes=new[]{AuthorityCanonicalCborV1.ComputeHash(correlation),AuthorityCanonicalCborV1.ComputeHash(proposal),AuthorityCanonicalCborV1.ComputeHash(batch)};
        Assert.Equal(3,hashes.Distinct().Count());
        Assert.False(AuthorityCanonicalCborV1.TryDecodeCorrelation(correlationBytes.Concat(new byte[]{0}).ToArray(),out _));
        Assert.False(AuthorityCanonicalCborV1.TryDecodeProposal(proposalBytes.Concat(new byte[]{0}).ToArray(),out _));
        Assert.False(AuthorityCanonicalCborV1.TryDecodeAppendBatch(batchBytes.Concat(new byte[]{0}).ToArray(),out _));
    }
}
