using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class QuotientRingTests
{
    private static QuotientRing<Integer> Mod5(Integer value) => ZMod.Create(value, (Integer)5);
    private static Func<Integer, Integer> Reduce5 = ZMod.Reducer((Integer)5);

    // --- ZMod arithmetic ---

    [Fact]
    public void ZMod5_AdditionWraps()
    {
        var a = Mod5((Integer)3);
        var b = Mod5((Integer)4);
        var sum = a + b;
        Assert.Equal((Integer)2, sum.Representative); // 3 + 4 = 7 ≡ 2 mod 5
    }

    [Fact]
    public void ZMod5_Multiplication()
    {
        var a = Mod5((Integer)3);
        var b = Mod5((Integer)4);
        var product = a * b;
        Assert.Equal((Integer)2, product.Representative); // 3 * 4 = 12 ≡ 2 mod 5
    }

    [Fact]
    public void ZMod5_Negation()
    {
        var a = Mod5((Integer)3);
        var neg = -a;
        Assert.Equal((Integer)2, neg.Representative); // -3 ≡ 2 mod 5
    }

    [Fact]
    public void ZMod5_AdditiveInverse()
    {
        var a = Mod5((Integer)3);
        Assert.Equal((Integer)0, (a + (-a)).Representative);
    }

    [Fact]
    public void ZMod5_Reduction()
    {
        var a = Mod5((Integer)13);
        Assert.Equal((Integer)3, a.Representative); // 13 ≡ 3 mod 5
    }

    [Fact]
    public void ZMod5_NegativeInput()
    {
        var a = Mod5((Integer)(-3));
        Assert.Equal((Integer)2, a.Representative); // -3 ≡ 2 mod 5
    }

    // --- Equality ---

    [Fact]
    public void ZMod5_EqualClasses()
    {
        var a = Mod5((Integer)3);
        var b = Mod5((Integer)8);
        Assert.Equal(a, b); // 3 ≡ 8 mod 5
    }

    [Fact]
    public void ZMod5_DifferentClasses()
    {
        var a = Mod5((Integer)3);
        var b = Mod5((Integer)4);
        Assert.NotEqual(a, b);
    }

    // --- ZMod(1) is zero ring ---

    [Fact]
    public void ZMod1_EverythingIsZero()
    {
        var a = ZMod.Create((Integer)42, (Integer)1);
        Assert.Equal((Integer)0, a.Representative);
    }

    // --- Ideal ---

    [Fact]
    public void PrincipalIdeal()
    {
        var ideal = Ideal<Integer>.Principal((Integer)5);
        Assert.True(ideal.IsPrincipal);
        Assert.Equal((Integer)5, ideal.Generator);
    }

    // --- Context resolution (A5) ---

    [Fact]
    public void SameContext_Allowed()
    {
        // Both elements from ZMod(5) — same context object from cache
        var a = ZMod.Create((Integer)3, (Integer)5);
        var b = ZMod.Create((Integer)4, (Integer)5);
        var sum = a + b; // must not throw
        Assert.Equal((Integer)2, sum.Representative);
    }

    [Fact]
    public void SentinelPlusReal_Allowed()
    {
        // AdditiveIdentity carries sentinel context; adding a real element uses the real context
        var zero = QuotientRing<Integer>.AdditiveIdentity;
        var a = ZMod.Create((Integer)3, (Integer)5);
        var sum = zero + a; // must not throw
        Assert.Equal((Integer)3, sum.Representative);
    }

    [Fact]
    public void DifferentContexts_Throws()
    {
        var a = ZMod.Create((Integer)3, (Integer)5);
        var b = ZMod.Create((Integer)3, (Integer)7);
        Assert.Throws<InvalidOperationException>(() => _ = a + b);
    }
}
