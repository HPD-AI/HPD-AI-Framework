using Rhodium.Primitives;
using Rhodium.Quant;

namespace Rhodium.Quant.Tests;

/// <summary>
/// Tests for QuantRequest gating key.
/// </summary>
public class QuantRequestTests
{
    [Fact]
    public void Constructor_SetsSequenceAndVersion()
    {
        var sequence = new Sequence(100);
        var version = 5;

        var request = new QuantRequest(sequence, version);

        Assert.Equal(sequence, request.Sequence);
        Assert.Equal(version, request.BatchMapVersion);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var request1 = new QuantRequest(new Sequence(100), 5);
        var request2 = new QuantRequest(new Sequence(100), 5);

        Assert.Equal(request1, request2);
        Assert.True(request1 == request2);
        Assert.False(request1 != request2);
    }

    [Fact]
    public void Equality_DifferentSequence_AreNotEqual()
    {
        var request1 = new QuantRequest(new Sequence(100), 5);
        var request2 = new QuantRequest(new Sequence(101), 5);

        Assert.NotEqual(request1, request2);
        Assert.False(request1 == request2);
        Assert.True(request1 != request2);
    }

    [Fact]
    public void Equality_DifferentVersion_AreNotEqual()
    {
        var request1 = new QuantRequest(new Sequence(100), 5);
        var request2 = new QuantRequest(new Sequence(100), 6);

        Assert.NotEqual(request1, request2);
        Assert.False(request1 == request2);
        Assert.True(request1 != request2);
    }

    [Fact]
    public void GetHashCode_SameValues_SameHash()
    {
        var request1 = new QuantRequest(new Sequence(100), 5);
        var request2 = new QuantRequest(new Sequence(100), 5);

        Assert.Equal(request1.GetHashCode(), request2.GetHashCode());
    }

    [Fact]
    public void Deconstruct_ExtractsValues()
    {
        var request = new QuantRequest(new Sequence(100), 5);

        var (sequence, version) = request;

        Assert.Equal(new Sequence(100), sequence);
        Assert.Equal(5, version);
    }

    [Fact]
    public void Request_WithZeroSequence_IsValid()
    {
        var request = new QuantRequest(new Sequence(0), 0);

        Assert.Equal(new Sequence(0), request.Sequence);
        Assert.Equal(0, request.BatchMapVersion);
    }

    [Fact]
    public void Request_WithLargeValues_HandlesCorrectly()
    {
        var request = new QuantRequest(new Sequence(long.MaxValue), int.MaxValue);

        Assert.Equal(new Sequence(long.MaxValue), request.Sequence);
        Assert.Equal(int.MaxValue, request.BatchMapVersion);
    }
}
