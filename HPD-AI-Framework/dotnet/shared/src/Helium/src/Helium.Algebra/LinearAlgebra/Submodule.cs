using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Submodule (subspace) of R^n, represented by a set of spanning vectors.
/// </summary>
public class Submodule<R>
    where R : IRing<R>
{
    private readonly List<Vector<R>> _generators;
    public int AmbientDim { get; }

    private Submodule(int ambientDim, List<Vector<R>> generators)
    {
        AmbientDim = ambientDim;
        _generators = generators;
    }

    public static Submodule<R> Span(int ambientDim, IEnumerable<Vector<R>> generators) =>
        new(ambientDim, generators.ToList());

    public static Submodule<R> Zero(int ambientDim) =>
        new(ambientDim, []);

    public static Submodule<R> Entire(int dim)
    {
        var gens = new List<Vector<R>>();
        for (int i = 0; i < dim; i++)
            gens.Add(Vector<R>.StandardBasis(dim, i));
        return new(dim, gens);
    }

    public IReadOnlyList<Vector<R>> Generators => _generators;

    public Submodule<R> Sum(Submodule<R> other) =>
        new(AmbientDim, [.. _generators, .. other._generators]);
}
