namespace HPD.Math.Core;

/// <summary>
/// Executable equality operations for AOT-friendly kernels.
/// </summary>
public interface IEqualityOps<T>
{
    bool Eq(in T left, in T right);
}

/// <summary>
/// Executable preorder operations.
/// </summary>
public interface IPreorderOps<T> : IEqualityOps<T>
{
    bool LessEqual(in T left, in T right);
}

/// <summary>
/// Executable partial order operations with decidable equality.
/// </summary>
public interface IPartialOrderOps<T> : IPreorderOps<T>
{
}

/// <summary>
/// Executable total order operations.
/// </summary>
public interface ITotalOrderOps<T> : IPartialOrderOps<T>
{
    Ordering Compare(in T left, in T right);
}

/// <summary>
/// Finite, executable enumeration of values of a type.
/// </summary>
public interface IFiniteEnumerationOps<T> : IEqualityOps<T>
{
    int Cardinality { get; }

    AlgebraStatus TryGetElement(int index, out T value);

    AlgebraStatus TryFill(Span<T> destination);
}

/// <summary>
/// Additive commutative monoid operations.
/// </summary>
public interface IAdditiveCommutativeMonoidOps<T> : IEqualityOps<T>
{
    T Zero { get; }

    void Add(ref T destination, in T left, in T right);
}

/// <summary>
/// Additive commutative group operations.
/// </summary>
public interface IAdditiveCommutativeGroupOps<T> : IAdditiveCommutativeMonoidOps<T>
{
    void Neg(ref T destination, in T value);

    void Sub(ref T destination, in T left, in T right);
}

/// <summary>
/// Multiplicative monoid operations.
/// </summary>
public interface IMultiplicativeMonoidOps<T> : IEqualityOps<T>
{
    T One { get; }

    void Mul(ref T destination, in T left, in T right);
}

/// <summary>
/// Multiplicative group operations.
/// </summary>
public interface IGroupOps<T> : IMultiplicativeMonoidOps<T>
{
    AlgebraStatus TryInvert(ref T destination, in T value);
}

/// <summary>
/// Semiring operations: additive commutative monoid and multiplicative monoid.
/// </summary>
public interface ISemiringOps<T> :
    IAdditiveCommutativeMonoidOps<T>,
    IMultiplicativeMonoidOps<T>
{
}

/// <summary>
/// Ring operations that write through caller-owned storage.
/// </summary>
public interface IRingOps<T> :
    ISemiringOps<T>,
    IAdditiveCommutativeGroupOps<T>
{
}

/// <summary>
/// Commutative ring marker for operation witnesses.
/// </summary>
public interface ICommutativeRingOps<T> : IRingOps<T>
{
}

/// <summary>
/// Ring operations with an executable embedding of small integers.
/// </summary>
public interface IIntegerEmbeddingOps<T> : ICommutativeRingOps<T>
{
    AlgebraStatus TryFromInt(int value, out T result);
}

/// <summary>
/// Division ring operations. Multiplication need not be commutative.
/// </summary>
public interface IDivisionRingOps<T> : IRingOps<T>, IGroupOps<T>
{
}

/// <summary>
/// Field operations. Failure is explicit because inversion is partial in executable kernels.
/// </summary>
public interface IFieldOps<T> : ICommutativeRingOps<T>, IDivisionRingOps<T>
{
}

/// <summary>
/// Bounded ring operations where arithmetic failure is reported as data.
/// </summary>
public interface IStatusRingOps<T> : IEqualityOps<T>
{
    T Zero { get; }

    T One { get; }

    AlgebraStatus TryAdd(ref T destination, in T left, in T right);

    AlgebraStatus TrySub(ref T destination, in T left, in T right);

    AlgebraStatus TryMul(ref T destination, in T left, in T right);

    AlgebraStatus TryNeg(ref T destination, in T value);
}

/// <summary>
/// Bounded field operations where inversion and arithmetic failure are reported as data.
/// </summary>
public interface IStatusFieldOps<T> : IStatusRingOps<T>
{
    AlgebraStatus TryInvert(ref T destination, in T value);
}

/// <summary>
/// Module operations for an additive commutative group acted on by a scalar ring.
/// </summary>
public interface IModuleOps<TScalar, TElement> : IAdditiveCommutativeGroupOps<TElement>
{
    void Scale(ref TElement destination, in TScalar scalar, in TElement element);
}

/// <summary>
/// A lattice over a decidable partial order.
/// </summary>
public interface ILatticeOps<T> : IPartialOrderOps<T>
{
    void Join(ref T destination, in T left, in T right);

    void Meet(ref T destination, in T left, in T right);
}

/// <summary>
/// A lattice with top and bottom elements.
/// </summary>
public interface IBoundedLatticeOps<T> : ILatticeOps<T>
{
    T Top { get; }

    T Bottom { get; }
}

/// <summary>
/// Marker for distributive lattice operation witnesses.
/// </summary>
public interface IDistributiveLatticeOps<T> : ILatticeOps<T>
{
}

/// <summary>
/// A Boolean algebra over executable lattice operations.
/// </summary>
public interface IBooleanAlgebraOps<T> : IBoundedLatticeOps<T>, IDistributiveLatticeOps<T>
{
    void Complement(ref T destination, in T value);
}

/// <summary>
/// Finite complete lattice operations. The finite family is supplied by caller-owned storage.
/// </summary>
public interface ICompleteFiniteLatticeOps<T> : IBoundedLatticeOps<T>
{
    AlgebraStatus TrySupremum(ref T destination, ReadOnlySpan<T> values);

    AlgebraStatus TryInfimum(ref T destination, ReadOnlySpan<T> values);
}

/// <summary>
/// Executable order-preserving map witness. Monotonicity is validated by finite kernels.
/// </summary>
public interface IOrderHomOps<TSource, TTarget>
{
    void Apply(ref TTarget destination, in TSource source);
}
