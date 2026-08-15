using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SessionLifecycleFactIdsV1Tests
{
    [Fact]
    public void Derivation_MatchesCheckedInNetworkOrderGoldens()
    {
        var session = new SessionAuthorityStampV1(
            RuntimeGenerationId.FromValue(StableId128.FromBytes(Enumerable.Range(1, 16).Select(static value => (byte)value).ToArray())),
            LiveSessionId.FromValue(StableId128.FromBytes(Enumerable.Range(17, 16).Select(static value => (byte)value).ToArray())));
        var operation = OperationId.FromValue(
            StableId128.FromBytes(Enumerable.Range(33, 16).Select(static value => (byte)value).ToArray()));

        Assert.Equal("fct:0BYH69VMR206925V9VSTPMTV8Y", SessionLifecycleCommandFactIdV1.Derive(session, operation).ToString());
        Assert.Equal("fct:0C2AASXY8A1E0K6NZS0K3SYJNY", SessionLifecycleResultFactIdV1.Derive(
            new JournalPositionV1(session, 17)).ToString());
    }

    [Fact]
    public void CommandIdentity_IsStableAndScopesRuntimeSessionAndOperation()
    {
        var session = Session();
        var operation = OperationId.Create();
        var expected = SessionLifecycleCommandFactIdV1.Derive(session, operation);

        Assert.Equal(expected, SessionLifecycleCommandFactIdV1.Derive(session, operation));
        Assert.NotEqual(expected, SessionLifecycleCommandFactIdV1.Derive(Session(), operation));
        Assert.NotEqual(expected, SessionLifecycleCommandFactIdV1.Derive(session, OperationId.Create()));
    }

    [Fact]
    public void ResultIdentity_IsStableAndScopesRuntimeSessionAndCommandPosition()
    {
        var session = Session();
        var position = new JournalPositionV1(session, 17);
        var expected = SessionLifecycleResultFactIdV1.Derive(position);

        Assert.Equal(expected, SessionLifecycleResultFactIdV1.Derive(position));
        Assert.NotEqual(expected, SessionLifecycleResultFactIdV1.Derive(new JournalPositionV1(session, 18)));
        Assert.NotEqual(expected, SessionLifecycleResultFactIdV1.Derive(new JournalPositionV1(Session(), 17)));
    }

    [Fact]
    public void Derivation_RejectsInvalidAuthorityInputs()
    {
        Assert.Throws<ArgumentException>(() => SessionLifecycleCommandFactIdV1.Derive(default, OperationId.Create()));
        Assert.Throws<ArgumentException>(() => SessionLifecycleCommandFactIdV1.Derive(Session(), default));
        Assert.Throws<ArgumentException>(() => SessionLifecycleResultFactIdV1.Derive(default));
    }

    private static SessionAuthorityStampV1 Session() =>
        new(RuntimeGenerationId.Create(), LiveSessionId.Create());
}
