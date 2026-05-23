using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Finite group ring over a commutative ordered coefficient ring and ordered group basis.
/// </summary>
public readonly struct GroupRing<R, G> :
    IHopfAlgebra<R, GroupRing<R, G>>,
    ITotalOrder<GroupRing<R, G>>,
    IEquatable<GroupRing<R, G>>,
    IFormattable
    where R : ICommRing<R>, ITotalOrder<R>
    where G : IGroup<G>, ITotalOrder<G>
{
    private readonly Finsupp<G, R> _coefficients;

    private GroupRing(Finsupp<G, R> coefficients)
    {
        _coefficients = coefficients;
    }

    static GroupRing<R, G> System.Numerics.IAdditiveIdentity<GroupRing<R, G>, GroupRing<R, G>>.AdditiveIdentity => Zero;

    public static GroupRing<R, G> Zero => new(Finsupp<G, R>.Empty);

    public bool IsZero => _coefficients.IsZero;

    public IEnumerable<(G Basis, R Coefficient)> Components
    {
        get
        {
            var coefficients = _coefficients;
            foreach (var basis in coefficients.Support)
                yield return (basis, coefficients[basis]);
        }
    }

    public static GroupRing<R, G> Basis(G basis) =>
        Term(basis, R.MultiplicativeIdentity);

    public static GroupRing<R, G> Term(G basis, R coefficient) =>
        new(Finsupp<G, R>.Single(basis, coefficient));

    public static GroupRing<R, G> Unit(R scalar) =>
        Term(G.Identity, scalar);

    public R Coefficient(G basis) => _coefficients[basis];

    public static GroupRing<R, G> operator +(GroupRing<R, G> left, GroupRing<R, G> right) =>
        new(left._coefficients + right._coefficients);

    public static GroupRing<R, G> operator -(GroupRing<R, G> left, GroupRing<R, G> right) =>
        new(left._coefficients - right._coefficients);

    public static GroupRing<R, G> operator -(GroupRing<R, G> value) =>
        new(-value._coefficients);

    public static GroupRing<R, G> operator *(R scalar, GroupRing<R, G> value) =>
        new(value._coefficients.ScalarMultiply(scalar));

    public static GroupRing<R, G> operator *(GroupRing<R, G> left, GroupRing<R, G> right) =>
        Multiply(left, right);

    public static GroupRing<R, G> ScalarMultiply(R scalar, GroupRing<R, G> element) =>
        scalar * element;

    public static GroupRing<R, G> Multiply(GroupRing<R, G> left, GroupRing<R, G> right)
    {
        var result = Zero;
        foreach (var (leftBasis, leftCoefficient) in left.Components)
        foreach (var (rightBasis, rightCoefficient) in right.Components)
        {
            var basis = G.Multiply(leftBasis, rightBasis);
            var coefficient = leftCoefficient * rightCoefficient;
            result += Term(basis, coefficient);
        }

        return result;
    }

    public static TensorProduct<R, GroupRing<R, G>, GroupRing<R, G>> Comultiplication(GroupRing<R, G> value)
    {
        var result = TensorProduct<R, GroupRing<R, G>, GroupRing<R, G>>.Zero;
        foreach (var (basis, coefficient) in value.Components)
        {
            var basisElement = Basis(basis);
            result += TensorProduct<R, GroupRing<R, G>, GroupRing<R, G>>.Elementary(basisElement, basisElement, coefficient);
        }

        return result;
    }

    public static R Counit(GroupRing<R, G> value)
    {
        var result = R.AdditiveIdentity;
        foreach (var (_, coefficient) in value.Components)
            result += coefficient;
        return result;
    }

    public static GroupRing<R, G> Antipode(GroupRing<R, G> value)
    {
        var result = Zero;
        foreach (var (basis, coefficient) in value.Components)
            result += Term(G.Invert(basis), coefficient);
        return result;
    }

    public static bool DecidableEquals(GroupRing<R, G> left, GroupRing<R, G> right) => left == right;

    public static bool LessEqual(GroupRing<R, G> left, GroupRing<R, G> right) =>
        CompareOrder(left, right) != Ordering.Greater;

    public static Ordering CompareOrder(GroupRing<R, G> left, GroupRing<R, G> right)
    {
        using var leftEnumerator = left.Components.GetEnumerator();
        using var rightEnumerator = right.Components.GetEnumerator();

        while (true)
        {
            var hasLeft = leftEnumerator.MoveNext();
            var hasRight = rightEnumerator.MoveNext();

            if (!hasLeft && !hasRight) return Ordering.Equal;
            if (!hasLeft) return Ordering.Less;
            if (!hasRight) return Ordering.Greater;

            var basisCompare = G.CompareOrder(leftEnumerator.Current.Basis, rightEnumerator.Current.Basis);
            if (basisCompare != Ordering.Equal)
                return basisCompare;

            var coefficientCompare = R.CompareOrder(leftEnumerator.Current.Coefficient, rightEnumerator.Current.Coefficient);
            if (coefficientCompare != Ordering.Equal)
                return coefficientCompare;
        }
    }

    public bool Equals(GroupRing<R, G> other) => _coefficients == other._coefficients;
    public override bool Equals(object? obj) => obj is GroupRing<R, G> other && Equals(other);
    public override int GetHashCode() => _coefficients.GetHashCode();
    public static bool operator ==(GroupRing<R, G> left, GroupRing<R, G> right) => left.Equals(right);
    public static bool operator !=(GroupRing<R, G> left, GroupRing<R, G> right) => !left.Equals(right);

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider)
    {
        if (IsZero)
            return "0";

        return string.Join(" + ", Components.Select(c => $"{FormatHelpers.FormatElement(c.Coefficient, format, provider)}*{c.Basis}"));
    }
}
