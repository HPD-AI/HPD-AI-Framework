namespace HPD.Math.Core;

/// <summary>
/// Requests generation of an <see cref="IStaticDimension"/> witness.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class DimensionAttribute : Attribute
{
    public DimensionAttribute(int value)
    {
        Value = value;
    }

    public int Value { get; }
}

/// <summary>
/// Requests generation of an <see cref="IStaticPrecision"/> witness.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class PrecisionAttribute : Attribute
{
    public PrecisionAttribute(int value)
    {
        Value = value;
    }

    public int Value { get; }
}

/// <summary>
/// Requests generation of an <see cref="IPrimeModulus"/> witness.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class PrimeModulusAttribute : Attribute
{
    public PrimeModulusAttribute(int value)
    {
        Value = value;
    }

    public int Value { get; }
}

/// <summary>
/// Requests generation of a v2 univariate polynomial mathematical context.
/// A context names the universe and exposes caller-owned scope factories.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class PolynomialContextAttribute : Attribute
{
    public PolynomialContextAttribute(Type coefficientType, Type coefficientOpsType)
    {
        CoefficientType = coefficientType;
        CoefficientOpsType = coefficientOpsType;
    }

    public Type CoefficientType { get; }

    public Type CoefficientOpsType { get; }

    public int Terms { get; set; } = 32;

    public int Workspace { get; set; } = 64;

    public int Handles { get; set; } = 16;
}

/// <summary>
/// Requests generation of a first-class bounded sparse univariate polynomial context.
/// Terms is the maximum finite support size carried by each generated value.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class SparsePolynomialContextAttribute : Attribute
{
    public SparsePolynomialContextAttribute(Type coefficientType, Type coefficientOpsType)
    {
        CoefficientType = coefficientType;
        CoefficientOpsType = coefficientOpsType;
    }

    public Type CoefficientType { get; }

    public Type CoefficientOpsType { get; }

    public int Terms { get; set; } = 32;
}

/// <summary>
/// Requests generation of a v2 scope-first univariate polynomial authoring surface.
/// Terms is the per-handle term capacity; Workspace is the multiplication workspace capacity.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class PolynomialScopeAttribute : Attribute
{
    public PolynomialScopeAttribute(Type coefficientType, Type coefficientOpsType)
    {
        CoefficientType = coefficientType;
        CoefficientOpsType = coefficientOpsType;
    }

    public Type CoefficientType { get; }

    public Type CoefficientOpsType { get; }

    public int Terms { get; set; } = 32;

    public int Workspace { get; set; } = 64;

    public int Handles { get; set; } = 16;
}

/// <summary>
/// Requests generation of a v2 fixed-size dense matrix mathematical context.
/// A context names the universe and exposes caller-owned scope factories.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class MatrixContextAttribute : Attribute
{
    public MatrixContextAttribute(Type elementType, Type elementOpsType)
    {
        ElementType = elementType;
        ElementOpsType = elementOpsType;
    }

    public Type ElementType { get; }

    public Type ElementOpsType { get; }

    public int Rows { get; set; } = 2;

    public int Columns { get; set; } = 2;

    public int Handles { get; set; } = 16;
}

/// <summary>
/// Requests generation of a v2 scope-first fixed-size dense matrix authoring surface.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class MatrixScopeAttribute : Attribute
{
    public MatrixScopeAttribute(Type elementType, Type elementOpsType)
    {
        ElementType = elementType;
        ElementOpsType = elementOpsType;
    }

    public Type ElementType { get; }

    public Type ElementOpsType { get; }

    public int Rows { get; set; } = 2;

    public int Columns { get; set; } = 2;

    public int Handles { get; set; } = 16;
}

/// <summary>
/// Requests generation of a v2 reverse-mode autodiff mathematical context.
/// A context names the universe and exposes caller-owned scope factories.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class ReverseDiffContextAttribute : Attribute
{
    public ReverseDiffContextAttribute(Type scalarType, Type scalarOpsType)
    {
        ScalarType = scalarType;
        ScalarOpsType = scalarOpsType;
    }

    public Type ScalarType { get; }

    public Type ScalarOpsType { get; }

    public int Nodes { get; set; } = 32;
}

/// <summary>
/// Requests generation of a v2 scope-first reverse-mode autodiff authoring surface.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class ReverseDiffScopeAttribute : Attribute
{
    public ReverseDiffScopeAttribute(Type scalarType, Type scalarOpsType)
    {
        ScalarType = scalarType;
        ScalarOpsType = scalarOpsType;
    }

    public Type ScalarType { get; }

    public Type ScalarOpsType { get; }

    public int Nodes { get; set; } = 32;
}

/// <summary>
/// Requests generation of a v2 univariate polynomial quotient-ring mathematical context.
/// A context names the universe and exposes caller-owned scope factories.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class PolynomialQuotientContextAttribute : Attribute
{
    public PolynomialQuotientContextAttribute(Type coefficientType, Type coefficientOpsType)
    {
        CoefficientType = coefficientType;
        CoefficientOpsType = coefficientOpsType;
    }

    public Type CoefficientType { get; }

    public Type CoefficientOpsType { get; }

    public int Terms { get; set; } = 8;

    public int Handles { get; set; } = 16;

    public int Workspace { get; set; } = 16;
}

