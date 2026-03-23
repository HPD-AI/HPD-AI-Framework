using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class SeededRandomTests
{
    [Fact]
    public void Create_WithSeed_Deterministic()
    {
        var a = SeededRandom.Create(42);
        var b = SeededRandom.Create(42);

        var seqA = Enumerable.Range(0, 10).Select(_ => a.Next()).ToList();
        var seqB = Enumerable.Range(0, 10).Select(_ => b.Next()).ToList();

        Assert.Equal(seqA, seqB);
    }

    [Fact]
    public void Create_NullSeed_ReturnsShared()
    {
        var rng = SeededRandom.Create(null);
        Assert.Same(Random.Shared, rng);
    }

    [Fact]
    public void Derive_WithSeed_ReturnsDerivedSeed()
    {
        var d0 = SeededRandom.Derive(42, 0);
        var d1 = SeededRandom.Derive(42, 1);

        Assert.NotNull(d0);
        Assert.NotNull(d1);
        Assert.NotEqual(d0, d1);
    }

    [Fact]
    public void Derive_NullSeed_ReturnsNull()
    {
        Assert.Null(SeededRandom.Derive(null, 0));
    }

    [Fact]
    public void Derive_SameInputs_SameOutput()
    {
        var a = SeededRandom.Derive(42, 3);
        var b = SeededRandom.Derive(42, 3);

        Assert.Equal(a, b);
    }
}
