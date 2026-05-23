using Helium.Algorithms;
using Helium.Primitives;

namespace Helium.Algorithms.Tests;

public class FiniteCategoryTests
{
    [Fact]
    public void ArrowCategory_ComposesIdentitiesAndArrow()
    {
        var category = ArrowCategory();

        Assert.Equal(O(0), category.Source(F()));
        Assert.Equal(O(1), category.Target(F()));
        Assert.True(category.CanCompose(Id1(), F()));
        Assert.False(category.CanCompose(F(), Id1()));
        Assert.Equal(F(), category.Compose(Id1(), F()));
        Assert.Equal(F(), category.Compose(F(), Id0()));
    }

    [Fact]
    public void Constructor_RejectsMissingComposablePair()
    {
        var source = Sources();
        var target = Targets();
        var identities = Identities();
        var composition = Composition();
        composition.Remove((Id1(), F()));

        Assert.Throws<ArgumentException>(() => new FiniteCategory<Fin, Fin>(
            Objects(),
            Morphisms(),
            source,
            target,
            identities,
            composition));
    }

    [Fact]
    public void Constructor_RejectsIdentityLawFailure()
    {
        var composition = Composition();
        composition[(Id1(), F())] = Id1();

        Assert.Throws<ArgumentException>(() => new FiniteCategory<Fin, Fin>(
            Objects(),
            Morphisms(),
            Sources(),
            Targets(),
            Identities(),
            composition));
    }

    [Fact]
    public void IdentityFunctor_PreservesStructure()
    {
        var category = ArrowCategory();
        var functor = IdentityFunctor(category);

        Assert.Equal(O(0), functor.MapObject(O(0)));
        Assert.Equal(F(), functor.MapMorphism(F()));
    }

    [Fact]
    public void Functor_RejectsSourceTargetMismatch()
    {
        var category = ArrowCategory();
        var objectMap = new Dictionary<Fin, Fin>
        {
            [O(0)] = O(0),
            [O(1)] = O(1)
        };
        var morphismMap = new Dictionary<Fin, Fin>
        {
            [Id0()] = Id0(),
            [Id1()] = Id1(),
            [F()] = Id0()
        };

        Assert.Throws<ArgumentException>(() => new FiniteFunctor<Fin, Fin, Fin, Fin>(
            category,
            category,
            objectMap,
            morphismMap));
    }

    [Fact]
    public void IdentityNaturalTransformation_SatisfiesNaturality()
    {
        var category = ArrowCategory();
        var functor = IdentityFunctor(category);
        var natural = new NaturalTransformation<Fin, Fin, Fin, Fin>(
            functor,
            functor,
            new Dictionary<Fin, Fin>
            {
                [O(0)] = Id0(),
                [O(1)] = Id1()
            });

        Assert.Equal(Id0(), natural.Component(O(0)));
        Assert.Equal(Id1(), natural.Component(O(1)));
    }

    [Fact]
    public void NaturalTransformation_RejectsWrongComponentType()
    {
        var category = ArrowCategory();
        var functor = IdentityFunctor(category);

        Assert.Throws<ArgumentException>(() => new NaturalTransformation<Fin, Fin, Fin, Fin>(
            functor,
            functor,
            new Dictionary<Fin, Fin>
            {
                [O(0)] = F(),
                [O(1)] = Id1()
            }));
    }

    private static Fin O(int value) => new(value, 2);
    private static Fin Id0() => new(0, 3);
    private static Fin Id1() => new(1, 3);
    private static Fin F() => new(2, 3);

    private static Finset<Fin> Objects() => Finset<Fin>.Of(O(0), O(1));
    private static Finset<Fin> Morphisms() => Finset<Fin>.Of(Id0(), Id1(), F());

    private static Dictionary<Fin, Fin> Sources() => new()
    {
        [Id0()] = O(0),
        [Id1()] = O(1),
        [F()] = O(0)
    };

    private static Dictionary<Fin, Fin> Targets() => new()
    {
        [Id0()] = O(0),
        [Id1()] = O(1),
        [F()] = O(1)
    };

    private static Dictionary<Fin, Fin> Identities() => new()
    {
        [O(0)] = Id0(),
        [O(1)] = Id1()
    };

    private static Dictionary<(Fin Left, Fin Right), Fin> Composition() => new()
    {
        [(Id0(), Id0())] = Id0(),
        [(Id1(), Id1())] = Id1(),
        [(Id1(), F())] = F(),
        [(F(), Id0())] = F()
    };

    private static FiniteCategory<Fin, Fin> ArrowCategory() =>
        new(Objects(), Morphisms(), Sources(), Targets(), Identities(), Composition());

    private static FiniteFunctor<Fin, Fin, Fin, Fin> IdentityFunctor(FiniteCategory<Fin, Fin> category) =>
        new(
            category,
            category,
            new Dictionary<Fin, Fin>
            {
                [O(0)] = O(0),
                [O(1)] = O(1)
            },
            new Dictionary<Fin, Fin>
            {
                [Id0()] = Id0(),
                [Id1()] = Id1(),
                [F()] = F()
            });
}
