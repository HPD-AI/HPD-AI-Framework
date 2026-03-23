using FluentAssertions;
using HPD.Sandbox.Local.State;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public class SandboxStateDataTests
{
    [Fact]
    public void Default_HasEmptyBlockedFunctions()
    {
        var state = new SandboxStateData();

        state.BlockedFunctions.Should().BeEmpty();
        state.ViolationCount.Should().Be(0);
    }

    [Fact]
    public void WithBlockedFunction_AddsFunction()
    {
        var state = new SandboxStateData();

        var newState = state.WithBlockedFunction("dangerous_func");

        newState.BlockedFunctions.Should().Contain("dangerous_func");
        newState.ViolationCount.Should().Be(1);
    }

    [Fact]
    public void WithBlockedFunction_IncrementsViolationCount()
    {
        var state = new SandboxStateData();

        var newState = state
            .WithBlockedFunction("func1")
            .WithBlockedFunction("func2")
            .WithBlockedFunction("func3");

        newState.ViolationCount.Should().Be(3);
        newState.BlockedFunctions.Should().HaveCount(3);
    }

    [Fact]
    public void WithBlockedFunction_IsImmutable()
    {
        var original = new SandboxStateData();

        var modified = original.WithBlockedFunction("blocked");

        original.BlockedFunctions.Should().BeEmpty();
        original.ViolationCount.Should().Be(0);
        modified.BlockedFunctions.Should().Contain("blocked");
    }

    [Fact]
    public void WithBlockedFunction_HandlesDuplicates()
    {
        var state = new SandboxStateData()
            .WithBlockedFunction("func")
            .WithBlockedFunction("func"); // Same function

        // ImmutableHashSet deduplicates
        state.BlockedFunctions.Should().HaveCount(1);
        // But violation count still increments
        state.ViolationCount.Should().Be(2);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var state = new SandboxStateData()
            .WithBlockedFunction("func1")
            .WithBlockedFunction("func2");

        var reset = state.Reset();

        reset.BlockedFunctions.Should().BeEmpty();
        reset.ViolationCount.Should().Be(0);
    }

    [Fact]
    public void Reset_IsImmutable()
    {
        var original = new SandboxStateData()
            .WithBlockedFunction("func");

        var reset = original.Reset();

        original.BlockedFunctions.Should().Contain("func");
        reset.BlockedFunctions.Should().BeEmpty();
    }
}
