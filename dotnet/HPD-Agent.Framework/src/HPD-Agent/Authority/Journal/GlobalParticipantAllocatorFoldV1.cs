using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Agent.Authority;

internal abstract record GlobalParticipantAllocatorFoldApplyResultV1
{
    private GlobalParticipantAllocatorFoldApplyResultV1() { }
    internal sealed record Accepted(ulong Sequence, GlobalParticipantAuthorityHeadV1 Head, ulong RecordCount, ulong TotalCanonicalRecordBytes) : GlobalParticipantAllocatorFoldApplyResultV1;
    internal sealed record InvalidHistory(BoundedAscii SafeCode, ulong LastVerifiedSequence) : GlobalParticipantAllocatorFoldApplyResultV1;
}

internal abstract record GlobalParticipantAllocatorFoldResultV1
{
    private GlobalParticipantAllocatorFoldResultV1() { }
    internal sealed record Current(GlobalParticipantAllocatorCompletedFoldV1 Snapshot) : GlobalParticipantAllocatorFoldResultV1;
    internal sealed record InvalidHistory(BoundedAscii SafeCode, ulong LastVerifiedSequence) : GlobalParticipantAllocatorFoldResultV1;
}

internal readonly record struct GlobalParticipantAllocatorTransitionEvaluationV1(bool Valid,bool InsertOwnerAfterHash,bool OwnershipUnchanged);

internal sealed class GlobalParticipantAllocatorCompletedFoldV1
{
    private readonly SparseIndex.Snapshot _index;
    internal GlobalParticipantAllocatorCompletedFoldV1(GlobalParticipantAllocatorJournalId journalId, GlobalParticipantAuthorityHeadV1? head, Hash256 indexRoot, ulong recordCount, ulong totalCanonicalRecordBytes, IReadOnlyDictionary<ParticipantId, ParticipantIdOwnerEvidenceV1> owners)
    { JournalId=journalId;Head=head;IndexRoot=indexRoot;RecordCount=recordCount;TotalCanonicalRecordBytes=totalCanonicalRecordBytes;_index=SparseIndex.Seal(owners,indexRoot); }
    internal GlobalParticipantAllocatorJournalId JournalId { get; }
    internal GlobalParticipantAuthorityHeadV1? Head { get; }
    internal Hash256 IndexRoot { get; }
    internal ulong RecordCount { get; }
    internal ulong TotalCanonicalRecordBytes { get; }
    internal int RetainedStructuralNodeCount=>_index.RetainedStructuralNodeCount;
    internal int RetainedOwnerCount=>_index.RetainedOwnerCount;
    internal long RetainedCanonicalEvidenceBytes=>_index.RetainedCanonicalEvidenceBytes;
    internal long ConservativeRetainedStorageUpperBoundBytes=>checked(256L+RetainedStructuralNodeCount*64L+RetainedOwnerCount*96L+RetainedCanonicalEvidenceBytes);
    internal int SealBuildCount=>1;
    internal int QueryBuildCount=>0;
    internal ParticipantIdOwnerProofV1 Query(ParticipantId participantId)
    {
        if (!participantId.IsValid) throw new ArgumentException("A valid participant ID is required.", nameof(participantId));
        var proof=_index.Query(participantId);
        if(!GlobalParticipantAllocatorCodecsV1.VerifyProof(participantId,proof.Owner is null?(ushort)1:(ushort)2,proof.Owner,proof.Root,proof.Path))throw new InvalidOperationException($"Compressed proof mismatch owner={proof.Owner is not null} root={proof.Root==IndexRoot}.");
        return new ParticipantIdOwnerProofV1(participantId,Head,proof.Owner is null?(ushort)1:(ushort)2,proof.Owner,proof.Root,proof.Path,RecordCount);
    }
}

internal static class GlobalParticipantAllocatorFoldV1
{
    internal static GlobalParticipantAllocatorFoldAccumulatorV1 Create(GlobalParticipantAllocatorJournalId journalId) => new(journalId);
    internal static GlobalParticipantAllocatorTransitionEvaluationV1 EvaluateTransition(ParticipantIdOwnerEvidenceV1? authenticatedExistingOwner,ParticipantIdClaimOutcomeV1 outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Kind switch
        {
            1 when authenticatedExistingOwner is null&&outcome.ExistingOwnerPosition is null&&outcome.SafeCode is null=>new(true,true,false),
            2 when authenticatedExistingOwner is not null&&outcome.ExistingOwnerPosition==authenticatedExistingOwner.ClaimHead.Position&&outcome.SafeCode?.ToString()=="participant-id-owned"=>new(true,false,true),
            3 when outcome.ExistingOwnerPosition is null&&outcome.SafeCode?.ToString() is "session-authority-stale" or "source-evidence-invalid" or "owner-proof-invalid" or "participant-id-derivation-mismatch" or "invalid-body"=>new(true,false,true),
            _=>new(false,false,false)
        };
    }
}

