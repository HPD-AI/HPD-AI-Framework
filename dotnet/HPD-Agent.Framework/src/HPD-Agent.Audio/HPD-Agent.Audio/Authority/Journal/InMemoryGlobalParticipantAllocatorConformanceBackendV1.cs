using System.Collections.Immutable;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal sealed class InMemoryGlobalParticipantAllocatorConformanceBackendV1 : IGlobalParticipantAllocatorClaimPortV1, IGlobalParticipantAllocatorExactRecordSnapshotReaderV1
{
    internal sealed record State(GlobalParticipantAuthorityHeadV1? Head,Hash256 IndexRoot,ulong RecordCount,ulong TotalCanonicalRecordBytes,ImmutableDictionary<JournalFactId,(GlobalParticipantAuthorityHeadV1 Head,ulong Sequence,ReadOnlyMemory<byte> Bytes)> Facts,ImmutableDictionary<ParticipantId,ParticipantIdOwnerEvidenceV1> Owners,ImmutableList<ReadOnlyMemory<byte>> Records,BoundedAscii? QuarantineCode);
    private readonly object _gate=new();private readonly GlobalParticipantAllocatorJournalId _journalId;private readonly ManualResetEventSlim? _entered,_continue;private State _state=new(null,GlobalParticipantAllocatorCodecsV1.EmptyIndexRoot(),0,0,ImmutableDictionary<JournalFactId,(GlobalParticipantAuthorityHeadV1,ulong,ReadOnlyMemory<byte>)>.Empty,ImmutableDictionary<ParticipantId,ParticipantIdOwnerEvidenceV1>.Empty,ImmutableList<ReadOnlyMemory<byte>>.Empty,null);
    internal InMemoryGlobalParticipantAllocatorConformanceBackendV1(GlobalParticipantAllocatorJournalId journalId):this(journalId,null,null){}
    internal InMemoryGlobalParticipantAllocatorConformanceBackendV1(GlobalParticipantAllocatorJournalId journalId,ManualResetEventSlim? lockEnteredSignalForTests,ManualResetEventSlim? continueSignalForTests)
    {if(!journalId.IsValid)throw new ArgumentException("A valid journal ID is required.",nameof(journalId));_journalId=journalId;_entered=lockEnteredSignalForTests;_continue=continueSignalForTests;}
    ValueTask<GlobalParticipantAllocatorClaimResultV1> IGlobalParticipantAllocatorClaimPortV1.ClaimAsync(GlobalParticipantAllocatorClaimRequestV1 request,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();if(request is null)return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(Invalid("request-null"));var memory=request.ExactCanonicalRecordBytes;
        if(memory.Length is 0 or >8192)return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(Invalid("record-size-invalid"));var bytes=memory.ToArray();if(!request.JournalId.IsValid)return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(Invalid("journal-invalid"));if(request.JournalId!=_journalId)return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(Invalid("journal-mismatch"));
        if(!GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(bytes,out var outer)||outer is null||!GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer.BodyBytes.ToArray(),out var body)||body is null)return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(Invalid("record-wire-invalid"));
        var fingerprint=GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(body.Source);var fact=GlobalParticipantAllocatorFactIdsV1.Fact(outer.SourceSession.LiveSessionId,body.OperationId,fingerprint);if(fact!=request.FactId)return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(Invalid("fact-id-mismatch"));var participant=GlobalParticipantAllocatorFactIdsV1.Participant(outer.SourceSession.LiveSessionId,body.OperationId,fingerprint);if(participant!=body.ParticipantId)return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(Invalid("participant-id-mismatch"));
        lock(_gate)
        {
            Signal();
            var s=_state;
            if(s.QuarantineCode is{} q)return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(new GlobalParticipantAllocatorClaimResultV1.Quarantined(q));
            if(s.Facts.TryGetValue(fact,out var prior))
            {
                if(prior.Bytes.Span.SequenceEqual(bytes))return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(new GlobalParticipantAllocatorClaimResultV1.AlreadyCommitted(prior.Head,prior.Sequence,prior.Bytes.ToArray()));
                return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(Quarantine(s,"fact-id-bytes-conflict"));
            }
            if(request.ExpectedHead!=s.Head)return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(new GlobalParticipantAllocatorClaimResultV1.HeadConflict(s.Head));
            if(s.RecordCount>=65536)return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(new GlobalParticipantAllocatorClaimResultV1.LifetimeExhausted(s.RecordCount,s.TotalCanonicalRecordBytes));
            if(body.AssignedPosition.JournalId!=_journalId||body.AssignedPosition.Sequence!=s.RecordCount+1||body.PriorHead!=s.Head)return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(Invalid("head-invalid"));
            var proof=body.OwnerProof;var owner=s.Owners.TryGetValue(body.ParticipantId,out var found)?found:null;
            if(proof.Head!=s.Head||proof.IndexRoot!=s.IndexRoot||proof.RecordCount!=s.RecordCount||proof.State!=(owner is null?1:2)||!OwnerEqual(proof.Owner,owner)||!GlobalParticipantAllocatorCodecsV1.VerifyProof(body.ParticipantId,proof.State,proof.Owner,proof.IndexRoot,proof.ProofPathBytes))return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(Invalid("head-invalid"));
            var transition=GlobalParticipantAllocatorFoldV1.EvaluateTransition(owner,body.Outcome);
            if(body.Source.LiveSessionId!=outer.SourceSession.LiveSessionId||body.Source.SourceFactPosition.Session!=outer.SourceSession||!transition.Valid)return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(Invalid("head-invalid"));
            var total=checked(s.TotalCanonicalRecordBytes+(ulong)bytes.Length);if(total>536870912||transition.InsertOwnerAfterHash&&s.Owners.Count>=65536)return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(new GlobalParticipantAllocatorClaimResultV1.LifetimeExhausted(s.RecordCount,s.TotalCanonicalRecordBytes));
            var hash=GlobalParticipantAllocatorFactIdsV1.RecordHash(body.AssignedPosition,s.Head,fact,outer.SourceSession,outer.SourceExpectedAuthority,outer.BodyBytes);var head=new GlobalParticipantAuthorityHeadV1(body.AssignedPosition,hash);
            var owners=s.Owners;var root=s.IndexRoot;if(transition.InsertOwnerAfterHash){var nextOwner=new ParticipantIdOwnerEvidenceV1(body.ParticipantId,body.Source.LiveSessionId,body.OperationId,fingerprint,head);owners=owners.Add(body.ParticipantId,nextOwner);root=SparseIndex.ApplyOwner(body.ParticipantId,nextOwner,proof.ProofPathBytes);}
            var owned=bytes.ToArray();
            var result=new GlobalParticipantAllocatorClaimResultV1.Committed(head,s.RecordCount+1,owned.ToArray());
            var next=new State(head,root,s.RecordCount+1,total,s.Facts.Add(fact,(head,s.RecordCount+1,owned)),owners,s.Records.Add(owned),null);
            if(!CheapStateValid(s)||!CheapDeltaValid(s,next,fact,owned,transition.InsertOwnerAfterHash,body.ParticipantId,root))return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(Quarantine(s,"retained-state-invalid"));
            _state=next;
            return ValueTask.FromResult<GlobalParticipantAllocatorClaimResultV1>(result);
        }
    }
    GlobalParticipantAllocatorSnapshotResultV1 IGlobalParticipantAllocatorExactRecordSnapshotReaderV1.ReadExactSnapshot(){lock(_gate){var s=_state;if(s.QuarantineCode is{}q)return new GlobalParticipantAllocatorSnapshotResultV1.Quarantined(q);if(!AuthenticateSnapshot(s)){var code=new BoundedAscii("retained-state-invalid");var result=new GlobalParticipantAllocatorSnapshotResultV1.Quarantined(code);var next=s with{QuarantineCode=code};_state=next;return result;}return new GlobalParticipantAllocatorSnapshotResultV1.Current(new(_journalId,s.Head,s.RecordCount,s.TotalCanonicalRecordBytes,s.Records.Select(x=>(ReadOnlyMemory<byte>)x.ToArray()).ToArray()));}}
    private void Signal(){try{_entered?.Set();_continue?.Wait();}catch(ObjectDisposedException){}}
    private static GlobalParticipantAllocatorClaimResultV1.InvalidRecord Invalid(string code)=>new(new BoundedAscii(code));
    private GlobalParticipantAllocatorClaimResultV1.Quarantined Quarantine(State s,string code){var c=new BoundedAscii(code);var result=new GlobalParticipantAllocatorClaimResultV1.Quarantined(c);var next=s with{QuarantineCode=c};_state=next;return result;}
    private bool AuthenticateSnapshot(State s)
    {
        if(!CheapStateValid(s))return false;var fold=new GlobalParticipantAllocatorFoldAccumulatorV1(_journalId);
        for(var index=0;index<s.Records.Count;index++)
        {
            var record=s.Records[index];
            if(!GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(record,out var outer)||outer is null||!GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer.BodyBytes.ToArray(),out var body)||body is null)return false;
            var fingerprint=GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(body.Source);
            var fact=GlobalParticipantAllocatorFactIdsV1.Fact(outer.SourceSession.LiveSessionId,body.OperationId,fingerprint);
            if(fold.Apply(record) is not GlobalParticipantAllocatorFoldApplyResultV1.Accepted accepted||!s.Facts.TryGetValue(fact,out var retained)||retained.Head!=accepted.Head||retained.Sequence!=(ulong)index+1||!retained.Bytes.Span.SequenceEqual(record.Span))return false;
        }
        var completed=fold.Complete();
        if(completed is not GlobalParticipantAllocatorFoldResultV1.Current current||current.Snapshot.Head!=s.Head||current.Snapshot.RecordCount!=s.RecordCount||current.Snapshot.TotalCanonicalRecordBytes!=s.TotalCanonicalRecordBytes)return false;
        if(current.Snapshot.IndexRoot!=s.IndexRoot||current.Snapshot.RetainedOwnerCount!=s.Owners.Count)return false;
        foreach(var pair in s.Owners){var proof=current.Snapshot.Query(pair.Key);if(!OwnerEqual(proof.Owner,pair.Value))return false;}
        return true;
    }
    private static bool CheapStateValid(State s)=>s.RecordCount==(ulong)s.Records.Count&&s.RecordCount==(ulong)s.Facts.Count&&(s.RecordCount==0)==(s.Head is null)&&s.TotalCanonicalRecordBytes<=536870912&&s.Owners.Count<=65536;
    private static bool CheapDeltaValid(State prior,State next,JournalFactId fact,ReadOnlyMemory<byte> bytes,bool inserted,ParticipantId participant,Hash256 expectedRoot)
    {
        if(next.RecordCount!=prior.RecordCount+1||next.Records.Count!=prior.Records.Count+1||next.Facts.Count!=prior.Facts.Count+1||next.Owners.Count!=prior.Owners.Count+(inserted?1:0)||next.TotalCanonicalRecordBytes!=prior.TotalCanonicalRecordBytes+(ulong)bytes.Length||next.IndexRoot!=expectedRoot||!next.Records[^1].Span.SequenceEqual(bytes.Span))return false;
        if(!next.Facts.TryGetValue(fact,out var retained)||retained.Head!=next.Head||retained.Sequence!=next.RecordCount||!retained.Bytes.Span.SequenceEqual(bytes.Span))return false;
        return inserted?next.Owners.ContainsKey(participant)&&next.IndexRoot!=prior.IndexRoot:next.Owners==prior.Owners&&next.IndexRoot==prior.IndexRoot;
    }
    private static bool OwnerEqual(ParticipantIdOwnerEvidenceV1? x,ParticipantIdOwnerEvidenceV1? y)=>x is null?y is null:y is not null&&GlobalParticipantAllocatorCodecsV1.Encode(x).AsSpan().SequenceEqual(GlobalParticipantAllocatorCodecsV1.Encode(y));
}
