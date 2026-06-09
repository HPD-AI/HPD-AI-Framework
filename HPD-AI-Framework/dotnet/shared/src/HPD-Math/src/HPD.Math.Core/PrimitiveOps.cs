namespace HPD.Math.Core;

/// <summary>
/// Total order for <see cref="int"/>.
/// </summary>
public readonly struct Int32OrderOps : ITotalOrderOps<int>
{
    public bool Eq(in int left, in int right) => left == right;

    public bool LessEqual(in int left, in int right) => left <= right;

    public Ordering Compare(in int left, in int right) =>
        left < right ? Ordering.Less :
        left > right ? Ordering.Greater :
        Ordering.Equal;
}

/// <summary>
/// Checked integer ring operations. Useful for bootstrapping kernels and tests.
/// </summary>
public readonly struct CheckedInt32RingOps : IIntegerEmbeddingOps<int>
{
    public int Zero => 0;
    public int One => 1;

    public bool Eq(in int left, in int right) => left == right;

    public void Add(ref int destination, in int left, in int right) =>
        destination = checked(left + right);

    public void Sub(ref int destination, in int left, in int right) =>
        destination = checked(left - right);

    public void Mul(ref int destination, in int left, in int right) =>
        destination = checked(left * right);

    public void Neg(ref int destination, in int value) =>
        destination = checked(-value);

    public AlgebraStatus TryFromInt(int value, out int result)
    {
        result = value;
        return AlgebraStatus.Ok;
    }
}

/// <summary>
/// Boolean algebra operations for <see cref="bool"/>.
/// </summary>
public readonly struct BoolAlgebraOps :
    IBooleanAlgebraOps<bool>,
    ICompleteFiniteLatticeOps<bool>,
    IFiniteEnumerationOps<bool>
{
    public bool Top => true;

    public bool Bottom => false;

    public int Cardinality => 2;

    public bool Eq(in bool left, in bool right) => left == right;

    public bool LessEqual(in bool left, in bool right) => !left || right;

    public AlgebraStatus TryGetElement(int index, out bool value)
    {
        value = index switch
        {
            0 => false,
            1 => true,
            _ => false
        };

        return index is 0 or 1 ? AlgebraStatus.Ok : AlgebraStatus.InvalidInput;
    }

    public AlgebraStatus TryFill(Span<bool> destination)
    {
        if (destination.Length < Cardinality)
            return AlgebraStatus.InsufficientDestination;

        destination[0] = false;
        destination[1] = true;
        return AlgebraStatus.Ok;
    }

    public void Join(ref bool destination, in bool left, in bool right) =>
        destination = left || right;

    public void Meet(ref bool destination, in bool left, in bool right) =>
        destination = left && right;

    public void Complement(ref bool destination, in bool value) =>
        destination = !value;

    public AlgebraStatus TrySupremum(ref bool destination, ReadOnlySpan<bool> values)
    {
        destination = Bottom;
        for (var i = 0; i < values.Length; i++)
            destination |= values[i];

        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryInfimum(ref bool destination, ReadOnlySpan<bool> values)
    {
        destination = Top;
        for (var i = 0; i < values.Length; i++)
            destination &= values[i];

        return AlgebraStatus.Ok;
    }
}
