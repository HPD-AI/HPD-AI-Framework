namespace HPD.Agent.Authority;

internal sealed class GlobalParticipantClaimRecordV1
{
    private readonly byte[] _body;
    internal GlobalParticipantClaimRecordV1(SessionAuthorityStampV1 sourceSession, ExpectedAuthorityVectorV1 sourceExpectedAuthority, ReadOnlySpan<byte> body)
    {
        if (!sourceSession.IsValid || sourceExpectedAuthority is null || sourceExpectedAuthority.Session != sourceSession || body.Length is < 1 or > GlobalParticipantAllocatorCodecsV1.MaximumBodyBytes)
            throw new ArgumentException("Invalid global participant claim envelope.");
        SourceSession=sourceSession; SourceExpectedAuthority=sourceExpectedAuthority; _body=body.ToArray(); Body=Array.AsReadOnly(_body);
    }
    internal SessionAuthorityStampV1 SourceSession{get;} internal ExpectedAuthorityVectorV1 SourceExpectedAuthority{get;}
    internal IReadOnlyList<byte> Body{get;} internal ReadOnlySpan<byte> BodyBytes=>_body;
}

internal readonly record struct GlobalParticipantAuthorityPositionV1
{
    internal GlobalParticipantAuthorityPositionV1(GlobalParticipantAllocatorJournalId journalId, ulong sequence)
    { if(!journalId.IsValid||sequence is 0 or >65536)throw new ArgumentException("A valid global position is required.");JournalId=journalId;Sequence=sequence; }
    internal GlobalParticipantAllocatorJournalId JournalId{get;} internal ulong Sequence{get;}
    internal bool IsValid=>JournalId.IsValid&&Sequence>0;
}

internal readonly record struct GlobalParticipantAuthorityHeadV1
{
    internal GlobalParticipantAuthorityHeadV1(GlobalParticipantAuthorityPositionV1 position,Hash256 recordHash)
    { Span<byte>b=stackalloc byte[32];if(!position.IsValid||!recordHash.TryWriteBytes(b))throw new ArgumentException("A valid global head is required.");Position=position;RecordHash=recordHash; }
    internal GlobalParticipantAuthorityPositionV1 Position{get;} internal Hash256 RecordHash{get;}
}

internal sealed record GlobalParticipantAllocationSourceV1
{
    internal GlobalParticipantAllocationSourceV1(LiveSessionId liveSessionId, JournalPositionV1 sourceFactPosition, Hash256 sourceOuterIntegrityHash, Hash256 sourceBodyHash)
    { Span<byte>b=stackalloc byte[32];if(!liveSessionId.IsValid||!sourceFactPosition.IsValid||sourceFactPosition.Session.LiveSessionId!=liveSessionId||!sourceOuterIntegrityHash.TryWriteBytes(b)||!sourceBodyHash.TryWriteBytes(b))throw new ArgumentException("Invalid allocation source.");LiveSessionId=liveSessionId;SourceFactPosition=sourceFactPosition;SourceOuterIntegrityHash=sourceOuterIntegrityHash;SourceBodyHash=sourceBodyHash; }
    internal LiveSessionId LiveSessionId{get;} internal JournalPositionV1 SourceFactPosition{get;} internal Hash256 SourceOuterIntegrityHash{get;} internal Hash256 SourceBodyHash{get;}
}
internal sealed record ParticipantIdOwnerEvidenceV1
{
    internal ParticipantIdOwnerEvidenceV1(ParticipantId participantId,LiveSessionId liveSessionId,OperationId operationId,Hash256 sourceFingerprint,GlobalParticipantAuthorityHeadV1 claimHead)
    {Span<byte>b=stackalloc byte[32];if(!participantId.IsValid||!liveSessionId.IsValid||!operationId.IsValid||!sourceFingerprint.TryWriteBytes(b)||!claimHead.Position.IsValid)throw new ArgumentException("Invalid owner evidence.");ParticipantId=participantId;LiveSessionId=liveSessionId;OperationId=operationId;SourceFingerprint=sourceFingerprint;ClaimHead=claimHead;}
    internal ParticipantId ParticipantId{get;}internal LiveSessionId LiveSessionId{get;}internal OperationId OperationId{get;}internal Hash256 SourceFingerprint{get;}internal GlobalParticipantAuthorityHeadV1 ClaimHead{get;}
}

