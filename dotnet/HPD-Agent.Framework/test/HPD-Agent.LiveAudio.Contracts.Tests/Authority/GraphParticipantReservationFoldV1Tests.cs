using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests.Authority;

public sealed class GraphParticipantReservationFoldV1Tests
{
    [Fact]
    public void Create_rejects_invalid_session() =>
        Assert.Throws<ArgumentException>(() => GraphParticipantReservationFoldV1.Create(default));

    [Fact]
    public void Completed_empty_fold_queries_not_found()
    {
        var session = Session();
        var fold = GraphParticipantReservationFoldV1.Create(session);
        var completed = fold.Complete();
        Assert.Equal(session, completed.Session);
        Assert.Equal(0, completed.SnapshotThrough);
        Assert.IsType<GraphParticipantReservationFoldV1.NotFound>(fold.Query(Operation()));
        Assert.Equal(completed, fold.Complete());
    }

    [Fact]
    public void Fold_closed_result_inventory_is_exact()
    {
        Assert.Equal(2, new[] { typeof(GraphParticipantReservationFoldV1.Accepted), typeof(GraphParticipantReservationFoldV1.InvalidHistory) }.Distinct().Count());
        Assert.Equal(4, new[] { typeof(GraphParticipantReservationFoldV1.NotFound), typeof(GraphParticipantReservationFoldV1.CommandOnly), typeof(GraphParticipantReservationFoldV1.AppliedReservation), typeof(GraphParticipantReservationFoldV1.RejectedReservation) }.Distinct().Count());
        Assert.True(typeof(GraphParticipantReservationFoldV1.Completed).IsSealed);
    }

    [Fact]
    public void Query_requires_completion_and_completion_is_idempotent()
    {
        var fold = GraphParticipantReservationFoldV1.Create(Session());
        Assert.Throws<InvalidOperationException>(() => fold.Query(Operation()));
        var first = fold.Complete();
        Assert.Equal(first, fold.Complete());
        Assert.IsType<GraphParticipantReservationFoldV1.NotFound>(fold.Query(Operation()));
    }

    private static SessionAuthorityStampV1 Session() => new(
        RuntimeGenerationId.FromValue(StableId128.FromBytes(Convert.FromHexString("101112131415161718191A1B1C1D1E1F"))),
        LiveSessionId.FromValue(StableId128.FromBytes(Convert.FromHexString("707172737475767778797A7B7C7D7E7F"))));
    private static OperationId Operation() => OperationId.FromValue(StableId128.FromBytes(Convert.FromHexString("000102030405060708090A0B0C0D0E0F")));
}
