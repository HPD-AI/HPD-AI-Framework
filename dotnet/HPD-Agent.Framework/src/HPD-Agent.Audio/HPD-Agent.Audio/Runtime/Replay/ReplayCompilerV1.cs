using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Replay;

internal interface IAuthorityReplaySourceV1
{
    ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,CancellationToken cancellationToken=default);
}

internal sealed class AuthorityJournalReplaySourceV1(IAuthorityJournalV1 journal):IAuthorityReplaySourceV1
{
    private readonly IAuthorityJournalV1 _journal=journal??throw new ArgumentNullException(nameof(journal));
    public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,CancellationToken cancellationToken=default)=>_journal.ReadAsync(request,cancellationToken);
}

internal sealed class ReplayCompilationV1
{
    private readonly ReadOnlyCollection<AuthorityFactEnvelopeV1> _facts;
    internal ReplayCompilationV1(SessionAuthorityStampV1 session,long through,IEnumerable<AuthorityFactEnvelopeV1> facts,Hash256 fingerprint)
    {var copy=facts?.ToArray()??throw new ArgumentNullException(nameof(facts));if(!session.IsValid||through<0||copy.Any(x=>x.Position.Session!=session)||copy.Select(x=>x.Position.Sequence).Where(x=>x>0).DefaultIfEmpty().Max()>through||fingerprint==default)throw new ArgumentException("Replay compilation is invalid.");Session=session;Through=through;_facts=Array.AsReadOnly(copy);Fingerprint=fingerprint;}
    internal SessionAuthorityStampV1 Session{get;}internal long Through{get;}internal IReadOnlyList<AuthorityFactEnvelopeV1> Facts=>_facts;internal Hash256 Fingerprint{get;}
}
internal abstract record ReplayCompileResultV1
{
    private ReplayCompileResultV1(){}internal sealed record Compiled(ReplayCompilationV1 Compilation):ReplayCompileResultV1;internal sealed record InvalidHistory(BoundedAscii SafeCode):ReplayCompileResultV1;internal sealed record Unavailable(BoundedAscii SafeCode):ReplayCompileResultV1;
}

internal static class ReplayCompilerV1
{
    private const ushort MaximumFacts=64;private const uint MaximumBytes=ProposedAuthorityFactV1.MaximumPayloadBytes;private const int MaximumPages=16;
    internal static async ValueTask<ReplayCompileResultV1> CompileAsync(IAuthorityReplaySourceV1 source,SessionAuthorityStampV1 session,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(source);if(!session.IsValid)throw new ArgumentException("A session is required.",nameof(session));
        var facts=new List<AuthorityFactEnvelopeV1>();long cursor=0;long? through=null;
        for(var page=0;page<MaximumPages;page++)
        {
            ReadAuthorityRangeResultV1 result;try{result=await source.ReadAsync(new(session,cursor,through??long.MaxValue,MaximumFacts,MaximumBytes),cancellationToken).ConfigureAwait(false);}catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){throw;}catch(Exception){return new ReplayCompileResultV1.Unavailable(new BoundedAscii("replay-read-exception"));}
            if(result is ReadAuthorityRangeResultV1.StoreUnavailable unavailable)return new ReplayCompileResultV1.Unavailable(unavailable.SafeCode);
            if(result is not ReadAuthorityRangeResultV1.Batch batch||batch.Session!=session||batch.AfterExclusive!=cursor||batch.Facts.Count>MaximumFacts)return Invalid("replay-page-invalid");
            through??=batch.SnapshotThrough;if(through!=batch.SnapshotThrough)return Invalid("replay-snapshot-drift");
            foreach(var fact in batch.Facts){if(fact.Position.Session!=session||fact.Position.Sequence<=cursor||fact.PayloadHash!=AuthorityPayloadHashV1.Compute(new BoundedAscii(Token(fact.PayloadSchema)),fact.PayloadSchema,fact.PayloadMemory.Span))return Invalid("replay-fact-invalid");cursor=fact.Position.Sequence;facts.Add(fact);}
            if(!batch.HasMore){if(cursor!=through)return Invalid("replay-snapshot-incomplete");return new ReplayCompileResultV1.Compiled(new(session,through.Value,facts,Fingerprint(session,through.Value,facts)));}
            if(batch.Facts.Count==0)return Invalid("replay-empty-continuation");
        }
        return new ReplayCompileResultV1.Unavailable(new BoundedAscii("replay-page-bound"));
    }
    private static ReplayCompileResultV1.InvalidHistory Invalid(string code)=>new(new BoundedAscii(code));
    private static string Token(SchemaReferenceV1 schema)=>AuthoritySchemaLedgerV1.Schemas.Single(row=>row.Contains("|1.0|",StringComparison.Ordinal)&&row.Split('|')[0] is var token&&AuthoritySchemaIdentityV1.Derive(new BoundedAscii(token))==schema.SchemaId).Split('|')[0];
    private static Hash256 Fingerprint(SessionAuthorityStampV1 session,long through,IReadOnlyList<AuthorityFactEnvelopeV1> facts)
    {using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);hash.AppendData("hpd-s10-replay-compilation-v1\0"u8);hash.AppendData(SessionAuthorityStampV1Codec.Encode(session));Span<byte>n=stackalloc byte[8];Span<byte>id=stackalloc byte[16];Span<byte>h=stackalloc byte[32];BinaryPrimitives.WriteInt64BigEndian(n,through);hash.AppendData(n);foreach(var fact in facts){BinaryPrimitives.WriteInt64BigEndian(n,fact.Position.Sequence);hash.AppendData(n);fact.FactId.TryWriteBytes(id);hash.AppendData(id);fact.PayloadHash.TryWriteBytes(h);hash.AppendData(h);BinaryPrimitives.WriteUInt16BigEndian(n,(ushort)fact.Owner);hash.AppendData(n[..2]);BinaryPrimitives.WriteInt32BigEndian(n, fact.Payload.Count);hash.AppendData(n[..4]);hash.AppendData(fact.PayloadMemory.Span);}return Hash256.FromBytes(hash.GetHashAndReset());}
}
