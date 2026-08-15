using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class CaptureGrantCodecsV1Tests
{
    [Fact]
    public void Command_roundtrips_canonical_terms()
    {
        var fixture = new Fixture();
        var encoded = CaptureGrantCodecsV1.EncodeCommand(fixture.Command);
        Assert.True(CaptureGrantCodecsV1.TryDecodeCommand(encoded, out var decoded));
        Assert.Equal(fixture.Command, decoded);
        Assert.Equal(encoded, CaptureGrantCodecsV1.EncodeCommand(decoded!));
    }

    [Fact]
    public void Fact_roundtrips_and_binds_source_position()
    {
        var fixture = new Fixture();
        var fact = new CaptureGrantCommittedV1(fixture.Body.OperationId, new JournalPositionV1(fixture.Session, 7),
            fixture.Authority, CaptureGrantCommitDispositionV1.Granted);
        var encoded = CaptureGrantCodecsV1.EncodeFact(fact);
        Assert.True(CaptureGrantCodecsV1.TryDecodeFact(encoded, out var decoded));
        Assert.Equal(fact, decoded);
        Assert.Equal(7, decoded!.SourcePosition.Sequence);
    }

    [Fact]
    public void Noncanonical_or_trailing_payload_is_rejected()
    {
        var fixture = new Fixture();
        var command = CaptureGrantCodecsV1.EncodeCommand(fixture.Command);
        Assert.False(CaptureGrantCodecsV1.TryDecodeCommand(command.Concat(new byte[] { 0 }).ToArray(), out _));
        command[0] = 0xbf;
        Assert.False(CaptureGrantCodecsV1.TryDecodeCommand(command, out _));
    }

    [Fact]
    public void Command_and_fact_hashes_are_schema_separated()
    {
        var fixture = new Fixture();
        var fact = new CaptureGrantCommittedV1(fixture.Body.OperationId, new JournalPositionV1(fixture.Session, 7),
            fixture.Authority, CaptureGrantCommitDispositionV1.Granted);
        Assert.NotEqual(CaptureGrantCodecsV1.CommandHash(fixture.Command), CaptureGrantCodecsV1.FactHash(fact));
    }

    private sealed class Fixture
    {
        internal Fixture()
        {
            Session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            Authority = ExpectedAuthorityVectorV1.Create(Session,
                [new AuthorityAxisValueV1.Privacy(PrivacyGenerationId.Create())]);
            Body = new CaptureAuthorizationBodyV1(OperationId.Create(), CaptureGrantId.Create(), AuthorizationId.Create(),
                Hash256.FromBytes(Enumerable.Repeat((byte)1, 32).ToArray()),
                Hash256.FromBytes(Enumerable.Repeat((byte)2, 32).ToArray()), new UtcInstant(900));
            Command = new CaptureAuthorizationCommandV1(Session, Authority, Body);
        }
        internal SessionAuthorityStampV1 Session { get; }
        internal ExpectedAuthorityVectorV1 Authority { get; }
        internal CaptureAuthorizationBodyV1 Body { get; }
        internal CaptureAuthorizationCommandV1 Command { get; }
    }
}
