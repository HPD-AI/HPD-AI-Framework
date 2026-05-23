using Helium.Primitives;

namespace Helium.Primitives.Tests.Axioms;

public static class ModuleAxioms
{
    public static void VerifyScalarIdentity<R, M>(M m)
        where R : ICommRing<R>
        where M : IModule<R, M>
    {
        Assert.Equal(m, M.ScalarMultiply(R.MultiplicativeIdentity, m));
    }

    public static void VerifyScalarCompatibility<R, M>(R a, R b, M m)
        where R : ICommRing<R>
        where M : IModule<R, M>
    {
        Assert.Equal(M.ScalarMultiply(a * b, m), M.ScalarMultiply(a, M.ScalarMultiply(b, m)));
    }

    public static void VerifyDistributesOverModuleAddition<R, M>(R a, M m, M n)
        where R : ICommRing<R>
        where M : IModule<R, M>
    {
        Assert.Equal(M.ScalarMultiply(a, m + n), M.ScalarMultiply(a, m) + M.ScalarMultiply(a, n));
    }

    public static void VerifyDistributesOverScalarAddition<R, M>(R a, R b, M m)
        where R : ICommRing<R>
        where M : IModule<R, M>
    {
        Assert.Equal(M.ScalarMultiply(a + b, m), M.ScalarMultiply(a, m) + M.ScalarMultiply(b, m));
    }

    public static void VerifyZeroScalar<R, M>(M m)
        where R : ICommRing<R>
        where M : IModule<R, M>
    {
        Assert.Equal(M.AdditiveIdentity, M.ScalarMultiply(R.AdditiveIdentity, m));
    }

    public static void VerifyAll<R, M>(R a, R b, M m, M n)
        where R : ICommRing<R>
        where M : IModule<R, M>
    {
        AddGroupAxioms.VerifyAll(m, n, m + n);
        VerifyScalarIdentity<R, M>(m);
        VerifyScalarCompatibility<R, M>(a, b, m);
        VerifyDistributesOverModuleAddition<R, M>(a, m, n);
        VerifyDistributesOverScalarAddition<R, M>(a, b, m);
        VerifyZeroScalar<R, M>(m);
    }
}