/// <summary>
/// Requests generation of a v2 scope-first univariate polynomial quotient-ring authoring surface.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class PolynomialQuotientScopeAttribute : Attribute
{
    public PolynomialQuotientScopeAttribute(Type coefficientType, Type coefficientOpsType)
    {
        CoefficientType = coefficientType;
        CoefficientOpsType = coefficientOpsType;
    }

    public Type CoefficientType { get; }

    public Type CoefficientOpsType { get; }

    public int Terms { get; set; } = 8;

    public int Handles { get; set; } = 16;

    public int Workspace { get; set; } = 16;
}

/// <summary>
/// Requests generation of a v2 univariate rational-function mathematical context.
/// A context names the universe and exposes caller-owned scope factories.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class RationalFunctionContextAttribute : Attribute
{
    public RationalFunctionContextAttribute(Type coefficientType, Type coefficientOpsType)
    {
        CoefficientType = coefficientType;
        CoefficientOpsType = coefficientOpsType;
    }

    public Type CoefficientType { get; }

    public Type CoefficientOpsType { get; }

    public int Terms { get; set; } = 8;

    public int Handles { get; set; } = 16;

    public int Workspace { get; set; } = 16;
}

/// <summary>
/// Requests generation of a v2 scope-first univariate rational-function authoring surface.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class RationalFunctionScopeAttribute : Attribute
{
    public RationalFunctionScopeAttribute(Type coefficientType, Type coefficientOpsType)
    {
        CoefficientType = coefficientType;
        CoefficientOpsType = coefficientOpsType;
    }

    public Type CoefficientType { get; }

    public Type CoefficientOpsType { get; }

    public int Terms { get; set; } = 8;

    public int Handles { get; set; } = 16;

    public int Workspace { get; set; } = 16;
}

/// <summary>
/// Requests generation of a v2 algebraic field-extension mathematical context.
/// A context names the universe and exposes caller-owned scope factories.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class FieldExtensionContextAttribute : Attribute
{
    public FieldExtensionContextAttribute(Type coefficientType, Type coefficientOpsType)
    {
        CoefficientType = coefficientType;
        CoefficientOpsType = coefficientOpsType;
    }

    public Type CoefficientType { get; }

    public Type CoefficientOpsType { get; }

    public int Terms { get; set; } = 8;

    public int Handles { get; set; } = 16;

    public int Workspace { get; set; } = 16;
}

/// <summary>
/// Requests generation of a v2 scope-first algebraic field-extension authoring surface.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class FieldExtensionScopeAttribute : Attribute
{
    public FieldExtensionScopeAttribute(Type coefficientType, Type coefficientOpsType)
    {
        CoefficientType = coefficientType;
        CoefficientOpsType = coefficientOpsType;
    }

    public Type CoefficientType { get; }

    public Type CoefficientOpsType { get; }

    public int Terms { get; set; } = 8;

    public int Handles { get; set; } = 16;

    public int Workspace { get; set; } = 16;
}

/// <summary>
/// Requests generation of a v2 truncated p-adic mathematical context.
/// A context names the universe and exposes caller-owned scope factories.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class PadicContextAttribute : Attribute
{
    public PadicContextAttribute(Type primeType, Type precisionType)
    {
        PrimeType = primeType;
        PrecisionType = precisionType;
    }

    public Type PrimeType { get; }

    public Type PrecisionType { get; }

    public int Handles { get; set; } = 16;
}

/// <summary>
/// Requests generation of a v2 scope-first truncated p-adic authoring surface.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class PadicScopeAttribute : Attribute
{
    public PadicScopeAttribute(Type primeType, Type precisionType)
    {
        PrimeType = primeType;
        PrecisionType = precisionType;
    }

    public Type PrimeType { get; }

    public Type PrecisionType { get; }

    public int Handles { get; set; } = 16;
}

/// <summary>
/// Requests generation of a v2 truncated p-typical Witt vector mathematical context.
/// A context names the universe and exposes caller-owned scope factories.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class WittVectorContextAttribute : Attribute
{
    public WittVectorContextAttribute(Type componentType, Type componentOpsType, Type primeType, int length)
    {
        ComponentType = componentType;
        ComponentOpsType = componentOpsType;
        PrimeType = primeType;
        Length = length;
    }

    public Type ComponentType { get; }

    public Type ComponentOpsType { get; }

    public Type PrimeType { get; }

    public int Length { get; }
}

/// <summary>
/// Requests generation of a v2 scope-first truncated p-typical Witt vector authoring surface.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class WittVectorScopeAttribute : Attribute
{
    public WittVectorScopeAttribute(Type componentType, Type componentOpsType, Type primeType, Type lengthType)
    {
        ComponentType = componentType;
        ComponentOpsType = componentOpsType;
        PrimeType = primeType;
        LengthType = lengthType;
    }

    public Type ComponentType { get; }

    public Type ComponentOpsType { get; }

    public Type PrimeType { get; }

    public Type LengthType { get; }

    public int Handles { get; set; } = 16;
}

/// <summary>
/// Requests generation of a first-class finite powerset context backed by inline storage.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class FinitePowerSetContextAttribute : Attribute
{
    public FinitePowerSetContextAttribute(int cardinality)
    {
        Cardinality = cardinality;
    }

    public int Cardinality { get; }
}