internal sealed class GlobalParticipantAllocatorFoldAccumulatorV1
{
    private readonly GlobalParticipantAllocatorJournalId _journalId;
    private readonly Dictionary<ParticipantId,ParticipantIdOwnerEvidenceV1> _owners=[];
    private readonly HashSet<JournalFactId> _facts=[];
    private GlobalParticipantAuthorityHeadV1? _head;
    private Hash256 _root=GlobalParticipantAllocatorCodecsV1.EmptyIndexRoot();
    private ulong _count,_bytes;
    private GlobalParticipantAllocatorFoldApplyResultV1.InvalidHistory? _invalid;
    private GlobalParticipantAllocatorFoldResultV1? _complete;

    internal GlobalParticipantAllocatorFoldAccumulatorV1(GlobalParticipantAllocatorJournalId journalId)
    { if(!journalId.IsValid)throw new ArgumentException("A valid journal ID is required.",nameof(journalId));_journalId=journalId; }

    internal GlobalParticipantAllocatorFoldApplyResultV1 Apply(ReadOnlyMemory<byte> exactOuterBytes)
    {
        if(_complete is not null)throw new InvalidOperationException("The fold is complete.");
        if(_invalid is not null)return _invalid;
        if(exactOuterBytes.Length is 0 or >8192)return Fail("record-size-invalid");
        if(!GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(exactOuterBytes,out var outer)||outer is null||!GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer.BodyBytes.ToArray(),out var body)||body is null)return Fail("record-wire-invalid");
        if(body.AssignedPosition.JournalId!=_journalId)return Fail("journal-mismatch");
        if(body.AssignedPosition.Sequence!=_count+1)return Fail("sequence-invalid");
        if(body.PriorHead!=_head)return Fail("prior-head-mismatch");
        var proof=body.OwnerProof;
        if(proof.Head!=_head||proof.IndexRoot!=_root||proof.RecordCount!=_count)return Fail("proof-pin-mismatch");
        ParticipantIdOwnerEvidenceV1? currentOwner=_owners.TryGetValue(body.ParticipantId,out var found)?found:null;
        if(proof.State!=(currentOwner is null?1:2)||!OwnerEqual(proof.Owner,currentOwner)||!GlobalParticipantAllocatorCodecsV1.VerifyProof(body.ParticipantId,proof.State,proof.Owner,proof.IndexRoot,proof.ProofPathBytes))return Fail("proof-invalid");
        var fingerprint=GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(body.Source);
        var participant=GlobalParticipantAllocatorFactIdsV1.Participant(outer.SourceSession.LiveSessionId,body.OperationId,fingerprint);
        if(body.Source.LiveSessionId!=outer.SourceSession.LiveSessionId||body.Source.SourceFactPosition.Session!=outer.SourceSession||participant!=body.ParticipantId)return Fail("source-identity-invalid");
        var fact=GlobalParticipantAllocatorFactIdsV1.Fact(outer.SourceSession.LiveSessionId,body.OperationId,fingerprint);
        if(!_facts.Add(fact))return Fail("fact-id-duplicate");
        var transition=GlobalParticipantAllocatorFoldV1.EvaluateTransition(currentOwner,body.Outcome);if(!transition.Valid){_facts.Remove(fact);return Fail("outcome-invalid");}
        var nextBytes=checked(_bytes+(ulong)exactOuterBytes.Length);
        if(_count>=65536||nextBytes>536870912||body.Outcome.Kind==1&&_owners.Count>=65536){_facts.Remove(fact);return Fail("lifetime-limit-invalid");}
        var hash=GlobalParticipantAllocatorFactIdsV1.RecordHash(body.AssignedPosition,_head,fact,outer.SourceSession,outer.SourceExpectedAuthority,outer.BodyBytes);
        var head=new GlobalParticipantAuthorityHeadV1(body.AssignedPosition,hash);
        if(transition.InsertOwnerAfterHash){var owner=new ParticipantIdOwnerEvidenceV1(body.ParticipantId,body.Source.LiveSessionId,body.OperationId,fingerprint,head);_owners.Add(body.ParticipantId,owner);_root=SparseIndex.ApplyOwner(body.ParticipantId,owner,proof.ProofPathBytes);}
        _head=head;_count++;_bytes=nextBytes;
        return new GlobalParticipantAllocatorFoldApplyResultV1.Accepted(_count,head,_count,_bytes);
    }

    internal GlobalParticipantAllocatorFoldResultV1 Complete()
    {
        if(_complete is not null)return _complete;
        if(_invalid is not null)return _complete=new GlobalParticipantAllocatorFoldResultV1.InvalidHistory(_invalid.SafeCode,_invalid.LastVerifiedSequence);
        var owners=new Dictionary<ParticipantId,ParticipantIdOwnerEvidenceV1>(_owners);
        return _complete=new GlobalParticipantAllocatorFoldResultV1.Current(new GlobalParticipantAllocatorCompletedFoldV1(_journalId,_head,_root,_count,_bytes,owners));
    }

    internal ParticipantIdOwnerProofV1 Query(ParticipantId participantId) => _complete is GlobalParticipantAllocatorFoldResultV1.Current current
        ? current.Snapshot.Query(participantId) : throw new InvalidOperationException("Only a completed current fold can answer ownership queries.");

    private GlobalParticipantAllocatorFoldApplyResultV1.InvalidHistory Fail(string code)=>_invalid=new(new BoundedAscii(code),_count);
    private static bool OwnerEqual(ParticipantIdOwnerEvidenceV1? x,ParticipantIdOwnerEvidenceV1? y)=>x is null?y is null:y is not null&&GlobalParticipantAllocatorCodecsV1.Encode(x).AsSpan().SequenceEqual(GlobalParticipantAllocatorCodecsV1.Encode(y));
}

