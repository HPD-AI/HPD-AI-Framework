using HPD.Agent.Authority;
namespace HPD.Agent.LiveAudio.Contracts.Tests.Authority;
public sealed class GlobalParticipantAllocatorClaimPortV1Tests
{
    [Fact] public void RequestOwnsCallerBytes(){var b=new byte[]{1};var r=new GlobalParticipantAllocatorClaimRequestV1(default,null,b,default);b[0]=2;Assert.Equal(1,r.ExactCanonicalRecordBytes.Span[0]);}
}