internal sealed class ParticipantIdOwnerProofV1
{
    private readonly byte[] _proofPath;
    internal ParticipantIdOwnerProofV1(ParticipantId participantId,GlobalParticipantAuthorityHeadV1? head,ushort state,ParticipantIdOwnerEvidenceV1? owner,Hash256 indexRoot,ReadOnlySpan<byte> proofPath,ulong recordCount)
    { Span<byte>b=stackalloc byte[32];if(!participantId.IsValid||state is <1 or >2||proofPath.Length!=4096||recordCount>65536||(state==1)!=(owner is null)||!indexRoot.TryWriteBytes(b)||(head is null)!=(recordCount==0)||(head is{}h&&h.Position.Sequence!=recordCount)||(owner is not null&&(owner.ParticipantId!=participantId||head is null||owner.ClaimHead.Position.JournalId!=head.Value.Position.JournalId||owner.ClaimHead.Position.Sequence>head.Value.Position.Sequence))||!GlobalParticipantAllocatorCodecsV1.VerifyProof(participantId,state,owner,indexRoot,proofPath)||(recordCount==0&&!GlobalParticipantAllocatorCodecsV1.IsExactEmptyProof(indexRoot,proofPath)))throw new ArgumentException("Invalid owner proof.");ParticipantId=participantId;Head=head;State=state;Owner=owner;IndexRoot=indexRoot;_proofPath=proofPath.ToArray();ProofPath=Array.AsReadOnly(_proofPath);RecordCount=recordCount; }
    internal ParticipantId ParticipantId{get;} internal GlobalParticipantAuthorityHeadV1? Head{get;} internal ushort State{get;} internal ParticipantIdOwnerEvidenceV1? Owner{get;} internal Hash256 IndexRoot{get;} internal IReadOnlyList<byte> ProofPath{get;} internal ReadOnlySpan<byte> ProofPathBytes=>_proofPath; internal ulong RecordCount{get;}
}

internal sealed record ParticipantIdClaimOutcomeV1
{
    internal ParticipantIdClaimOutcomeV1(ushort kind,GlobalParticipantAuthorityPositionV1? existingOwnerPosition,BoundedAscii? safeCode){if(kind is <1 or >3||(kind==1&&(existingOwnerPosition is not null||safeCode is not null))||(kind==2&&(existingOwnerPosition is null||safeCode?.ToString()!="participant-id-owned"))||(kind==3&&(existingOwnerPosition is not null||safeCode is null||safeCode.ToString() is not ("session-authority-stale" or "source-evidence-invalid" or "owner-proof-invalid" or "participant-id-derivation-mismatch" or "invalid-body"))))throw new ArgumentException("Invalid claim outcome.");Kind=kind;ExistingOwnerPosition=existingOwnerPosition;SafeCode=safeCode;}internal ushort Kind{get;}internal GlobalParticipantAuthorityPositionV1? ExistingOwnerPosition{get;}internal BoundedAscii? SafeCode{get;}
}
internal sealed record GlobalParticipantClaimRecordBodyV1
{
    internal GlobalParticipantClaimRecordBodyV1(OperationId operationId,GlobalParticipantAllocationSourceV1 source,ParticipantId participantId,GlobalParticipantAuthorityHeadV1? priorHead,ParticipantIdOwnerProofV1 ownerProof,ParticipantIdClaimOutcomeV1 outcome,GlobalParticipantAuthorityPositionV1 assignedPosition,MonotonicStampV1 observedAt){if(!operationId.IsValid||source is null||!participantId.IsValid||ownerProof is null||ownerProof.ParticipantId!=participantId||ownerProof.Head!=priorHead||outcome is null||!assignedPosition.IsValid||!observedAt.IsValid||(priorHead is null?assignedPosition.Sequence!=1:priorHead.Value.Position.JournalId!=assignedPosition.JournalId||priorHead.Value.Position.Sequence+1!=assignedPosition.Sequence))throw new ArgumentException("Invalid claim body.");OperationId=operationId;Source=source;ParticipantId=participantId;PriorHead=priorHead;OwnerProof=ownerProof;Outcome=outcome;AssignedPosition=assignedPosition;ObservedAt=observedAt;}internal OperationId OperationId{get;}internal GlobalParticipantAllocationSourceV1 Source{get;}internal ParticipantId ParticipantId{get;}internal GlobalParticipantAuthorityHeadV1? PriorHead{get;}internal ParticipantIdOwnerProofV1 OwnerProof{get;}internal ParticipantIdClaimOutcomeV1 Outcome{get;}internal GlobalParticipantAuthorityPositionV1 AssignedPosition{get;}internal MonotonicStampV1 ObservedAt{get;}
}