internal static class SparseIndex
{
    private static readonly byte[] Absent=Encoding.UTF8.GetBytes("hpd-s1-global-participant-absent-leaf-v1\0"),Owner=Encoding.UTF8.GetBytes("hpd-s1-global-participant-owner-leaf-v1\0"),Node=Encoding.UTF8.GetBytes("hpd-s1-global-participant-owner-node-v1\0");
    private static readonly byte[][] Defaults=CreateDefaults();
    internal sealed record Proof(ParticipantIdOwnerEvidenceV1? Owner,Hash256 Root,byte[] Path);
    internal sealed class Snapshot
    {
        private readonly int _root;private readonly Hash256 _hash;private readonly int[] _bit,_left,_right,_evidenceOffset,_evidenceLength;private readonly UInt128[] _key;private readonly byte[] _hashes,_evidence;
        private Snapshot(int root,Hash256 hash,int[] bit,int[] left,int[] right,UInt128[] key,byte[] hashes,int[] evidenceOffset,int[] evidenceLength,byte[] evidence){_root=root;_hash=hash;_bit=bit;_left=left;_right=right;_key=key;_hashes=hashes;_evidenceOffset=evidenceOffset;_evidenceLength=evidenceLength;_evidence=evidence;}
        internal static Snapshot Create(int root,Hash256 hash,List<int> bit,List<int> left,List<int> right,List<UInt128> key,List<byte[]> hashes,List<int> offsets,List<int> lengths,byte[] evidence)=>new(root,hash,bit.ToArray(),left.ToArray(),right.ToArray(),key.ToArray(),hashes.SelectMany(x=>x).ToArray(),offsets.ToArray(),lengths.ToArray(),evidence);
        internal int RetainedStructuralNodeCount=>_bit.Length;internal int RetainedOwnerCount=>_evidenceLength.Count(x=>x>0);internal long RetainedCanonicalEvidenceBytes=>_evidence.LongLength;
        internal Proof Query(ParticipantId target){var key=Key(target);var path=new byte[4096];for(var d=0;d<128;d++)Defaults[d].CopyTo(path,d*32);ParticipantIdOwnerEvidenceV1? owner=null;if(_root>=0)Collect(_root,key,path,ref owner);return new(owner,_hash,path);}
        private void Collect(int node,UInt128 target,byte[] path,ref ParticipantIdOwnerEvidenceV1? owner)
        {
            var level=_bit[node]<0?0:_bit[node]+1;if(_key[node]!=target){var differing=HighestDifferingBit(_key[node],target);if(differing>=level){Lift(HashAt(node),level,differing,_key[node]).CopyTo(path,differing*32);return;}}
            if(_bit[node]<0){if(!GlobalParticipantAllocatorCodecsV1.TryDecodeEvidence(_evidence.AsMemory(_evidenceOffset[node],_evidenceLength[node]),out owner)||owner is null)throw new InvalidOperationException("Retained owner evidence is invalid.");return;}
            var bit=_bit[node];var choose=((target>>bit)&1)!=0;var child=choose?_right[node]:_left[node];var sibling=choose?_left[node]:_right[node];var siblingLevel=_bit[sibling]<0?0:_bit[sibling]+1;Lift(HashAt(sibling),siblingLevel,bit,_key[sibling]).CopyTo(path,bit*32);Collect(child,target,path,ref owner);
        }
        private byte[] HashAt(int node)=>_hashes.AsSpan(node*32,32).ToArray();
    }
    internal static Snapshot Seal(IReadOnlyDictionary<ParticipantId,ParticipantIdOwnerEvidenceV1> owners,Hash256 expectedRoot)
    {
        var source=owners.Select(p=>(Key:Key(p.Key),Evidence:GlobalParticipantAllocatorCodecsV1.Encode(p.Value),Hash:OwnerLeaf(p.Key,p.Value))).OrderBy(x=>x.Key).ToArray();var bits=new List<int>();var left=new List<int>();var right=new List<int>();var keys=new List<UInt128>();var hashes=new List<byte[]>();var offsets=new List<int>();var lengths=new List<int>();var evidence=source.SelectMany(x=>x.Evidence).ToArray();var o=0;foreach(var leaf in source){bits.Add(-1);left.Add(-1);right.Add(-1);keys.Add(leaf.Key);hashes.Add(leaf.Hash);offsets.Add(o);lengths.Add(leaf.Evidence.Length);o+=leaf.Evidence.Length;}int Build(int start,int count,int bit){if(count==1)return start;var d=bit;while(d>=0&&(((source[start].Key>>d)&1)==((source[start+count-1].Key>>d)&1)))d--;var split=start;while(split<start+count&&((source[split].Key>>d)&1)==0)split++;var l=Build(start,split-start,d-1);var r=Build(split,start+count-split,d-1);var lh=Lift(hashes[l],bits[l]<0?0:bits[l]+1,d,keys[l]);var rh=Lift(hashes[r],bits[r]<0?0:bits[r]+1,d,keys[r]);var index=bits.Count;bits.Add(d);left.Add(l);right.Add(r);keys.Add(keys[l]);hashes.Add(HashNode(d+1,lh,rh));offsets.Add(0);lengths.Add(0);return index;}var root=source.Length==0?-1:Build(0,source.Length,127);var calculated=Hash256.FromBytes(root<0?Defaults[128]:Lift(hashes[root],bits[root]<0?0:bits[root]+1,128,keys[root]));if(calculated!=expectedRoot)throw new InvalidOperationException("Compressed index root mismatch.");return Snapshot.Create(root,expectedRoot,bits,left,right,keys,hashes,offsets,lengths,evidence);
    }
    internal static Hash256 ApplyOwner(ParticipantId id,ParticipantIdOwnerEvidenceV1 owner,ReadOnlySpan<byte> authenticatedPath){if(authenticatedPath.Length!=4096)throw new ArgumentException("A complete proof path is required.");var key=Key(id);var current=OwnerLeaf(id,owner);for(var depth=1;depth<=128;depth++){var sibling=authenticatedPath.Slice((depth-1)*32,32).ToArray();current=(key&1)==0?HashNode(depth,current,sibling):HashNode(depth,sibling,current);key>>=1;}return Hash256.FromBytes(current);}
    private static UInt128 Key(ParticipantId id){Span<byte>b=stackalloc byte[16];if(!id.TryWriteBytes(b))throw new ArgumentException("Invalid participant.");return BinaryPrimitives.ReadUInt128BigEndian(b);}
    private static int HighestDifferingBit(UInt128 x,UInt128 y){var v=x^y;for(var i=127;i>=0;i--)if(((v>>i)&1)!=0)return i;return -1;}
    private static byte[] Lift(byte[] hash,int fromLevel,int toLevel,UInt128 key)=>Lift(hash,fromLevel,toLevel,null,key);
    private static byte[] Lift(byte[] hash,int fromLevel,int toLevel,byte[]? path,UInt128 key){var current=hash;for(var level=fromLevel+1;level<=toLevel;level++){var sibling=Defaults[level-1];current=((key>>(level-1))&1)==0?HashNode(level,current,sibling):HashNode(level,sibling,current);}return current;}
    private static byte[] OwnerLeaf(ParticipantId id,ParticipantIdOwnerEvidenceV1 owner){Span<byte>b=stackalloc byte[16];id.TryWriteBytes(b);var e=GlobalParticipantAllocatorCodecsV1.Encode(owner);var p=new byte[Owner.Length+16+e.Length];Owner.CopyTo(p,0);b.CopyTo(p.AsSpan(Owner.Length));e.CopyTo(p,Owner.Length+16);return SHA256.HashData(p);}
    private static byte[] HashNode(int depth,byte[] left,byte[] right){var p=new byte[Node.Length+65];Node.CopyTo(p,0);p[Node.Length]=(byte)depth;left.CopyTo(p,Node.Length+1);right.CopyTo(p,Node.Length+33);return SHA256.HashData(p);}
    private static byte[][] CreateDefaults(){var d=new byte[129][];d[0]=SHA256.HashData(Absent);for(var i=1;i<=128;i++)d[i]=HashNode(i,d[i-1],d[i-1]);return d;}
}
