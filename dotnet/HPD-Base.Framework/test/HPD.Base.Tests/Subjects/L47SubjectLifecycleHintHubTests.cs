using System.Threading.Channels;

namespace HPD.Base.Tests.Subjects;

public sealed class L47SubjectLifecycleHintHubTests
{
    [Fact]
    public async Task Hints_are_delivered_only_to_the_exact_contract_and_scope()
    {
        var hub = new BaseSubjectLifecycleHintHub();
        using BaseSubjectLifecycleHintHub.Lease matching = hub.Subscribe(
            "example.user", 1, Scope("tenant-a"));
        using BaseSubjectLifecycleHintHub.Lease wrongScope = hub.Subscribe(
            "example.user", 1, Scope("tenant-b"));
        using BaseSubjectLifecycleHintHub.Lease wrongContract = hub.Subscribe(
            "example.other", 1, Scope("tenant-a"));

        hub.Publish(Evidence(1, "tenant-a"));

        BaseSubjectLifecycleCommitEvidence delivered = await matching.Reader.ReadAsync();
        Assert.Equal("subject-1", delivered.SubjectId);
        Assert.False(wrongScope.Reader.TryRead(out _));
        Assert.False(wrongContract.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Overflow_closes_only_the_slow_hint_channel_with_replacement_required()
    {
        var hub = new BaseSubjectLifecycleHintHub();
        using BaseSubjectLifecycleHintHub.Lease slow = hub.Subscribe(
            "example.user", 1, Scope("tenant-a"));
        using BaseSubjectLifecycleHintHub.Lease healthy = hub.Subscribe(
            "example.user", 1, Scope("tenant-a"));

        for (int sequence = 1; sequence <= 32; sequence++)
        {
            hub.Publish(Evidence(sequence, "tenant-a"));
            Assert.Equal(sequence, (await healthy.Reader.ReadAsync()).SubjectSequence);
        }

        hub.Publish(Evidence(33, "tenant-a"));
        Assert.Equal(33, (await healthy.Reader.ReadAsync()).SubjectSequence);

        for (int sequence = 1; sequence <= 32; sequence++)
            Assert.Equal(sequence, (await slow.Reader.ReadAsync()).SubjectSequence);

        ChannelClosedException closed = await Assert.ThrowsAsync<ChannelClosedException>(
            async () => await slow.Reader.ReadAsync());
        BaseRealtimeFeedException failure = Assert.IsType<BaseRealtimeFeedException>(closed.InnerException);
        Assert.Equal(BaseRealtimeErrorCodes.ReplacementRequired, failure.Code);
    }

    private static BaseOwnedSubjectScopeEvidence Scope(string tenant) => new()
    {
        Kind = BaseSubjectScopeKind.Tenant,
        Value = tenant,
    };

    private static BaseSubjectLifecycleCommitEvidence Evidence(long sequence, string tenant) => new()
    {
        ContractId = "example.user",
        ContractVersion = 1,
        SubjectId = $"subject-{sequence}",
        Kind = sequence == 1
            ? BaseSubjectLifecycleMutationKind.Create
            : BaseSubjectLifecycleMutationKind.Preserve,
        AuthorityEpoch = new BaseSubjectAuthorityEpoch(Enumerable.Repeat((byte)0x11, 16).ToArray()),
        Incarnation = BaseSubjectIncarnation.Create(1),
        SubjectSequence = sequence,
        ContractStateGeneration = 1,
        DeliveryEpoch = 1,
        Scope = Scope(tenant),
        PreviousState = sequence == 1 ? null : BaseSubjectLifecycleState.Active,
        ResultingState = BaseSubjectLifecycleState.Active,
        CommitPosition = new BaseMutationJournalPosition(sequence),
    };
}
