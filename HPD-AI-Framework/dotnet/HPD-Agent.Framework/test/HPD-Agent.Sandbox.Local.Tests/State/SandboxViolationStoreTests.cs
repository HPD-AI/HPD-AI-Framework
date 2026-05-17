using FluentAssertions;
using HPD.Sandbox.Local.State;
using Xunit;

namespace HPD.Sandbox.Local.Tests.State;

public sealed class SandboxViolationStoreTests
{
    [Fact]
    public void Add_AppendsViolationAndIncrementsCounts()
    {
        var store = new SandboxViolationStore(capacity: 3);
        var violation = CreateViolation("one");

        store.Add(violation);

        store.Count.Should().Be(1);
        store.TotalCount.Should().Be(1);
        store.Get().Should().ContainSingle().Which.Should().Be(violation);
    }

    [Fact]
    public void Add_DropsOldestViolationWhenCapacityIsExceeded()
    {
        var store = new SandboxViolationStore(capacity: 2);

        store.Add(CreateViolation("one"));
        store.Add(CreateViolation("two"));
        store.Add(CreateViolation("three"));

        store.Count.Should().Be(2);
        store.TotalCount.Should().Be(3);
        store.Get().Select(v => v.Message).Should().Equal("two", "three");
    }

    [Fact]
    public void Get_WithLimit_ReturnsMostRecentViolations()
    {
        var store = new SandboxViolationStore(capacity: 5);
        store.Add(CreateViolation("one"));
        store.Add(CreateViolation("two"));
        store.Add(CreateViolation("three"));

        var violations = store.Get(limit: 2);

        violations.Select(v => v.Message).Should().Equal("two", "three");
    }

    [Fact]
    public void GetSinceTotalCount_ReturnsViolationsAfterBaseline()
    {
        var store = new SandboxViolationStore(capacity: 5);
        store.Add(CreateViolation("one"));
        var baseline = store.TotalCount;
        store.Add(CreateViolation("two"));
        store.Add(CreateViolation("three"));

        var violations = store.GetSinceTotalCount(baseline);

        violations.Select(v => v.Message).Should().Equal("two", "three");
    }

    [Fact]
    public void GetSinceTotalCount_WhenBaselineFallsOutOfTail_ReturnsAvailableTail()
    {
        var store = new SandboxViolationStore(capacity: 2);
        store.Add(CreateViolation("one"));
        var baseline = store.TotalCount;
        store.Add(CreateViolation("two"));
        store.Add(CreateViolation("three"));
        store.Add(CreateViolation("four"));

        var violations = store.GetSinceTotalCount(baseline);

        violations.Select(v => v.Message).Should().Equal("three", "four");
    }

    [Fact]
    public void Subscribe_ImmediatelyReceivesCurrentSnapshotAndThenUpdates()
    {
        var store = new SandboxViolationStore(capacity: 3);
        store.Add(CreateViolation("one"));
        var snapshots = new List<IReadOnlyList<SandboxViolation>>();

        using var subscription = store.Subscribe(snapshot => snapshots.Add(snapshot));
        store.Add(CreateViolation("two"));

        snapshots.Should().HaveCount(2);
        snapshots[0].Select(v => v.Message).Should().Equal("one");
        snapshots[1].Select(v => v.Message).Should().Equal("one", "two");
    }

    [Fact]
    public void Subscribe_DisposeStopsUpdates()
    {
        var store = new SandboxViolationStore(capacity: 3);
        var snapshots = new List<IReadOnlyList<SandboxViolation>>();

        var subscription = store.Subscribe(snapshot => snapshots.Add(snapshot));
        subscription.Dispose();
        store.Add(CreateViolation("one"));

        snapshots.Should().ContainSingle()
            .Which.Should().BeEmpty();
    }

    [Fact]
    public void Add_IgnoresSubscriberExceptions()
    {
        var store = new SandboxViolationStore(capacity: 3);
        using var subscription = store.Subscribe(_ => throw new InvalidOperationException());

        var act = () => store.Add(CreateViolation("one"));

        act.Should().NotThrow();
        store.TotalCount.Should().Be(1);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        var act = () => new SandboxViolationStore(capacity: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static SandboxViolation CreateViolation(string message) => new()
    {
        Type = ViolationType.FilesystemWrite,
        Message = message,
        Timestamp = DateTimeOffset.UtcNow,
    };
}
