namespace Helium.Primitives;

public readonly struct BoolAlgebra :
    IEquatable<BoolAlgebra>,
    IDecidableEq<BoolAlgebra>,
    IBooleanAlgebra<BoolAlgebra>,
    ICompleteLattice<BoolAlgebra>,
    IFintype<BoolAlgebra>
{
    public bool Value { get; }

    public BoolAlgebra(bool value)
    {
        Value = value;
    }

    public static implicit operator BoolAlgebra(bool value) => new(value);
    public static explicit operator bool(BoolAlgebra value) => value.Value;

    public static BoolAlgebra Top => new(true);
    public static BoolAlgebra Bottom => new(false);
    public static FiniteList<BoolAlgebra> Elements => FiniteList<BoolAlgebra>.Of(Bottom, Top);
    public static int Cardinality => 2;

    public static bool DecidableEquals(BoolAlgebra left, BoolAlgebra right) => left.Value == right.Value;
    public static bool LessEqual(BoolAlgebra left, BoolAlgebra right) => !left.Value || right.Value;
    public static BoolAlgebra Join(BoolAlgebra left, BoolAlgebra right) => new(left.Value || right.Value);
    public static BoolAlgebra Meet(BoolAlgebra left, BoolAlgebra right) => new(left.Value && right.Value);
    public static BoolAlgebra Complement(BoolAlgebra value) => new(!value.Value);

    public static BoolAlgebra Supremum(FiniteList<BoolAlgebra> values) =>
        values.FoldLeft(Bottom, Join);

    public static BoolAlgebra Infimum(FiniteList<BoolAlgebra> values) =>
        values.FoldLeft(Top, Meet);

    public bool Equals(BoolAlgebra other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is BoolAlgebra other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(BoolAlgebra left, BoolAlgebra right) => left.Equals(right);
    public static bool operator !=(BoolAlgebra left, BoolAlgebra right) => !left.Equals(right);
    public override string ToString() => Value ? "true" : "false";
}
